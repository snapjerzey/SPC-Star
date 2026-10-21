using System.IO.Compression;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class InspectionSequenceTests
{
    [Fact]
    public void HistoryAndFilteredExport_UseSharedSequenceForExistingRecordsWithoutChangingThem()
    {
        var repository = new InMemorySpcRepository();
        var at = DateTimeOffset.Parse("2026-10-17T10:00:00Z");
        var completed = new JobPhaseCompletion
        {
            JobNum = "70306", PartNum = "70306", ProcessCode = "Schneider Production", OperationSeq = 10,
            ResourceId = "4S-1", CompletedByUserId = "operator1", InspectionPhase = "In Process", CompletionNumber = 5, CompletedAt = at
        };
        var failed = new FailedInspection
        {
            JobNum = "70306", PartNum = "70306", ProcessCode = "Schneider Production", OperationSeq = 10,
            ResourceId = "4S-2", FailedByUserId = "operator1", InspectionPhase = "In Process", FailureNumber = 1, FailedAt = at.AddMinutes(1)
        };
        var next = new JobPhaseCompletion
        {
            JobNum = "70306", PartNum = "70306", ProcessCode = "Schneider Production", OperationSeq = 10,
            ResourceId = "4S-1", CompletedByUserId = "operator1", InspectionPhase = "In Process", CompletionNumber = 6, CompletedAt = at.AddMinutes(2)
        };
        repository.JobPhaseCompletions.AddRange([completed, next]);
        repository.FailedInspections.Add(failed);
        repository.FailedInspections.Add(new FailedInspection
        {
            JobNum = "70306", PartNum = "70306", ProcessCode = "Other Operation", OperationSeq = 20,
            ResourceId = "4S-1", FailedByUserId = "operator1", InspectionPhase = "In Process", FailureNumber = 1, FailedAt = at
        });
        var history = new JobHistoryService(repository).GetForJob("70306");
        Assert.Equal(5, history.Single(row => row.Id == completed.Id).CompletionNumber);
        Assert.Equal(6, history.Single(row => row.Id == failed.Id).FailureNumber);
        Assert.Equal(7, history.Single(row => row.Id == next.Id).CompletionNumber);
        Assert.Equal(1, history.Single(row => row.ProcessCode == "Other Operation").FailureNumber);

        var bytes = new HistoryExportService(repository).ExportLedgerXlsx(
            new(["70306"], ["70306"], at.AddSeconds(30), null));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var xml = string.Join("\n", zip.Entries.Where(item => item.FullName.StartsWith("xl/worksheets/"))
            .Select(item => { using var reader = new StreamReader(item.Open()); return reader.ReadToEnd(); }));
        Assert.Contains("In Process inspection 6 failed", xml);
        Assert.Contains("In Process inspection 7 complete", xml);
        Assert.Equal(1, failed.FailureNumber);
        Assert.Equal(6, next.CompletionNumber);
        Assert.Equal(2, repository.JobPhaseCompletions.Count);
        Assert.Equal(2, repository.FailedInspections.Count);
    }
}
