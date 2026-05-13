using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class FileBackedSpcRepositoryTests
{
    [Fact]
    public void SaveChanges_ReloadsStoredSpcData()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"spcstar-{Guid.NewGuid():N}.json");
        try
        {
            var repository = new FileBackedSpcRepository(storagePath);
            SeedData.SeedAll(repository);
            repository.Measurements.Add(new InspectionMeasurement
            {
                JobNum = "J100",
                PartNum = "P100",
                ProcessCode = "MOLD",
                OperationSeq = 10,
                ResourceId = "PRESS1",
                CharacteristicName = "Diameter",
                Value = 5.1m,
                Timestamp = DateTimeOffset.Parse("2026-05-05T12:00:00Z"),
                OperatorUserId = "operator1",
                SubmittedAt = DateTimeOffset.Parse("2026-05-05T12:00:01Z")
            });
            repository.MaterialChanges.Add(new MaterialChangeLog
            {
                JobNum = "J100",
                PartNum = "P100",
                MaterialPartNum = "MAT-1",
                OldLotNum = string.Empty,
                NewLotNum = "LOT-1",
                ResourceId = "PRESS1",
                OperatorUserId = "operator1",
                Timestamp = DateTimeOffset.Parse("2026-05-05T12:10:00Z"),
                Reason = "Lot Change",
                SubmittedAt = DateTimeOffset.Parse("2026-05-05T12:10:01Z")
            });
            repository.JobNotes.Add(new JobNote
            {
                JobNum = "J100",
                PartNum = "P100",
                ResourceId = "PRESS1",
                OperatorUserId = "operator1",
                NoteText = "Checked material lot and restarted.",
                Timestamp = DateTimeOffset.Parse("2026-05-05T12:15:00Z")
            });
            repository.JobTags.Add(new JobTag
            {
                JobNum = "J100",
                PartNum = "P100",
                ResourceId = "PRESS1",
                TagName = "Wire Shipment",
                TagValue = "WIRE-1",
                OperatorUserId = "operator1",
                UpdatedAt = DateTimeOffset.Parse("2026-05-05T12:20:00Z")
            });

            repository.SaveChanges();

            var reloaded = new FileBackedSpcRepository(storagePath);

            Assert.Contains(reloaded.Users, user => user.UserName == "admin1");
            Assert.True(new CredentialService(reloaded).ValidateCredential("admin1", "admin1"));
            Assert.True(new PermissionService(reloaded).UserHasPermission("admin1", PermissionNames.CanManageUsers));
            Assert.Contains(reloaded.Characteristics, characteristic => characteristic.Name == "Diameter");
            Assert.Contains(reloaded.Measurements, measurement => measurement.Value == 5.1m);
            Assert.Contains(reloaded.MaterialChanges, change => change.NewLotNum == "LOT-1");
            Assert.Contains(reloaded.JobNotes, note => note.NoteText == "Checked material lot and restarted.");
            Assert.Contains(reloaded.JobTags, tag => tag.TagName == "Wire Shipment" && tag.TagValue == "WIRE-1");
        }
        finally
        {
            if (File.Exists(storagePath))
            {
                File.Delete(storagePath);
            }
        }
    }
}
