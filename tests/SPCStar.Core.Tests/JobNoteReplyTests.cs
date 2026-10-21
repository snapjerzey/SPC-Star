using System.IO.Compression;
using System.Text.Json;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobNoteReplyTests
{
    private static InMemorySpcRepository Repository(string role = RoleNames.QA)
    {
        var r = new InMemorySpcRepository(); SeedData.SeedSecurity(r);
        r.Users.Add(new User { UserName = "responder", PasswordHash = "unused", PasswordSalt = "unused",
            Roles = { r.Roles.Single(item => item.Name == role) } });
        r.JobNotes.Add(new JobNote { JobNum = "J1", PartNum = "70311", ResourceId = "4S-1", OperatorUserId = "operator",
            NoteText = "Original operator note\nKeep unchanged", Timestamp = DateTimeOffset.Parse("2025-01-01T12:00:00Z") });
        return r;
    }

    [Theory]
    [InlineData(RoleNames.QA, true)]
    [InlineData(RoleNames.GOD, true)]
    [InlineData(RoleNames.QAReadOnly, false)]
    [InlineData(RoleNames.Operator, false)]
    [InlineData(RoleNames.LineTech, false)]
    public void RepliesRequireQaOrSystemManagerAccess(string role, bool allowed)
    {
        var r = Repository(role); var note = r.JobNotes[0];
        var result = new JobNoteService(r).AddReply(note.Id, "responder", new("Response", Guid.NewGuid()));
        Assert.Equal(allowed, result.Succeeded); Assert.Equal(allowed ? 1 : 0, note.Replies.Count);
    }

    [Fact]
    public void ReplyPreservesOriginalAndRetryDoesNotDuplicateIt()
    {
        var r = Repository(); var note = r.JobNotes[0]; var service = new JobNoteService(r);
        var original = JsonSerializer.Serialize(new {note.Id,note.JobNum,note.PartNum,note.ResourceId,note.OperatorUserId,note.NoteText,note.Timestamp});
        var request = new JobNoteReplyEntry("  Response <test>\nSecond line  ", Guid.NewGuid());
        var before = DateTimeOffset.UtcNow;
        Assert.True(service.AddReply(note.Id, "RESPONDER", request).Succeeded);
        Assert.True(service.AddReply(note.Id, "responder", request).Succeeded);
        var reply = Assert.Single(note.Replies);
        Assert.Equal("responder", reply.UserName); Assert.Equal("Response <test>\nSecond line", reply.ReplyText);
        Assert.InRange(reply.Timestamp, before, DateTimeOffset.UtcNow);
        Assert.False(service.AddReply(note.Id, "responder", request with { ReplyText = "Changed retry" }).Succeeded);
        Assert.Equal(original, JsonSerializer.Serialize(new {note.Id,note.JobNum,note.PartNum,note.ResourceId,note.OperatorUserId,note.NoteText,note.Timestamp}));
        Assert.Single(service.Read(new(Search: "Second line")).Entries);
        Assert.Single(service.GetForJob("J1")[0].Replies!);
        Assert.Single(new JobHistoryService(r).GetForJob("J1").Single(item => item.EntryType == "Note").NoteReplies!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyResponsesAreRejected(string? text)
    {
        var r = Repository();
        Assert.False(new JobNoteService(r).AddReply(r.JobNotes[0].Id, "responder", new(text!, Guid.NewGuid())).Succeeded);
        Assert.Empty(r.JobNotes[0].Replies);
    }

    [Fact]
    public void InvalidRequestsDoNotCreateResponses()
    {
        var r = Repository(); var service = new JobNoteService(r); var note = r.JobNotes[0];
        Assert.False(service.AddReply(note.Id,"responder",new(new string('x',1001),Guid.NewGuid())).Succeeded);
        Assert.False(service.AddReply(note.Id,"responder",new("Text",Guid.Empty)).Succeeded);
        Assert.False(service.AddReply(Guid.NewGuid(),"responder",new("Text",Guid.NewGuid())).Succeeded);
        Assert.Empty(note.Replies);
    }

    [Fact]
    public void RepliesSurviveDatabaseSaveBackupAndRestoreAndExportWithTheirParent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "spc-note-replies-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var r = new SqliteBackedSpcRepository(Path.Combine(folder,"data.db")); r.ImportFrom(Repository());
            var note = r.JobNotes[0]; Assert.True(new JobNoteService(r).AddReply(note.Id,"responder",new("Saved response",Guid.NewGuid())).Succeeded);
            r.SaveChanges(); var backup=Path.Combine(folder,"backup.db"); r.BackupTo(backup);
            var reloaded = new SqliteBackedSpcRepository(r.StoragePath);
            Assert.Equal("Saved response",Assert.Single(Assert.Single(reloaded.JobNotes).Replies).ReplyText);
            reloaded.JobNotes.Clear(); reloaded.SaveChanges(); reloaded.RestoreFrom(backup);
            Assert.Single(Assert.Single(reloaded.JobNotes).Replies);
            var bytes=new HistoryExportService(reloaded).ExportLedgerXlsx(new(["70311"],["J1"],null,null));
            using var zip=new ZipArchive(new MemoryStream(bytes));using var reader=new StreamReader(zip.GetEntry("xl/worksheets/sheet5.xml")!.Open());
            var xml=reader.ReadToEnd();Assert.Contains("Saved response",xml);Assert.Contains("responder",xml);Assert.Contains("Replies",xml);
        }
        finally { Directory.Delete(folder,true); }
    }

    [Fact]
    public void OldNotesDefaultToAnEmptyReplyThread()
    {
        var note=JsonSerializer.Deserialize<JobNote>("""{"JobNum":"J1","PartNum":"70311","ResourceId":"4S-1","OperatorUserId":"operator","NoteText":"Old note"}""")!;
        Assert.Empty(note.Replies); Assert.Equal("Old note",note.NoteText);
    }

    [Fact]
    public void ArchivingANoteRetainsItsWholeReplyThread()
    {
        var folder=Path.Combine(Path.GetTempPath(),"spc-note-archive-"+Guid.NewGuid().ToString("N"));
        try
        {
            var r=Repository();Assert.True(new JobNoteService(r).AddReply(r.JobNotes[0].Id,"responder",new("Archived response",Guid.NewGuid())).Succeeded);
            var service=new ArchiveService(r,new CredentialService(r),new PermissionService(r),folder);
            var result=service.Create(new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"),"Archon","archon","ARCHIVE"));
            Assert.True(result.Succeeded,string.Join(";",result.Errors));Assert.Empty(r.JobNotes);
            Assert.Contains("Archived response",File.ReadAllText(result.Value!.ArchivePath));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
