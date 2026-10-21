using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record CapabilityResult(decimal? Mean, decimal? Min, decimal? Max, decimal? OverallStdDev,
    decimal? WithinStdDev, decimal? Cp, decimal? Cpk, decimal? Pp, decimal? Ppk, int Count, int OutOfSpecCount,
    int SubgroupCount, string WithinMethod);

public sealed class CapabilityCalculationService(ISpcRepository repository)
{
    private sealed record Group(string Stream, DateTimeOffset At, InspectionMeasurement[] Measurements, bool Complete);

    public CapabilityResult Calculate(IEnumerable<InspectionMeasurement> candidates, decimal? lsl, decimal? usl)
    {
        var lookup = candidates.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var groups = new List<Group>();
        var used = new HashSet<Guid>();
        bool IsFullFailedGroup(InspectionMeasurement[] rows)
        {
            var first = rows[0];
            return (from part in repository.Parts
                join operation in repository.Operations on part.Id equals operation.PartId
                join process in repository.Processes on operation.ProcessId equals process.Id
                join characteristic in repository.Characteristics on operation.Id equals characteristic.OperationId
                join plan in repository.InspectionPlans on characteristic.Id equals plan.CharacteristicId
                where part.PartNum.Equals(first.PartNum,StringComparison.OrdinalIgnoreCase) &&
                    process.ProcessCode.Equals(first.ProcessCode,StringComparison.OrdinalIgnoreCase) && operation.OperationSeq == first.OperationSeq &&
                    characteristic.Name.Equals(first.CharacteristicName,StringComparison.OrdinalIgnoreCase) &&
                    plan.InspectionPhase.Equals(first.InspectionPhase,StringComparison.OrdinalIgnoreCase) && plan.SampleSize == rows.Length
                select plan).Any();
        }
        void Add(IEnumerable<Guid> ids, DateTimeOffset at, bool complete)
        {
            foreach (var stream in ids.Distinct().Where(id => lookup.ContainsKey(id) && !used.Contains(id)).Select(id => lookup[id])
                .GroupBy(item => $"{item.JobNum}|{item.PartNum}|{item.ProcessCode}|{item.OperationSeq}|{item.ResourceId}|{item.InspectionPhase}|{item.CharacteristicName}".ToLowerInvariant()))
            {
                var rows = stream.ToArray();
                foreach (var row in rows) used.Add(row.Id);
                groups.Add(new Group(stream.Key, at, rows, complete || IsFullFailedGroup(rows)));
            }
        }
        foreach (var run in repository.JobPhaseCompletions.OrderBy(item => item.CompletedAt)) Add(run.MeasurementIds, run.CompletedAt, true);
        foreach (var failure in repository.FailedInspections.OrderBy(item => item.FailedAt)) Add(failure.MeasurementIds, failure.FailedAt, false);
        var values = groups.SelectMany(group => group.Measurements).Select(item => item.Value).ToArray();
        var mean = values.Length == 0 ? (decimal?)null : values.Average();
        var overall = SampleStdDev(values);
        var full = groups.Where(group => group.Complete).ToArray();
        var multi = full.Where(group => group.Measurements.Length > 1).ToArray();
        decimal? within = null;
        var method = "Insufficient completed inspection groups";
        if (multi.Length >= 2)
        {
            var degrees = multi.Sum(group => group.Measurements.Length - 1);
            var sum = multi.Sum(group => SquaredDeviations(group.Measurements.Select(item => item.Value).ToArray()));
            within = Sqrt(sum / degrees);
            method = "Pooled within-inspection standard deviation (no bias correction)";
        }
        else if (full.Length >= 2 && full.All(group => group.Measurements.Length == 1))
        {
            var ranges = full.GroupBy(group => group.Stream).SelectMany(stream => {
                var ordered = stream.OrderBy(group => group.At).ToArray();
                return ordered.Zip(ordered.Skip(1), (a,b) => Math.Abs(b.Measurements[0].Value - a.Measurements[0].Value));
            }).ToArray();
            if (ranges.Length > 0) { within = ranges.Average() / 1.128m; method = "Average moving range / 1.128 (single-sample inspections)"; }
        }
        decimal? SpreadIndex(decimal? sigma) => sigma > 0 && lsl.HasValue && usl.HasValue && usl > lsl
            ? (usl.Value - lsl.Value) / (6m * sigma.Value) : null;
        decimal? CenteredIndex(decimal? sigma)
        {
            if (!(sigma > 0) || !mean.HasValue || (!lsl.HasValue && !usl.HasValue) || (lsl.HasValue && usl.HasValue && usl <= lsl)) return null;
            var lower = lsl.HasValue ? (mean.Value - lsl.Value) / (3m * sigma.Value) : (decimal?)null;
            var upper = usl.HasValue ? (usl.Value - mean.Value) / (3m * sigma.Value) : (decimal?)null;
            return lower.HasValue && upper.HasValue ? Math.Min(lower.Value,upper.Value) : lower ?? upper;
        }
        return new(mean, values.Length == 0 ? null : values.Min(), values.Length == 0 ? null : values.Max(), overall, within,
            SpreadIndex(within), CenteredIndex(within), SpreadIndex(overall), CenteredIndex(overall), values.Length,
            values.Count(v => (lsl.HasValue && v < lsl) || (usl.HasValue && v > usl)), full.Length, method);
    }

    private static decimal SquaredDeviations(decimal[] values)
    {
        var mean = values.Average();
        return values.Sum(value => (value - mean) * (value - mean));
    }
    private static decimal? SampleStdDev(decimal[] values) => values.Length < 2 ? null : Sqrt(SquaredDeviations(values) / (values.Length - 1));
    private static decimal Sqrt(decimal value) => (decimal)Math.Sqrt((double)value);
}
