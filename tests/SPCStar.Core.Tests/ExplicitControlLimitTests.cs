using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ExplicitControlLimitTests
{
    private const string Header = "RowType,PartNum,PartDescription,ProductGroup,InspectionPhase,Operation,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet";

    [Fact]
    public void ImportBlankControls_RemovesPriorCopies_WithoutChangingSpecs()
    {
        var r = new InMemorySpcRepository(); var service = new SetupImportService(r);
        Assert.True(service.ImportCsv(Header+"\nVariable,P1,Test,General,In Process,PROC,Width,Variable,,.195,.205,.195,.205,in,3,Event,1,StartOfJob,Custom").Succeeded);
        Assert.Single(r.ControlLimits);
        Assert.True(service.ImportCsv(Header+"\nVariable,P1,Test,General,In Process,PROC,Width,Variable,,.195,.205,,,in,3,Event,1,StartOfJob,Custom").Succeeded);
        Assert.Empty(r.ControlLimits);
        Assert.Equal(.195m, r.InspectionPlans.Single().Lsl); Assert.Equal(.205m, r.InspectionPlans.Single().Usl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(".194", null)]
    [InlineData(null, ".206")]
    public void ManualSetup_NeverFillsMissingControlLimitFromSpec(string? lower, string? upper)
    {
        var r = new InMemorySpcRepository(); var service = new SetupManagementService(r);
        var request = new UpsertInspectionSetupRequest("P1", "Test", "General", "PROC", "Process", 10, "Width", CharacteristicType.Variable,
            null, .195m, .205m, lower is null ? null : decimal.Parse(lower), upper is null ? null : decimal.Parse(upper),
            "in", 3, FrequencyType.Event, 1, FrequencyUnit.StartOfJob, "Custom");
        var result = service.UpsertInspectionSetup(request);
        Assert.True(result.Succeeded, string.Join(";",result.Errors)); Assert.Empty(r.ControlLimits);
    }
}
