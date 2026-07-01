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
        Assert.Contains(result.Errors, error => error.Contains("LSL must be less than or equal to USL", StringComparison.OrdinalIgnoreCase));
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
    public void ImportCsv_AllowsRecordOnlyVariablesWithoutSpecLimits()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "Variable,P100,Widget,General,Setup,MOLD,,,,,Speed / Time in Acid,Variable,,,,,,sec,1,Event,1,ToolChange,None,false,Mean,,1",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var characteristic = Assert.Single(repository.Characteristics);
        Assert.Equal("Speed / Time in Acid", characteristic.Name);
        Assert.Empty(repository.SpecLimits);
        Assert.Empty(repository.ControlLimits);

        var plan = Assert.Single(new SetupQueryService(repository).GetInspectionPlans("P100"));
        Assert.Equal("Speed / Time in Acid", plan.CharacteristicName);
        Assert.Null(plan.Nominal);
        Assert.Null(plan.Lsl);
        Assert.Null(plan.Usl);
    }

    [Fact]
    public void ImportCsv_ImportsShiftFrequencyForTwicePerShiftChecks()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "Attribute,P100,Widget,General,In Process,MOLD,,,,,Tip Sensor,Attribute,,,,,,,1,Quantity,2,shift,GlobalDefault,false,Mean,,1",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var plan = Assert.Single(repository.InspectionPlans);
        Assert.Equal(FrequencyType.Event, plan.Frequency.Type);
        Assert.Equal(2, plan.Frequency.Value);
        Assert.Equal(FrequencyUnit.Shift, plan.Frequency.Unit);
    }

    [Fact]
    public void ImportCsv_ImportsBoxFrequencyForPerBoxChecks()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "Attribute,P100,Widget,General,In Process,MOLD,,,,,Overall Visual,Attribute,,,,,,,1,Quantity,1,Box,GlobalDefault,false,Mean,,1",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var plan = Assert.Single(repository.InspectionPlans);
        Assert.Equal(FrequencyType.Quantity, plan.Frequency.Type);
        Assert.Equal(1, plan.Frequency.Value);
        Assert.Equal(FrequencyUnit.Box, plan.Frequency.Unit);
    }

    [Fact]
    public void ImportCsv_UsesRequirementTextWhenSampleContextIsBlank()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            "RecordType,PartNum,PartDescription,ProductGroup,Operation,InspectionParameter,Attribute/Variable,RequirementText,SampleContext,UOM,SampleSize,InProcessRequired,InProcessSampleSize,InProcessFrequencyQty,InProcessFrequencyUnit",
            "INSPECTION,P100,Widget,General,MOLD,X,Variable,Reference only; target 0.007,,in,4,Y,4,2000,Pieces",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal("Reference only; target 0.007", repository.Characteristics.Single().Location);
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
            "Variable,P200,Needle,Needles,Startup,Needle Forming,,,,,Diameter,Variable,5.0,4.5,5.5,4.25,5.75,mm,5,Event,1,StartOfJob,WesternElectric,,",
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
        var header = new[] { "Part Number", "Part Description", "Product Group", "Inspection Phase", "Operation", "Job Data Field", "Material Name", "Material Part Number", "Material Description", "Variable Name", "Attribute Name", "Required", "Sort Order", "Unit", "Target", "Lower Spec", "Upper Spec", "Lower Control", "Upper Control", "Sample Size", "Frequency Type", "Frequency", "Frequency Unit", "Drift Rule" };
        string Row(params string[] values) => string.Join(",", values.Concat(Enumerable.Repeat("", header.Length)).Take(header.Length));

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            Row("P300", "Human Template Part", "Needles", "Startup", "", "Wire Shipment", "", "", "", "", "", "true", "1"),
            Row("P300", "Human Template Part", "Needles", "Startup", "", "", "Wire", "WIRE-302", "302 stainless wire", "", "", "true", "2"),
            Row("P300", "Human Template Part", "Needles", "Startup", "Needle Forming", "", "", "", "", "Outside Diameter", "", "", "", "mm", "5.0", "4.5", "5.5", "4.4", "5.6", "5", "Event", "1", "StartOfJob", "WesternElectric"),
            Row("P300", "Human Template Part", "Needles", "Startup", "Needle Forming", "", "", "", "", "", "Comparator Check", "", "", "Accept/Reject", "", "", "", "", "", "5", "Event", "1", "StartOfJob", "WesternElectric"),
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.PartJobDataFields, field => field.FieldName == "Wire Shipment");
        Assert.Contains(repository.PartMaterialFields, field => field.MaterialPartNum == "WIRE-302");
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Outside Diameter" && characteristic.Type == CharacteristicType.Variable);
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Comparator Check" && characteristic.Type == CharacteristicType.Attribute);
    }

    [Fact]
    public void ImportCsv_ImportsInspectionMetadataColumns()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var header = new[]
        {
            "Part Number", "Part Description", "Product Group", "Inspection Phase", "Operation",
            "Variable Name", "Unit", "Location", "Inspection Method",
            "Target", "Lower Spec", "Upper Spec", "Lower Control", "Upper Control",
            "Sample Size", "Frequency Type", "Frequency", "Frequency Unit", "Drift Rule"
        };

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            "70307,Schneider HOM Jaw Terminal,Schneider,In Process,Inspection,Material Thickness,in,Front,Micrometer,.050,.049,.051,.049,.051,2,Quantity,5000,Pieces,WesternElectric",
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var characteristic = Assert.Single(repository.Characteristics);
        Assert.Equal("Front", characteristic.Location);
        Assert.Equal("Micrometer", characteristic.InspectionMethod);
    }

    [Fact]
    public void ImportCsv_ImportsSchneider70310Template()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var csvPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "imports", "schneider-70310-import.csv"));

        var result = service.ImportCsv(File.ReadAllText(csvPath));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.Parts, part => part.PartNum == "70310" && part.ProductGroup == "Schneider");
        Assert.Empty(repository.PartMaterialFields);
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Jaw Width");
        Assert.DoesNotContain(repository.Characteristics, characteristic => characteristic.Name.Contains(" - Front", StringComparison.OrdinalIgnoreCase) || characteristic.Name.Contains(" - Back", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Jaw Profile No Clip" && characteristic.Type == CharacteristicType.Attribute);
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Weld Pool Clearance from Contact Face" && characteristic.Type == CharacteristicType.Variable);

        var inProcess = new SetupQueryService(repository)
            .GetInspectionPlans("70310")
            .Where(plan => plan.InspectionPhase == "In Process")
            .Select(plan => plan.CharacteristicName)
            .ToArray();
        Assert.Equal("Overall Visual", inProcess[0]);
        Assert.Equal("Jaw Check for Burrs and Cracking at Forms", inProcess[1]);
        Assert.Equal("Spring Clip Fully Pressed Against Jaw", inProcess[2]);
        Assert.Equal("Weld Pool Clearance from Contact Face", inProcess[3]);
        Assert.Equal("Inside Legs Gap No Clip", inProcess[4]);
        Assert.Equal("Shear Force", inProcess[5]);
        Assert.Equal("Bottom Contact Location", inProcess[15]);
        Assert.DoesNotContain("Jaw Leg Width", inProcess);
        Assert.DoesNotContain("Jaw Tab Width", inProcess);
        Assert.DoesNotContain("Jaw Tab Center", inProcess);

        var setup = new SetupQueryService(repository)
            .GetInspectionPlans("70310")
            .Where(plan => plan.InspectionPhase == "Setup")
            .Select(plan => plan.CharacteristicName)
            .ToArray();
        Assert.Contains("Jaw Leg Width", setup);
        Assert.Contains("Jaw Tab Width", setup);
        Assert.Contains("Jaw Tab Center", setup);
    }

    [Fact]
    public void ImportCsv_ImportsV4OrderedInspectionTemplate()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var header = new[]
        {
            "RecordType", "PartNum", "PartDescription", "ProductGroup", "Operation", "CustomerPartNum",
            "MaterialRole", "MaterialPartNum", "MaterialDescription", "RequiresLotEntry",
            "ParameterSeq", "InspectionParameter", "Attribute/Variable", "EntryType", "RequirementText", "Tool Used",
            "LowerSpec", "UpperSpec", "NominalSpec", "UOM", "SampleContext",
            "StartupRequired", "StartupSampleSize", "SetupRequired", "SetupSampleSize",
            "InProcessRequired", "InProcessSampleSize", "InProcessFrequencyQty", "InProcessFrequencyUnit",
            "CoilChangeRequired", "CoilChangeSampleSize", "SpoolRequired", "SpoolSampleSize"
        };
        string Row(params (string Field, string Value)[] values)
        {
            var row = header.ToDictionary(field => field, _ => "", StringComparer.OrdinalIgnoreCase);
            foreach (var (field, value) in values)
            {
                row[field] = value;
            }

            return string.Join(",", header.Select(field => row[field]));
        }

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            Row(("RecordType", "PART"), ("PartNum", "70305"), ("PartDescription", "Schneider Jaw Assy"), ("ProductGroup", "Schneider"), ("Operation", "general production"), ("CustomerPartNum", "48852-012-51-02")),
            Row(("RecordType", "MATERIAL"), ("PartNum", "70305"), ("PartDescription", "Schneider Jaw Assy"), ("ProductGroup", "Schneider"), ("Operation", "general production"), ("CustomerPartNum", "48852-012-51-02"), ("MaterialRole", "Copper"), ("MaterialPartNum", "51475"), ("MaterialDescription", "Copper"), ("RequiresLotEntry", "Y")),
            Row(("RecordType", "INSPECTION"), ("PartNum", "70305"), ("PartDescription", "Schneider Jaw Assy"), ("ProductGroup", "Schneider"), ("Operation", "general production"), ("CustomerPartNum", "48852-012-51-02"), ("ParameterSeq", "1"), ("InspectionParameter", "Material Thickness"), ("Attribute/Variable", "Variable"), ("EntryType", "Actual measurement"), ("RequirementText", ".050 +/- .001"), ("Tool Used", "Micrometer"), ("LowerSpec", ".049"), ("UpperSpec", ".051"), ("NominalSpec", ".050"), ("UOM", "in"), ("SampleContext", "2 pcs from Front and 2 pcs from Back"), ("StartupRequired", "Y"), ("StartupSampleSize", "4"), ("CoilChangeRequired", "Y"), ("CoilChangeSampleSize", "4")),
            Row(("RecordType", "INSPECTION"), ("PartNum", "70305"), ("PartDescription", "Schneider Jaw Assy"), ("ProductGroup", "Schneider"), ("Operation", "general production"), ("CustomerPartNum", "48852-012-51-02"), ("ParameterSeq", "2"), ("InspectionParameter", "Jaw Profile (No Clip)"), ("Attribute/Variable", "Attribute"), ("EntryType", "Accept/Reject"), ("RequirementText", "Accept / Reject"), ("Tool Used", "T-071 Template"), ("SampleContext", "Front and Back"), ("StartupRequired", "Y"), ("StartupSampleSize", "4"), ("SetupRequired", "Y"), ("SetupSampleSize", "2"), ("CoilChangeRequired", "Y"), ("CoilChangeSampleSize", "4")),
            Row(("RecordType", "INSPECTION"), ("PartNum", "70305"), ("PartDescription", "Schneider Jaw Assy"), ("ProductGroup", "Schneider"), ("Operation", "general production"), ("CustomerPartNum", "48852-012-51-02"), ("ParameterSeq", "3"), ("InspectionParameter", "Brazed Contact to Jaw"), ("Attribute/Variable", "Variable"), ("EntryType", "Actual measurement"), ("RequirementText", "280 lbs min"), ("Tool Used", "Force Gage"), ("LowerSpec", "280"), ("UOM", "lbs"), ("SampleContext", "Front and Back"), ("StartupRequired", "Y"), ("StartupSampleSize", "2"), ("SetupRequired", "Y"), ("SetupSampleSize", "2"), ("InProcessRequired", "Y"), ("InProcessSampleSize", "3 per side"), ("InProcessFrequencyQty", "5000"), ("InProcessFrequencyUnit", "pcs per side"), ("CoilChangeRequired", "Y"), ("CoilChangeSampleSize", "2")),
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.Parts, part => part.PartNum == "70305" && part.ProductGroup == "Schneider");
        Assert.Contains(repository.PartMaterialFields, material => material.MaterialPartNum == "51475" && material.IsRequired);
        Assert.Contains(repository.Characteristics, characteristic => characteristic.Name == "Jaw Profile (No Clip)" && characteristic.Type == CharacteristicType.Attribute && characteristic.Location == "Front and Back");

        var plans = new SetupQueryService(repository).GetInspectionPlans("70305").ToArray();
        Assert.Contains(plans, plan => plan.CharacteristicName == "Material Thickness" && plan.InspectionPhase == "Coil Change" && plan.SampleSize == 4 && plan.FrequencyUnit == FrequencyUnit.MaterialChange);
        Assert.Contains(plans, plan => plan.CharacteristicName == "Brazed Contact to Jaw" && plan.InspectionPhase == "In Process" && plan.SampleSize == 3 && plan.FrequencyValue == 5000 && plan.FrequencyUnit == FrequencyUnit.Pieces);
        Assert.Equal(["Material Thickness", "Jaw Profile (No Clip)", "Brazed Contact to Jaw"], plans.Where(plan => plan.InspectionPhase == "Startup").Select(plan => plan.CharacteristicName).ToArray());
    }

    [Fact]
    public void ImportCsv_ImportsJobDataFromUniversalTemplateRows()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var header = new[]
        {
            "RecordType", "PartNum", "PartDescription", "ProductGroup", "Operation", "CustomerPartNum",
            "MaterialRole", "MaterialPartNum", "MaterialDescription", "RequiresLotEntry",
            "ParameterSeq", "InspectionParameter", "Attribute/Variable", "EntryType", "RequirementText", "Tool Used",
            "LowerSpec", "UpperSpec", "NominalSpec", "UOM", "SampleContext",
            "StartupRequired", "StartupSampleSize", "SetupRequired", "SetupSampleSize",
            "InProcessRequired", "InProcessSampleSize", "InProcessFrequencyQty", "InProcessFrequencyUnit",
            "CoilChangeRequired", "CoilChangeSampleSize", "SpoolRequired", "SpoolSampleSize"
        };
        string Row(params (string Field, string Value)[] values)
        {
            var row = header.ToDictionary(field => field, _ => "", StringComparer.OrdinalIgnoreCase);
            foreach (var (field, value) in values)
            {
                row[field] = value;
            }

            return string.Join(",", header.Select(field => row[field]));
        }

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            Row(("RecordType", "JOBDATA"), ("PartNum", "61135"), ("PartDescription", "22MIL SH-1 Undrilled"), ("ProductGroup", "Ethicon Taperpoint"), ("Operation", "Needlemaker"), ("ParameterSeq", "1"), ("InspectionParameter", "Vendor Coil #"), ("SetupRequired", "Y"), ("InProcessRequired", "Y"), ("SpoolRequired", "Y")),
            Row(("RecordType", "JOBDATA"), ("PartNum", "61135"), ("PartDescription", "22MIL SH-1 Undrilled"), ("ProductGroup", "Ethicon Taperpoint"), ("Operation", "Needlemaker"), ("ParameterSeq", "2"), ("InspectionParameter", "Wire Shipment (W/S) #"), ("SetupRequired", "Y"), ("InProcessRequired", "Y"), ("SpoolRequired", "Y")),
            Row(("RecordType", "INSPECTION"), ("PartNum", "61135"), ("PartDescription", "22MIL SH-1 Undrilled"), ("ProductGroup", "Ethicon Taperpoint"), ("Operation", "Needlemaker"), ("ParameterSeq", "3"), ("InspectionParameter", "T Dim"), ("Attribute/Variable", "Variable"), ("EntryType", "Actual measurement"), ("Tool Used", "Comparator"), ("LowerSpec", ".020"), ("UpperSpec", ".024"), ("NominalSpec", ".022"), ("UOM", "in"), ("SetupRequired", "Y"), ("SetupSampleSize", "4"), ("InProcessRequired", "Y"), ("InProcessSampleSize", "4"), ("InProcessFrequencyQty", "1"), ("InProcessFrequencyUnit", "Shift")),
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(6, repository.PartJobDataFields.Count);
        Assert.Contains(repository.PartJobDataFields, field => field.FieldName == "Vendor Coil #" && field.InspectionPhase == "Setup");
        Assert.Contains(repository.PartJobDataFields, field => field.FieldName == "Vendor Coil #" && field.InspectionPhase == "In Process");
        Assert.Contains(repository.PartJobDataFields, field => field.FieldName == "Wire Shipment (W/S) #" && field.InspectionPhase == "Spool");
        Assert.Contains(repository.InspectionPlans, plan => plan.InspectionPhase == "Setup" && plan.SampleSize == 4);
    }

    [Fact]
    public void ImportCsv_UsesPhaseSpecificDisplayOrderFromUniversalTemplateRows()
    {
        var repository = new InMemorySpcRepository();
        var service = new SetupImportService(repository);
        var header = new[]
        {
            "RecordType", "PartNum", "PartDescription", "ProductGroup", "Operation",
            "ParameterSeq", "InspectionParameter", "Attribute/Variable", "Tool Used",
            "LowerSpec", "UpperSpec", "NominalSpec",
            "SetupRequired", "SetupSampleSize", "SetupDisplayOrder",
            "SpoolRequired", "SpoolSampleSize", "SpoolDisplayOrder"
        };
        string Row(params (string Field, string Value)[] values)
        {
            var row = header.ToDictionary(field => field, _ => "", StringComparer.OrdinalIgnoreCase);
            foreach (var (field, value) in values)
            {
                row[field] = value;
            }

            return string.Join(",", header.Select(field => row[field]));
        }

        var result = service.ImportCsv(string.Join(Environment.NewLine, [
            string.Join(",", header),
            Row(("RecordType", "INSPECTION"), ("PartNum", "61135"), ("PartDescription", "22MIL SH-1 Undrilled"), ("ProductGroup", "Ethicon Taperpoint"), ("Operation", "Needlemaker"), ("ParameterSeq", "22"), ("InspectionParameter", "Rough Taper Profile"), ("Attribute/Variable", "Attribute"), ("Tool Used", "Comparator FX19"), ("SetupRequired", "Y"), ("SetupSampleSize", "4"), ("SetupDisplayOrder", "1"), ("SpoolRequired", "Y"), ("SpoolSampleSize", "2"), ("SpoolDisplayOrder", "14")),
            string.Empty
        ]));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Contains(repository.InspectionPlans, plan => plan.InspectionPhase == "Setup" && plan.DisplayOrder == 1);
        Assert.Contains(repository.InspectionPlans, plan => plan.InspectionPhase == "Spool" && plan.DisplayOrder == 14);
    }

    private static string ValidCsv(
        string description = "Widget",
        string lsl = "4.5",
        string usl = "5.5",
        string sampleSize = "1")
    {
        return string.Join(Environment.NewLine, [
            Header(),
            $"Variable,P100,{description},General,In Process,MOLD,,,,,Diameter,Variable,5.0,{lsl},{usl},,,mm,{sampleSize},Time,30,Minutes,WesternElectric,,",
            string.Empty
        ]);
    }

    private static string Header()
    {
        return "RowType,PartNum,PartDescription,ProductGroup,InspectionPhase,Operation,FieldName,MaterialName,MaterialPartNum,MaterialDescription,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequired,DisplayOrder";
    }
}
