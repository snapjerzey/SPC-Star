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
    DateTimeOffset Timestamp,
    IReadOnlyList<JobNoteReplyDto>? Replies = null);

public sealed record JobNoteReplyEntry(string ReplyText, Guid ClientRequestId);

public sealed record JobNoteReplyDto(Guid Id, string UserName, string ReplyText, DateTimeOffset Timestamp)
{
    public static JobNoteReplyDto From(JobNoteReply reply) => new(reply.Id, reply.UserName, reply.ReplyText, reply.Timestamp);
}

public sealed record JobNotesQuery(string? Search = null, string? PartNum = null, string? JobNum = null,
    string? ResourceId = null, string? UserName = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, int Page = 1, int PageSize = 50, string? RepliedBy = null);

public sealed record JobNotesPageDto(IReadOnlyList<JobNoteDto> Entries, int Total, int Page, int PageSize);

public sealed class JobNoteService(ISpcRepository repository)
{
    public ServiceResult<JobNoteDto> AddReply(Guid noteId, string userName, JobNoteReplyEntry entry)
    {
        if (!new PermissionService(repository).UserHasPermission(userName, PermissionNames.CanViewChangeHistory))
            return ServiceResult<JobNoteDto>.Fail("QA or System Manager access is required to reply to a job note.");
        if (string.IsNullOrWhiteSpace(entry.ReplyText) || entry.ReplyText.Trim().Length > 1000)
            return ServiceResult<JobNoteDto>.Fail("Enter a response between 1 and 1000 characters.");
        if (entry.ClientRequestId == Guid.Empty)
            return ServiceResult<JobNoteDto>.Fail("A reply request identifier is required.");
        var note = repository.JobNotes.FirstOrDefault(item => item.Id == noteId);
        if (note is null) return ServiceResult<JobNoteDto>.Fail("The job note was not found.");
        var actor = repository.Users.First(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)).UserName;
        lock (note)
        {
            var existing = note.Replies.FirstOrDefault(reply => reply.ClientRequestId == entry.ClientRequestId);
            if (existing is not null)
            {
                if (existing.UserName != actor || existing.ReplyText != entry.ReplyText.Trim())
                    return ServiceResult<JobNoteDto>.Fail("This reply request has already been used. Reload the notes and try again.");
                return ServiceResult<JobNoteDto>.Ok(ToDto(note));
            }
            note.Replies.Add(new JobNoteReply { UserName = actor, ReplyText = entry.ReplyText.Trim(),
                Timestamp = DateTimeOffset.UtcNow, ClientRequestId = entry.ClientRequestId });
            return ServiceResult<JobNoteDto>.Ok(ToDto(note));
        }
    }

    public JobNotesPageDto Read(JobNotesQuery query)
    {
        IEnumerable<JobNote> notes = repository.JobNotes;
        bool Matches(string actual, string? filter) => string.IsNullOrWhiteSpace(filter) ||
            actual.Equals(filter.Trim(), StringComparison.OrdinalIgnoreCase);
        notes = notes.Where(note => Matches(note.PartNum, query.PartNum) && Matches(note.JobNum, query.JobNum) &&
            Matches(note.ResourceId, query.ResourceId) && Matches(note.OperatorUserId, query.UserName) &&
            (string.IsNullOrWhiteSpace(query.RepliedBy) || note.Replies.Any(reply => Matches(reply.UserName, query.RepliedBy))) &&
            (!query.From.HasValue || note.Timestamp >= query.From.Value) &&
            (!query.To.HasValue || note.Timestamp <= query.To.Value));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            notes = notes.Where(note => new[] { note.NoteText, note.PartNum, note.JobNum, note.ResourceId, note.OperatorUserId }
                .Concat(note.Replies.SelectMany(reply => new[] { reply.ReplyText, reply.UserName }))
                .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        var ordered = notes.OrderByDescending(note => note.Timestamp).ThenByDescending(note => note.Id).ToArray();
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var lastPage = Math.Max(1, (int)Math.Ceiling(ordered.Length / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, lastPage);
        return new(ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToArray(), ordered.Length, page, pageSize);
    }

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
            note.Timestamp,
            note.Replies.OrderBy(reply => reply.Timestamp).ThenBy(reply => reply.Id).Select(JobNoteReplyDto.From).ToArray());
    }
}
