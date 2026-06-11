using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class JobNoteServiceTests
{
    [Fact]
    public void Add_StoresTimestampedJobNote()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = new JobNoteService(repository);

        var result = service.Add(new JobNoteEntry(
            "J100",
            "P100",
            "PRESS1",
            "operator1",
            "Press was adjusted after flash showed up.",
            DateTimeOffset.Parse("2026-05-12T08:15:00Z")));

        Assert.True(result.Succeeded);
        Assert.Single(repository.JobNotes);
        Assert.Equal("operator1", result.Value!.OperatorUserId);
        Assert.Equal("Press was adjusted after flash showed up.", result.Value.NoteText);
        Assert.Equal(DateTimeOffset.Parse("2026-05-12T08:15:00Z"), result.Value.Timestamp);
    }

    [Fact]
    public void GetForJob_ReturnsNewestNotesFirst()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);
        var service = new JobNoteService(repository);
        service.Add(new JobNoteEntry("J100", "P100", "PRESS1", "operator1", "First note", DateTimeOffset.Parse("2026-05-12T08:00:00Z")));
        service.Add(new JobNoteEntry("J100", "P100", "PRESS1", "linetech1", "Second note", DateTimeOffset.Parse("2026-05-12T09:00:00Z")));

        var notes = service.GetForJob("J100");

        Assert.Equal(["Second note", "First note"], notes.Select(note => note.NoteText).ToArray());
    }

    [Fact]
    public void Add_RejectsBlankNote()
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedAll(repository);
        SeedData.SeedSampleInspectionPlans(repository);

        var result = new JobNoteService(repository).Add(new JobNoteEntry("J100", "P100", "PRESS1", "operator1", " "));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("NoteText is required", StringComparison.OrdinalIgnoreCase));
    }
}
