using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using SPCStar.Core.Services;
using SPCStar.Core.Domain;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SpecPrecisionTests
{
    [Fact]
    public void SetupDisplay_PreservesLimitsThroughJsonReloadAndKeepsMissingLimitsBlank()
    {
        var dto = new InspectionPlanSetupDto("P", "Part", "General", "Process", "Process", 10,
            "Reading", CharacteristicType.Variable, null, .0570m, .0670m, "in", "", "", "Setup",
            1, 1, FrequencyType.Event, 1, FrequencyUnit.StartOfJob, null, "SpecLimitOnly");
        var reloaded = JsonSerializer.Deserialize<InspectionPlanSetupDto>(JsonSerializer.Serialize(dto))!;
        Assert.Equal("0.0570", reloaded.LslText);
        Assert.Equal("0.0670", reloaded.UslText);
        Assert.Null((reloaded with { Lsl = null }).LslText);
    }
    [Fact]
    public void SetupRequest_PreservesTypedLimitZeros()
    {
        var request = JsonSerializer.Deserialize<UpsertInspectionSetupRequest>("""{"lsl":"0.0570","usl":"0.0670"}""", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("0.0570", request!.Lsl?.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("0.0670", request.Usl?.ToString(CultureInfo.InvariantCulture));
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("0.0570", json);
    }

    [Fact]
    public void WorkbookImport_PreservesSpecFormatPrecisionWithoutRoundingOrFormattingCounts()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string name, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(xml);
            }
            Write("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="SPC-Star Import" r:id="rId1"/></sheets></workbook>""");
            Write("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>""");
            Write("xl/styles.xml", """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts><numFmt numFmtId="164" formatCode="0.0000"/></numFmts><cellXfs><xf numFmtId="0"/><xf numFmtId="164"/></cellXfs></styleSheet>""");
            Write("xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Lower Spec</t></is></c><c r="B1" t="inlineStr"><is><t>Upper Spec</t></is></c><c r="C1" t="inlineStr"><is><t>Sample Size</t></is></c></row><row r="2"><c r="A2" s="1"><v>0.057</v></c><c r="B2" s="1"><v>0.06701</v></c><c r="C2" s="1"><v>3</v></c></row><row r="3"><c r="A3"/><c r="B3" s="1"><v>0.067</v></c></row></sheetData></worksheet>""");
        }
        stream.Position = 0;
        var rows = CsvSupport.ReadRows(XlsxImportSupport.ReadImportSheetAsCsv(stream));
        Assert.Equal("0.0570", rows[0]["Lower Spec"]);
        Assert.Equal("0.06701", rows[0]["Upper Spec"]);
        Assert.Equal("3", rows[0]["Sample Size"]);
        Assert.Equal("", rows[1]["Lower Spec"]);
        Assert.Equal("0.0670", rows[1]["Upper Spec"]);
    }
}
