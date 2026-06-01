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
            "Variable,P100,Widget,General,In Process,MOLD,,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,1,Time,30,Minutes,WesternElectric,true,Mean,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
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
            "Variable,P100,Widget,General,In Process,MOLD,,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,1,Time,30,Minutes,WesternElectric,true,StandardDeviation,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
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
            "JobData,P200,Needle,Needles,Startup,,Wire Shipment,,,,,,,,,,,,,,,,,,,true,1",
            "Material,P200,Needle,Needles,Startup,,,Wire,WIRE-302,302 stainless wire,,,,,,,,,,,,,,,,true,2",
            "Variable,P200,Needle,Needles,Startup,Needle Forming,,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,5,Event,1,StartOfJob,WesternElectric,true,Mean,,",
            string.Empty
        ]));

        Assert.True(result.Succeeded);
        Assert.Single(repository.PartJobDataFields);
        Assert.Equal("Wire Shipment", repository.PartJobDataFields.Single().FieldName);
        Assert.Single(repository.PartMaterialFields);
        Assert.Equal("Wire", repository.PartMaterialFields.Single().MaterialName);
        Assert.Equal("WIRE-302", repository.PartMaterialFields.Single().MaterialPartNum);
        Assert.Equal("302 stainless wire", repository.PartMaterialFields.Single().MaterialDescription);
    }

    [Fact]
    public void ImportCsv_AcceptsHumanReadableTemplateHeaders()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var header = new[] { "Part Number", "Part Description", "Product Group", "Inspection Phase", "Section", "Operation", "Item Name", "Required", "Sort Order", "Material Part Number", "Material Description", "Unit", "Target", "Lower Spec", "Upper Spec", "Lower Control", "Upper Control", "Sample Size", "Frequency Type", "Frequency", "Frequency Unit", "Drift Rule", "COA Required", "COA Statistic" };
        string Row(params string[] values) => string.Join(",", values.Concat(Enumerable.Repeat("", header.Length)).Take(header.Length));

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            Row("P300", "Human Template Part", "Needles", "Startup", "Job Data", "", "Wire Shipment", "true", "1"),
            Row("P300", "Human Template Part", "Needles", "Startup", "Material", "", "Wire", "true", "2", "WIRE-302", "302 stainless wire"),
            Row("P300", "Human Template Part", "Needles", "Startup", "Variable", "Needle Forming", "Outside Diameter", "", "", "", "", "mm", "5.0", "4.5", "5.5", "4.4", "5.6", "5", "Event", "1", "StartOfJob", "WesternElectric", "true", "Mean"),
            Row("P300", "Human Template Part", "Needles", "Startup", "Attribute", "Needle Forming", "Comparator Check", "", "", "", "", "Accept/Reject", "", "", "", "", "", "5", "Event", "1", "StartOfJob", "WesternElectric", "false"),
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.PartJobDataFields, field => field.FieldName == "Wire Shipment");
        Assert.Contains(repository.PartMaterialFields, field => field.MaterialPartNum == "WIRE-302");
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Outside Diameter" && characteristic.Type == CharacteristicType.Variable);
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Comparator Check" && characteristic.Type == CharacteristicType.Attribute);
    }

    private static string ValidCsv(
        string description = "Widget",
        string lsl = "4.5",
        string usl = "5.5",
        string sampleSize = "1")
    {
        return string.Join(Environment.NewLine, [
            Header(),
            $"Variable,P100,{description},General,In Process,MOLD,,,,,Diameter,Variable,5.0,{lsl},{usl},,,mm,{sampleSize},Time,30,Minutes,WesternElectric,true,Mean,,",
            string.Empty
        ]);
    }

    private static string Header()
    {
        return "RowType,PartNum,PartDescription,ProductGroup,InspectionPhase,Operation,FieldName,MaterialName,MaterialPartNum,MaterialDescription,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA,COAStatistic,IsRequired,DisplayOrder";
    }
}
