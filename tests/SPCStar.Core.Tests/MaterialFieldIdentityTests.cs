using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class MaterialFieldIdentityTests
{
    [Fact]
    public void SamePartNumber_PreservesEachFieldInCurrentLotsHistoryExportsAndStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"material-fields-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteBackedSpcRepository(path);
            var part = new Part { PartNum = "70122", Description = "Contact assembly" };
            repository.Parts.Add(part);
            var names = new[] { "Contact Tape A - BOA Lot", "Contact Tape A - Supplier Lot", "Contact Tape B - BOA Lot", "Contact Tape B - Supplier Lot" };
            foreach (var name in names) repository.PartMaterialFields.Add(new PartMaterialField
            {
                PartId = part.Id, MaterialName = name, MaterialPartNum = "50041", MaterialDescription = "CONTACT TAPE, 2268-001", IsRequired = true
            });
            var service = new MaterialChangeLogService(repository);
            var at = DateTimeOffset.Parse("2026-10-17T13:07:00Z");
            for (var i = 0; i < names.Length; i++)
            {
                var result = service.Record(new("70122", "70122", "50041", "", $"LOT-{i}", null,
                    "4S-1", "operator1", at.AddSeconds(i), "Material issue at job start", MaterialName: names[i]));
                Assert.True(result.Succeeded, string.Join("; ", result.Errors));
                if (i < 3) Assert.NotNull(JobMaterialRules.RequiredIssueMessage(repository, "70122", "70122"));
            }
            Assert.Null(JobMaterialRules.RequiredIssueMessage(repository, "70122", "70122"));
            var changed = service.Record(new("70122", "70122", "50041", "", "NEW", null,
                "4S-1", "operator1", at.AddMinutes(1), "Material change", MaterialName: names[0]));
            Assert.True(changed.Succeeded);
            Assert.Equal("LOT-0", changed.Value!.OldLotNum);
            Assert.Equal("LOT-1", JobMaterialRules.CurrentIssue(repository, "70122", "70122", "50041", names[1])!.NewLotNum);
            var history = new JobHistoryService(repository).GetForJob("70122");
            Assert.All(names, name => Assert.Contains(history, row => row.MaterialName == name));
            var csv = new HistoryExportService(repository).ExportMaterialChangeHistoryCsv(new([], ["70122"], [], null, null));
            Assert.All(names, name => Assert.Contains(name, csv));
            repository.SaveChanges();
            var loaded = new SqliteBackedSpcRepository(path);
            Assert.Equal(5, loaded.MaterialChanges.Count);
            Assert.All(loaded.MaterialChanges, item => Assert.Contains(item.MaterialName, names));
            Assert.All(loaded.MaterialChanges, item => Assert.Equal("CONTACT TAPE, 2268-001", item.MaterialDescription));
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public void AmbiguousUnnamedField_IsNotGuessedOrUsedToSatisfyOtherFields()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "P", Description = "Part" }; repository.Parts.Add(part);
        foreach (var name in new[] { "A", "B" }) repository.PartMaterialFields.Add(new PartMaterialField
            { PartId = part.Id, MaterialName = name, MaterialPartNum = "50041", IsRequired = true });
        var result = new MaterialChangeLogService(repository).Record(new("J", "P", "50041", "", "1", null,
            "M", "operator", DateTimeOffset.UtcNow, "Material issue at job start"));
        Assert.False(result.Succeeded);
        Assert.Empty(repository.MaterialChanges);
    }
}
