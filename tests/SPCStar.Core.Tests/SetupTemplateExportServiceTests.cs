using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SetupTemplateExportServiceTests
{
    [Fact]
    public void ExportCsv_WritesCurrentSetupInTemplateFormat()
    {
        var repository = new InMemorySpcRepository();
        var import = new SetupImportService(repository);

        var result = import.ImportCsv(string.Join(Environment.NewLine, [
            Header(),
            "P100,Widget,Needles,B123,.029,,Needlemaker,,,,,Raw Dim,,TRUE,10,mm,Barrel,Caliper,.029,.028,.030,.027,.031,GlobalDefault,TRUE,3,Event,1,StartOfJob,WesternElectric,TRUE,2,Event,1,ToolChange,WesternElectric,,,,,,,TRUE,5,Quantity,1000,Pieces,Cusum,,,,,,",
            "P100,Widget,Needles,B123,.029,,Needlemaker,Vendor Coil #,,,,,,TRUE,1,,,,,,,,,,TRUE,,,,,,TRUE,,,,,,,,,,,,,,,,,,,,",
            "P100,Widget,Needles,B123,.029,,Needlemaker,,Wire,61046,Needle Blank Wire,,,,2,,,,,,,,,,TRUE,,,,,,TRUE,,,,,,,,,,,,,,,,,,,,",
            string.Empty
        ]));
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));

        var csv = new SetupTemplateExportService(repository).ExportCsv();
        var rows = CsvSupport.ReadRows(csv);

        Assert.StartsWith("Part Number,Part Description,Product Group,Blank Code,Hole Size", csv);
        var inspection = Assert.Single(rows, row => row["Variable Name"] == "Raw Dim");
        Assert.Equal("P100", inspection["Part Number"]);
        Assert.Equal("Needlemaker", inspection["Operation"]);
        Assert.Equal("B123", inspection["Blank Code"]);
        Assert.Equal(".029", inspection["Hole Size"]);
        Assert.Equal("TRUE", inspection["Startup Required"]);
        Assert.Equal("3", inspection["Startup Sample Size"]);
        Assert.Equal("TRUE", inspection["Setup Required"]);
        Assert.Equal("2", inspection["Setup Sample Size"]);
        Assert.Equal("TRUE", inspection["In Process Required"]);
        Assert.Equal("5", inspection["In Process Sample Size"]);
        Assert.Equal("Cusum", inspection["In Process Drift Rule"]);
        Assert.Equal("0.027", inspection["Lower Control"]);
        Assert.Equal("0.031", inspection["Upper Control"]);

        var jobData = Assert.Single(rows, row => row["Job Data Field"] == "Vendor Coil #");
        Assert.Equal("TRUE", jobData["Startup Required"]);
        Assert.Equal("TRUE", jobData["Setup Required"]);

        var material = Assert.Single(rows, row => row["Material Name"] == "Wire");
        Assert.Equal("61046", material["Material Part Number"]);
        Assert.Equal("Needle Blank Wire", material["Material Description"]);
    }

    private static string Header()
    {
        return string.Join(",", [
            "Part Number",
            "Part Description",
            "Product Group",
            "Blank Code",
            "Hole Size",
            "Inspection Phase",
            "Operation",
            "Job Data Field",
            "Material Name",
            "Material Part Number",
            "Material Description",
            "Variable Name",
            "Attribute Name",
            "Required",
            "Sort Order",
            "Unit",
            "Location",
            "Inspection Method",
            "Target",
            "Lower Spec",
            "Upper Spec",
            "Lower Control",
            "Upper Control",
            "Drift Rule",
            "Startup Required",
            "Startup Sample Size",
            "Startup Frequency Type",
            "Startup Frequency",
            "Startup Frequency Unit",
            "Startup Drift Rule",
            "Setup Required",
            "Setup Sample Size",
            "Setup Frequency Type",
            "Setup Frequency",
            "Setup Frequency Unit",
            "Setup Drift Rule",
            "Coil Change Required",
            "Coil Change Sample Size",
            "Coil Change Frequency Type",
            "Coil Change Frequency",
            "Coil Change Frequency Unit",
            "Coil Change Drift Rule",
            "In Process Required",
            "In Process Sample Size",
            "In Process Frequency Type",
            "In Process Frequency",
            "In Process Frequency Unit",
            "In Process Drift Rule",
            "Spool Required",
            "Spool Sample Size",
            "Spool Frequency Type",
            "Spool Frequency",
            "Spool Frequency Unit",
            "Spool Drift Rule"
        ]);
    }
}
