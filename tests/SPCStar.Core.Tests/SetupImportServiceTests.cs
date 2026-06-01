using SPCStar.Core.Infrastructure;
using SPCStar.Core.Domain;
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
        Assert.Single(repository.ControlLimits);
    }

    [Fact]
    public void ImportCsv_ImportsControlLimitsWhenProvided()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "Variable,P100,Widget,General,In Process,MOLD,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,1,Time,30,Minutes,WesternElectric,true,Mean,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded);
        var limit = Assert.Single(repository.ControlLimits);
        Assert.Equal(4.25m, limit.Lcl);
        Assert.Equal(5.75m, limit.Ucl);
    }

    [Fact]
    public void ImportCsv_ImportsCoaStatisticWhenProvided()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "Variable,P100,Widget,General,In Process,MOLD,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,1,Time,30,Minutes,WesternElectric,true,StandardDeviation,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded);
        Assert.Equal(CoaStatisticType.StandardDeviation, repository.Characteristics.Single().CoaStatisticType);
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
        Assert.Contains(result.Errors, error => error.Contains("Duplicate Variable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCsv_ImportsJobDataAndMaterialRows()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "JobData,P200,Needle,Needles,Startup,,Wire Shipment,,,,,,,,,,,,,,,,,,true,1",
            "Material,P200,Needle,Needles,Startup,,,Wire,WIRE-302,,,,,,,,,,,,,,,,true,2",
            "Variable,P200,Needle,Needles,Startup,Needle Forming,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,5,Event,1,StartOfJob,WesternElectric,true,Mean,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded);
        Assert.Single(repository.PartJobDataFields);
        Assert.Equal("Wire Shipment", repository.PartJobDataFields.Single().FieldName);
        Assert.Single(repository.PartMaterialFields);
        Assert.Equal("Wire", repository.PartMaterialFields.Single().MaterialName);
        Assert.Equal("WIRE-302", repository.PartMaterialFields.Single().MaterialPartNum);
    }

    private static string ValidCsv(
        string description = "Widget",
        string lsl = "4.5",
        string usl = "5.5",
        string sampleSize = "1")
    {
        return string.Join(Environment.NewLine, [
            Header(),
            $"Variable,P100,{description},General,In Process,MOLD,,,,Diameter,Variable,5.0,{lsl},{usl},,,mm,{sampleSize},Time,30,Minutes,WesternElectric,true,Mean,,",
            string.Empty
        ]);
    }

    private static string Header()
    {
        return "RowType,PartNum,PartDescription,ProductGroup,InspectionPhase,Operation,FieldName,MaterialName,MaterialPartNum,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA,COAStatistic,IsRequired,DisplayOrder";
    }
}
