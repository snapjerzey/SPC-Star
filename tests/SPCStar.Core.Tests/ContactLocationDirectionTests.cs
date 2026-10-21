using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ContactLocationDirectionTests
{
    private const string Header = "Section,Part Number,Part Description,Product Group,Operation,Inspection Item,Attribute/Variable,Lower Spec,Upper Spec,Unit,Startup Required,Startup Sample Size,Setup Required,Setup Sample Size,In Process Required,In Process Sample Size,In Process Frequency Type,In Process Frequency Qty,InProcessFirstDueQty,In Process Frequency Unit,Sort Order\n";
    private static string Row(string name, string lower, string upper, int order) =>
        $"Inspection,70122,Blade Terminal,Schneider,Schneider Production,{name},Variable,{lower},{upper},in,X,1,X,1,X,1,Quantity,8000,8000,Pieces,{order}\n";
    private static string Corrected => Header +
        Row("Contact Location - A - Front / Back", "-0.030", "0.010", 18) +
        Row("Contact Location - A - Inside", "", "0.010", 19) +
        Row("Contact Location - A - Outside", "", "0.020", 20) +
        Row("Contact Location - B - Front / Back", "-0.030", "0.010", 21) +
        Row("Contact Location - B - Inside", "", "0.010", 22) +
        Row("Contact Location - B - Outside", "", "0.020", 23);
    private static void Import(InMemorySpcRepository repository, string csv)
    {
        var result = new SetupImportService(repository).ImportCsv(csv);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public void CorrectedDirections_RetireOnlyOldAssignments_KeepHistoryAndFrontBackIdentities()
    {
        var repository = new InMemorySpcRepository();
        Import(repository, Header + Row("Contact Location - A - In / Out", "-0.020", "0.010", 19) +
            Row("Contact Location - B - In / Out", "-0.020", "0.010", 21) +
            Row("Contact Location - A - Front / Back", "-0.030", "0.010", 18) +
            Row("Contact Location - B - Front / Back", "-0.030", "0.010", 20));
        var originalIds = repository.Characteristics.ToDictionary(c => c.Name, c => c.Id);
        var originalFrontBackPlans = repository.InspectionPlans.Where(p =>
            originalIds.Where(x => x.Key.EndsWith("Front / Back")).Select(x => x.Value).Contains(p.CharacteristicId))
            .Select(p => p.Id).OrderBy(x => x).ToArray();
        var historical = new InspectionMeasurement { JobNum = "Existing", PartNum = "70122", ProcessCode = "Schneider Production",
            OperationSeq = 10, ResourceId = "R", CharacteristicName = "Contact Location - A - In / Out", Value = -.015m, OperatorUserId = "qa1" };
        repository.Measurements.Add(historical);
        Import(repository, Corrected);
        Import(repository, Corrected);
        var plans = new SetupQueryService(repository).GetInspectionPlans("70122");
        Assert.Equal(18, plans.Count);
        Assert.DoesNotContain(plans, p => p.CharacteristicName.EndsWith("In / Out"));
        Assert.All(originalIds, old => Assert.Equal(old.Value, repository.Characteristics.Single(c => c.Name == old.Key).Id));
        Assert.Equal(originalFrontBackPlans, repository.InspectionPlans.Where(p =>
            originalIds.Where(x => x.Key.EndsWith("Front / Back")).Select(x => x.Value).Contains(p.CharacteristicId)).Select(p => p.Id).OrderBy(x => x));
        foreach (var phase in new[] { "Startup", "Setup", "In Process" })
        {
            var ordered = plans.Where(p => p.InspectionPhase == phase).OrderBy(p => p.DisplayOrder).ToArray();
            Assert.Equal(new[] { "Contact Location - A - Front / Back", "Contact Location - A - Inside", "Contact Location - A - Outside",
                "Contact Location - B - Front / Back", "Contact Location - B - Inside", "Contact Location - B - Outside" }, ordered.Select(p => p.CharacteristicName));
        }
        Assert.All(plans.Where(p => !p.CharacteristicName.EndsWith("Front / Back")), p =>
        {
            Assert.Null(p.Lsl); Assert.Null(p.Nominal); Assert.Equal(1, p.SampleSize);
            Assert.Equal(p.CharacteristicName.EndsWith("Inside") ? "0.010" : "0.020", p.UslText);
            if (p.InspectionPhase == "In Process") { Assert.Equal(8000, p.FrequencyValue); Assert.Equal(8000, p.FirstDueValue); }
        });
        Assert.Same(historical, Assert.Single(repository.Measurements));
        Assert.Equal(-.015m, historical.Value);
        Assert.Equal("Contact Location - A - In / Out", historical.CharacteristicName);
        Assert.Empty(repository.JobPhaseCompletions); Assert.Empty(repository.ControlLimits);
    }

    [Fact]
    public void PartialReplacement_DoesNotRetireExistingPlans()
    {
        var repository = new InMemorySpcRepository();
        Import(repository, Header + Row("Contact Location - A - In / Out", "-0.020", "0.010", 19));
        Import(repository, Header + Row("Contact Location - A - Inside", "", "0.010", 19));
        Assert.Contains(new SetupQueryService(repository).GetInspectionPlans(), p => p.CharacteristicName.EndsWith("In / Out"));
    }

    [Fact]
    public void StaleCombinedEntry_IsNotReinterpretedOrAcceptedAfterReplacement()
    {
        var repository = new InMemorySpcRepository();SeedData.SeedSecurity(repository);TestSeedData.SeedUsers(repository);
        Import(repository, Header + Row("Contact Location - A - In / Out", "-0.020", "0.010", 19));
        Import(repository, Corrected);
        var result = new InspectionMeasurementService(repository,new WesternElectricRuleService()).EnterMeasurement(
            new("ContactTest","70122","Schneider Production",10,"R","Contact Location - A - In / Out",-.015m,DateTimeOffset.UtcNow,"qa1"));
        Assert.False(result.Succeeded);Assert.Contains(result.Errors,e=>e.Contains("Reload the inspection"));
        Assert.Empty(repository.Measurements);Assert.Empty(repository.Alerts);
    }

    public static IEnumerable<object[]> DirectionLimits()
    {
        foreach (var contact in new[] { "A", "B" })
        foreach (var direction in new[] { "Inside", "Outside" })
        foreach (var phase in new[] { "Startup", "Setup", "In Process" })
        foreach (var above in new[] { false, true })
            yield return new object[] { contact, direction, phase, above };
    }

    [Theory]
    [MemberData(nameof(DirectionLimits))]
    public void DirectionSpecificUpperLimits_AcceptBoundaryAndLockOnlyAbove(string contact, string direction, string phase, bool above)
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository); TestSeedData.SeedUsers(repository);
        Import(repository, Corrected);
        var limit = direction == "Inside" ? .010m : .020m;
        var result = new InspectionMeasurementService(repository, new WesternElectricRuleService()).EnterMeasurement(
            new("ContactTest", "70122", "Schneider Production", 10, "R", $"Contact Location - {contact} - {direction}",
                limit + (above ? .001m : 0), DateTimeOffset.UtcNow, "qa1", InspectionPhase: phase));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(above, repository.Alerts.Any(a => a.Status == AlertStatus.Active));
    }

    [Fact]
    public void NegativeMagnitude_IsRejectedWithoutInventingLowerSpec_ButFrontBackStillAllowsNegative()
    {
        var repository = new InMemorySpcRepository(); SeedData.SeedSecurity(repository); TestSeedData.SeedUsers(repository);
        Import(repository, Corrected);
        var service = new InspectionMeasurementService(repository, new WesternElectricRuleService());
        var negative = service.EnterMeasurement(new("ContactTest", "70122", "Schneider Production", 10, "R",
            "Contact Location - A - Inside", -.001m, DateTimeOffset.UtcNow, "qa1"));
        Assert.False(negative.Succeeded); Assert.Empty(repository.Measurements); Assert.Empty(repository.Alerts);
        var front = service.EnterMeasurement(new("ContactTest", "70122", "Schneider Production", 10, "R",
            "Contact Location - A - Front / Back", -.020m, DateTimeOffset.UtcNow, "qa1"));
        Assert.True(front.Succeeded, string.Join("; ", front.Errors)); Assert.Empty(repository.Alerts);
    }
}
