using SPCStar.Core.Domain;
using SPCStar.Core.Services;

public static class JobNotesEndpoints
{
    public static void MapJobNotesReview(this WebApplication app)
    {
        app.MapPost("/admin/job-notes/{noteId:guid}/replies", (Guid noteId, JobNoteReplyEntry request,
            HttpContext context, AuthSessionService auth, PermissionService permissions,
            JobNoteService notes, SPCStar.Core.Infrastructure.IRepositoryPersistence persistence) =>
        {
            var user = context.Request.Headers["X-SPCStar-User"].ToString();
            if (!auth.ValidateSession(user, context.Request.Headers["X-SPCStar-Session"].ToString()) ||
                !permissions.UserHasPermission(user, PermissionNames.CanViewChangeHistory)) return Results.StatusCode(403);
            var result = notes.AddReply(noteId, user, request);
            if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors });
            persistence.SaveChanges();
            return Results.Ok(result.Value);
        });
        app.MapGet("/admin/job-notes", (HttpContext context, AuthSessionService auth,
            PermissionService permissions, JobNoteService notes, string? search, string? partNum,
            string? jobNum, string? resourceId, string? userName, DateTimeOffset? from,
            DateTimeOffset? to, int? page, int? pageSize, string? repliedBy) =>
        {
            var user = context.Request.Headers["X-SPCStar-User"].ToString();
            if (!auth.ValidateSession(user, context.Request.Headers["X-SPCStar-Session"].ToString()) ||
                !permissions.UserHasPermission(user, PermissionNames.CanViewChangeHistory))
                return Results.StatusCode(403);
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                return Results.BadRequest(new { errors = new[] { "From must be earlier than or equal to To." } });
            return Results.Ok(notes.Read(new(search, partNum, jobNum, resourceId, userName, from, to, page ?? 1, pageSize ?? 50, repliedBy)));
        });
    }
}
