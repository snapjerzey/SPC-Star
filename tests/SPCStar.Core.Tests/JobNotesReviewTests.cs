using System.Text.Json;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobNotesReviewTests
{
    private static InMemorySpcRepository Repository()
    {
        var r = new InMemorySpcRepository();
        for (var i = 0; i < 7; i++)
            r.JobNotes.Add(new JobNote { JobNum = i % 2 == 0 ? "J1" : "J2", PartNum = i % 2 == 0 ? "70311" : "61033",
                ResourceId = i % 2 == 0 ? "4S-1" : "4S-2", OperatorUserId = i % 2 == 0 ? "operator" : "tech",
                NoteText = $"Note {i}\nKeep the exact text <test>", Timestamp = DateTimeOffset.Parse("2026-10-20T12:00:00Z").AddHours(i) });
        return r;
    }

    [Fact]
    public void ReadAcrossJobsIsNewestFirstPagedAndDoesNotChangeSavedNotes()
    {
        var r = Repository(); var before = JsonSerializer.Serialize(r.JobNotes);
        var service = new JobNoteService(r);
        var first = service.Read(new(PageSize: 3)); var second = service.Read(new(Page: 2, PageSize: 3));
        Assert.Equal(7, first.Total); Assert.Equal(3, first.Entries.Count);
        Assert.Equal("Note 6\nKeep the exact text <test>", first.Entries[0].NoteText);
        Assert.Equal("Note 3\nKeep the exact text <test>", second.Entries[0].NoteText);
        Assert.Contains(first.Entries, n => n.JobNum == "J1"); Assert.Contains(first.Entries, n => n.JobNum == "J2");
        Assert.Empty(first.Entries.Select(n => n.Id).Intersect(second.Entries.Select(n => n.Id)));
        Assert.Equal(before, JsonSerializer.Serialize(r.JobNotes));
    }

    [Fact]
    public void AllFiltersCombineWithoutChangingOriginalJobNoteQueries()
    {
        var r = Repository(); var service = new JobNoteService(r);
        var result = service.Read(new(Search: " NOTE 4 ", PartNum: "70311", JobNum: " j1 ", ResourceId: "4s-1", UserName: "OPERATOR",
            From: DateTimeOffset.Parse("2026-10-20T12:00:00-04:00"), To: DateTimeOffset.Parse("2026-10-20T12:00:00-04:00")));
        Assert.Single(result.Entries); Assert.StartsWith("Note 4", result.Entries[0].NoteText);
        Assert.Equal(4, service.GetForJob("J1").Count);
        Assert.Empty(service.Read(new(PartNum: "70311", JobNum: "J2")).Entries);
    }

    [Theory]
    [InlineData("4S-2", 3)]
    [InlineData("tech", 3)]
    [InlineData("61033", 3)]
    [InlineData("missing", 0)]
    public void SearchMatchesNoteTextAndJobContext(string search, int expected)
    {
        Assert.Equal(expected, new JobNoteService(Repository()).Read(new(Search: search)).Total);
    }

    [Fact]
    public void ResponderFilterMatchesRepliesNotOriginalAuthorAndPreservesWholeThreads()
    {
        var r = Repository();
        var note = r.JobNotes[4];
        note.Replies.Add(new JobNoteReply { UserName = "QualityUser", ReplyText = "First reply", Timestamp = DateTimeOffset.UtcNow });
        note.Replies.Add(new JobNoteReply { UserName = "Manager", ReplyText = "Second reply", Timestamp = DateTimeOffset.UtcNow });
        r.JobNotes[3].Replies.Add(new JobNoteReply { UserName = "QualityUser", ReplyText = "Other job", Timestamp = DateTimeOffset.UtcNow });
        var before = JsonSerializer.Serialize(r.JobNotes);
        var service = new JobNoteService(r);
        Assert.Equal(2, service.Read(new(RepliedBy: " qualityUSER ")).Total);
        var result = service.Read(new(RepliedBy: "qualityuser", PartNum: "70311", JobNum: "J1", UserName: "operator", Search: "Note 4"));
        Assert.Single(result.Entries); Assert.Equal(note.Id, result.Entries[0].Id);
        Assert.Equal(2, result.Entries[0].Replies!.Count);
        Assert.Empty(service.Read(new(RepliedBy: "operator")).Entries);
        Assert.Empty(service.Read(new(RepliedBy: "Quality")).Entries);
        Assert.Equal(7, service.Read(new(RepliedBy: " ")).Total);
        Assert.Equal(before, JsonSerializer.Serialize(r.JobNotes));
    }

    [Fact]
    public void ResponderFilterAppliesBeforePaginationAndCountsEachNoteOnlyOnce()
    {
        var r = Repository();
        foreach (var note in r.JobNotes)
            for (var i = 0; i < 2; i++) note.Replies.Add(new JobNoteReply { UserName = "QA", ReplyText = "Response", Timestamp = DateTimeOffset.UtcNow });
        var service = new JobNoteService(r);
        var first = service.Read(new(RepliedBy: "qa", PageSize: 3));
        var second = service.Read(new(RepliedBy: "qa", PageSize: 3, Page: 2));
        Assert.Equal(7, first.Total); Assert.Equal(3, first.Entries.Count);
        Assert.Empty(first.Entries.Select(n => n.Id).Intersect(second.Entries.Select(n => n.Id)));
    }

    [Fact]
    public void PaginationIsBoundedAndEmptyResultsAreExplicit()
    {
        var service = new JobNoteService(Repository());
        var last = service.Read(new(Page: int.MaxValue, PageSize: 3));
        Assert.Equal(3, last.Page); Assert.Single(last.Entries);
        Assert.Equal(1, service.Read(new(Page: -1, PageSize: 0)).Page);
        Assert.Equal(200, service.Read(new(PageSize: int.MaxValue)).PageSize);
        var empty = service.Read(new(JobNum: "missing", Page: 3));
        Assert.Equal(0, empty.Total); Assert.Equal(1, empty.Page); Assert.Empty(empty.Entries);
    }
}
