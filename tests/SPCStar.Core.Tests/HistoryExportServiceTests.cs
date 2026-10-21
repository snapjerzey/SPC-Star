using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class HistoryExportServiceTests
{
    [Fact]
    public void LedgerExport_CombinesMachineVariableOperationPhaseShiftAndDates()
    {
        var repository = RepositoryWithHistory();
        var reading = repository.Measurements.First(item => item.JobNum == "J100");
        reading.OperatorShift = "5 Day";
        var service = new HistoryExportService(repository);
        var request = new LedgerHistoryExportRequest([reading.PartNum], [reading.JobNum], reading.Timestamp, reading.Timestamp,
            $"{reading.ProcessCode}|{reading.OperationSeq}", reading.InspectionPhase, "5 Day", reading.ResourceId, reading.CharacteristicName);
        using var matched = new ZipArchive(new MemoryStream(service.ExportLedgerXlsx(request)));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = XDocument.Parse(ReadZipEntry(matched, "xl/worksheets/sheet2.xml")).Descendants(ns + "row").ToArray();
        Assert.Equal(2, rows.Length); Assert.Contains(reading.CharacteristicName, rows[1].Value);
        foreach (var query in new[] { request with { ResourceId="Other" }, request with { CharacteristicName="Other" },
            request with { Operation=$"{reading.ProcessCode}|100" }, request with { InspectionPhase="Unknown" } })
        {
            using var excluded = new ZipArchive(new MemoryStream(service.ExportLedgerXlsx(query)));
            Assert.Single(XDocument.Parse(ReadZipEntry(excluded,"xl/worksheets/sheet2.xml")).Descendants(ns + "row"));
        }
    }

    [Fact]
    public void LedgerExport_ShiftFiltersEverySheetAndRetainsRecordedInspectionShift()
    {
        var repository = RepositoryWithHistory();
        var measurement = repository.Measurements.First(item => item.JobNum == "J100");
        measurement.OperatorShift = "1st Half Days";
        repository.JobPhaseCompletions.Add(new JobPhaseCompletion {
            JobNum = "J100", PartNum = "P100", ResourceId = "PRESS1", ProcessCode = "MOLD", OperationSeq = 10,
            InspectionPhase = "In Process", CompletedByUserId = "operator1", OperatorShift = "1st Half Days",
            CompletedAt = measurement.Timestamp, MeasurementIds = { measurement.Id }
        });
        var service = new HistoryExportService(repository);
        using var included = new ZipArchive(new MemoryStream(service.ExportLedgerXlsx(new(["P100"], ["J100"], null, null, Shift: "1st Half Days"))));
        Assert.Contains("Inspection Complete", ReadZipEntry(included, "xl/worksheets/sheet2.xml"));
        Assert.Contains("1st Half Days", ReadZipEntry(included, "xl/worksheets/sheet2.xml"));
        using var excluded = new ZipArchive(new MemoryStream(service.ExportLedgerXlsx(new(["P100"], ["J100"], null, null, Shift: "2nd Half Nights"))));
        for (var sheet = 2; sheet <= 5; sheet++)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var xml = XDocument.Parse(ReadZipEntry(excluded, $"xl/worksheets/sheet{sheet}.xml"));
            Assert.Single(xml.Descendants(ns + "row"));
        }
        Assert.Contains("Shift filter", ReadZipEntry(excluded, "xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void Watch_RemainsOnYellowMeasurementRowAfterRecovery_WithoutJobHistoryEventOrLockout()
    {
        var repository = RepositoryWithHistory();
        var measurement = repository.Measurements.First(item => item.JobNum == "J100" && item.CharacteristicName == "Diameter");
        measurement.WatchReason = "Trending toward upper specification limit";
        var warning = new ProcessAlert { JobNum = "J100", PartNum = "P100", ResourceId = "PRESS1",
            CharacteristicName = "Diameter", OperatorUserId = "operator1", RuleTriggered = RuleTriggered.CustomRuleTriggered,
            LockedAt = measurement.Timestamp, Status = AlertStatus.Warning, Detail = measurement.WatchReason };
        repository.Alerts.Add(warning);
        Assert.DoesNotContain(new JobHistoryService(repository).GetForJob("J100"), row => row.Id == warning.Id);
        warning.Status = AlertStatus.Resolved;
        Assert.DoesNotContain(new JobHistoryService(repository).GetForJob("J100"), row => row.Id == warning.Id);
        var bytes = new HistoryExportService(repository).ExportLedgerXlsx(new(["P100"], ["J100"], null, null));
        using var archive = new ZipArchive(new MemoryStream(bytes));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var ledger = XDocument.Parse(ReadZipEntry(archive, "xl/worksheets/sheet2.xml"));
        var row = Assert.Single(ledger.Descendants(ns + "row"), row => row.Value.Contains(measurement.WatchReason));
        Assert.Contains("Watch", row.Value);
        Assert.All(row.Elements(ns + "c"), cell => Assert.Equal("2", (string?)cell.Attribute("s")));
        var styles = XDocument.Parse(ReadZipEntry(archive, "xl/styles.xml"));
        Assert.Contains(styles.Descendants(ns + "fgColor"), color => (string?)color.Attribute("rgb") == "FFFFF3CD");
        Assert.DoesNotContain(measurement.WatchReason, ReadZipEntry(archive, "xl/worksheets/sheet3.xml"));
    }

    [Fact]
    public void ExportInspectionCsv_FiltersByJobAndCharacteristic()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportInspectionCsv(new InspectionHistoryExportRequest([], ["J100"], [], ["Diameter"], null, null));

        Assert.Contains("JobNum,PartNum,ProcessCode,OperationSeq,ResourceID,CharacteristicName,MeasurementValue,Timestamp,OperatorUserID,OperatorShift", csv);
        Assert.Contains("J100,P100,MOLD,10,PRESS1,Diameter,5,2026-01-01T08:00:00.0000000+00:00,operator1,1st Shift", csv);
        Assert.DoesNotContain("J200", csv);
    }

    [Fact]
    public void ExportAlertHistoryCsv_IncludesOverriddenWhenRequested()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportAlertHistoryCsv(new AlertHistoryExportRequest([], [], [], [], null, null, IncludeOverridden: true));

        Assert.Contains("RuleTriggered", csv);
        Assert.Contains("Overridden", csv);
    }

    [Fact]
    public void ExportMaterialChangeHistoryCsv_ReturnsTraceabilityFields()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var csv = service.ExportMaterialChangeHistoryCsv(new MaterialHistoryExportRequest(["P100"], ["J100"], ["PRESS1"], null, null));

        Assert.Contains("MaterialPartNum,OldLotNum,NewLotNum", csv);
        Assert.Contains("RESIN-A,LOT1,LOT2", csv);
    }

    [Fact]
    public void ExportLedgerXlsx_WritesWorkbookPackage()
    {
        var repository = RepositoryWithHistory();
        var service = new HistoryExportService(repository);

        var bytes = service.ExportLedgerXlsx(new LedgerHistoryExportRequest(["P100"], ["J100"], null, null));

        Assert.True(bytes.Length > 0);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        var workbookXml = ReadZipEntry(archive, "xl/workbook.xml");
        Assert.Contains("Summary", workbookXml);
        Assert.Contains("Ledger", workbookXml);
        Assert.Contains("Lockouts", workbookXml);
        Assert.Contains("Edit Audit", workbookXml);
        Assert.Contains("Job Notes", workbookXml);
    }

    [Fact]
    public void ExportLedgerXlsx_IncludesFullJobNotesAcrossPhaseAndOperationWithJobPartAndDateFilters()
    {
        var repository = RepositoryWithHistory();
        var timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z");
        void AddNote(string job, string part, DateTimeOffset time, string text) => repository.JobNotes.Add(new JobNote
        {
            JobNum = job, PartNum = part, ResourceId = "PRESS1", OperatorUserId = "operator1", Timestamp = time, NoteText = text
        });
        const string fullNote = "Reviewed tooling & material <before restart>.\nSecond line with details.";
        AddNote("J100", "P100", timestamp, fullNote);
        AddNote("J100", "P100", timestamp.AddMinutes(1), "Latest note");
        AddNote("J200", "P100", timestamp, "Other job excluded");
        AddNote("J100", "P200", timestamp, "Other part excluded");
        AddNote("J100", "P100", timestamp.AddDays(-1), "Older note excluded");
        AddNote("J100", "P100", timestamp.AddDays(1), "Future note excluded");
        var bytes = new HistoryExportService(repository).ExportLedgerXlsx(new LedgerHistoryExportRequest(
            ["P100"], ["J100"], timestamp, timestamp.AddMinutes(1), "MOLD 10", "In Process"));
        using var archive = new ZipArchive(new MemoryStream(bytes));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var notes = XDocument.Parse(ReadZipEntry(archive, "xl/worksheets/sheet5.xml"));
        var rows = notes.Descendants(ns + "row")
            .Select(row => row.Descendants(ns + "t").Select(cell => cell.Value).ToArray()).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "Date / Time", "Job", "Part", "Machine", "User", "Note", "Replies" }, rows[0]);
        Assert.Equal("Latest note", rows[1][5]);
        Assert.Equal(new[] { timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), "J100", "P100", "PRESS1", "operator1", fullNote, "" }, rows[2]);
        Assert.Contains("Job notes rows", ReadZipEntry(archive, "xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void ExportLedgerXlsx_KeepsJobNotesHeadersWhenNoNotesMatch()
    {
        var bytes = new HistoryExportService(RepositoryWithHistory())
            .ExportLedgerXlsx(new LedgerHistoryExportRequest(["P100"], ["J100"], null, null));
        using var archive = new ZipArchive(new MemoryStream(bytes));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var notes = XDocument.Parse(ReadZipEntry(archive, "xl/worksheets/sheet5.xml"));
        Assert.Single(notes.Descendants(ns + "row"));
        Assert.Contains("Note", notes.Descendants(ns + "t").Select(cell => cell.Value));
    }

    private static string ReadZipEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Missing workbook entry {path}.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static InMemorySpcRepository RepositoryWithHistory()
    {
        var repository = new InMemorySpcRepository();
        repository.Measurements.AddRange([
            Measurement("J100", "Diameter", 5m),
            Measurement("J200", "Diameter", 5.1m),
            Measurement("J100", "Length", 10m)
        ]);
        repository.Alerts.Add(new ProcessAlert
        {
            JobNum = "J100",
            PartNum = "P100",
            ResourceId = "PRESS1",
            CharacteristicName = "Diameter",
            OperatorUserId = "operator1",
            OperatorShift = "1st Shift",
            RuleTriggered = RuleTriggered.OnePointBeyondControlLimit,
            LockedAt = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            Status = AlertStatus.Overridden
        });
        repository.MaterialChanges.Add(new MaterialChangeLog
        {
            JobNum = "J100",
            PartNum = "P100",
            MaterialPartNum = "RESIN-A",
            OldLotNum = "LOT1",
            NewLotNum = "LOT2",
            QuantityLoaded = 500m,
            ResourceId = "PRESS1",
            OperatorUserId = "operator1",
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            Reason = "Loaded next lot"
        });
        return repository;
    }

    private static InspectionMeasurement Measurement(string jobNum, string characteristicName, decimal value)
    {
        return new InspectionMeasurement
        {
            JobNum = jobNum,
            PartNum = "P100",
            ProcessCode = "MOLD",
            OperationSeq = 10,
            ResourceId = "PRESS1",
            CharacteristicName = characteristicName,
            Value = value,
            Timestamp = DateTimeOffset.Parse("2026-01-01T08:00:00Z"),
            OperatorUserId = "operator1",
            OperatorShift = "1st Shift"
        };
    }
}
