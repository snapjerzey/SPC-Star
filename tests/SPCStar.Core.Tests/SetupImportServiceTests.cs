using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SetupImportServiceTests
{
    [Fact]
    public void ImportCsv_RejectsInvalidSpecLimits()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(ValidCsv(lsl: "10", usl: "5"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("LSL must be less than USL", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repository.Parts);
    }

    [Fact]
    public void ImportCsv_UpsertsExistingCharacteristicPlan()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        Assert.True(service.ImportCsv(ValidCsv(description: "Original", sampleSize: "1")).Succeeded);
        Assert.True(service.ImportCsv(ValidCsv(description: "Updated", sampleSize: "3")).Succeeded);

        Assert.Single(repository.Parts);
        Assert.Equal("Updated", repository.Parts.Single().Description);
        Assert.Single(repository.Characteristics);
        Assert.Single(repository.InspectionPlans);
        Assert.Equal(3, repository.InspectionPlans.Single().SampleSize);
    }

    [Fact]
    public void ImportCsv_RejectsDuplicateCharacteristicsInSameFile()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var row = ValidCsv().Split(Environment.NewLine)[1];
        var duplicateCsv = ValidCsv() + row + Environment.NewLine;

        var result = service.ImportCsv(duplicateCsv);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Duplicate characteristic", StringComparison.OrdinalIgnoreCase));
    }

    private static string ValidCsv(
        string description = "Widget",
        string lsl = "4.5",
        string usl = "5.5",
        string sampleSize = "1")
    {
        return string.Join(Environment.NewLine, [
            "PartNum,PartDescription,ProcessCode,ProcessDescription,OperationSeq,CharacteristicName,CharacteristicType,Nominal,LSL,USL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA",
            $"P100,{description},MOLD,Molding,10,Diameter,Variable,5.0,{lsl},{usl},mm,{sampleSize},Time,30,Minutes,WesternElectric,true",
            string.Empty
        ]);
    }
}
