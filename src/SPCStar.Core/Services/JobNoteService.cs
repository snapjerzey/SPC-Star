using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

public sealed record JobNoteEntry(
    string JobNum,
    string PartNum,
    string ResourceId,
    string OperatorUserId,
    string NoteText,
    DateTimeOffset? Timestamp = null);

public sealed record JobNoteDto(
    Guid Id,
    string JobNum,
    string PartNum,
    string ResourceId,
    string OperatorUserId,
    string NoteText,
    DateTimeOffset Timestamp);

public sealed class JobNoteService(ISpcRepository repository)
{
    public IReadOnlyList<JobNoteDto> GetForJob(string jobNum)
    {
        if (string.IsNullOrWhiteSpace(jobNum))
        {
            return [];
        }

        return repository.JobNotes
            .Where(note => note.JobNum.Equals(jobNum.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.Timestamp)
            .Select(ToDto)
            .ToArray();
    }

    public ServiceResult<JobNoteDto> Add(JobNoteEntry entry)
    {
        var errors = Validate(entry);
        if (errors.Count > 0)
        {
            return ServiceResult<JobNoteDto>.Fail(errors);
        }

        if (!UserCanLeaveNotes(entry.OperatorUserId))
        {
            return ServiceResult<JobNoteDto>.Fail("User is not authorized to leave job notes.");
        }

        var note = new JobNote
        {
            JobNum = entry.JobNum.Trim(),
            PartNum = entry.PartNum.Trim(),
            ResourceId = entry.ResourceId.Trim(),
            OperatorUserId = entry.OperatorUserId.Trim(),
            NoteText = entry.NoteText.Trim(),
            Timestamp = entry.Timestamp ?? DateTimeOffset.UtcNow
        };

        repository.JobNotes.Add(note);
        return ServiceResult<JobNoteDto>.Ok(ToDto(note));
    }

    private bool UserCanLeaveNotes(string userName)
    {
        return repository.Users
            .FirstOrDefault(user => user.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Roles.Any(role => role.Permissions.Contains(PermissionNames.CanEnterInspections)) == true;
    }

    private static List<string> Validate(JobNoteEntry entry)
    {
        var errors = new List<string>();
        Required(entry.JobNum, nameof(entry.JobNum), errors);
        Required(entry.PartNum, nameof(entry.PartNum), errors);
        Required(entry.ResourceId, nameof(entry.ResourceId), errors);
        Required(entry.OperatorUserId, nameof(entry.OperatorUserId), errors);
        Required(entry.NoteText, nameof(entry.NoteText), errors);
        if (!string.IsNullOrWhiteSpace(entry.NoteText) && entry.NoteText.Trim().Length > 1000)
        {
            errors.Add("NoteText must be 1000 characters or fewer.");
        }

        return errors;
    }

    private static void Required(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static JobNoteDto ToDto(JobNote note)
    {
        return new JobNoteDto(
            note.Id,
            note.JobNum,
            note.PartNum,
            note.ResourceId,
            note.OperatorUserId,
            note.NoteText,
            note.Timestamp);
    }
}
