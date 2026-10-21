using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;

public sealed class SetupWriteEndpoint(AuthSessionService auth,
    PermissionService permissions, IRepositoryPersistence persistence, ChangeHistoryService history,
    ISpcRepository repository)
{
    private readonly object sync = new();
    public IResult Run(HttpContext context, string source, Func<IResult> update, bool isImport = false)
    {
        var actor = ResolveActor(context, isImport);
        if (!actor.Succeeded) return Results.Json(new { errors = actor.Errors }, statusCode: 403);
        return Observe(actor.Value!, source, update, isImport, groupId: GroupId(context));
    }

    public IResult Observe(string actor, string source, Func<IResult> update, bool isImport = false,
        ChangeHistoryTarget? target = null, string? reason = null, string? groupId = null, bool actionOnly = false)
    {
        lock (sync)
        {
            var before = isImport || actionOnly ? null : history.Capture(target);
            var result = update();
            if (result is IStatusCodeHttpResult status && status.StatusCode is >= 200 and < 300)
            {
                var entry = actionOnly ? history.AddAction(actor, "Admin", source) : isImport
                    ? history.AddAction(actor, "Imports", source, groupId)
                    : history.RecordSaved(before!, actor, source, target: target, reason: reason, groupId: groupId);
                try { persistence.SaveChanges(); }
                catch { if (entry is not null) repository.ChangeHistory.Remove(entry); throw; }
            }
            return result;
        }
    }

    public string RequestActor(HttpContext context, string? recordedUser = null)
    {
        var user = context.Request.Headers["X-SPCStar-User"].ToString();
        return auth.ValidateSession(user, context.Request.Headers["X-SPCStar-Session"].ToString())
            ? user : string.IsNullOrWhiteSpace(recordedUser) ? "Unknown" : recordedUser;
    }

    private static string? GroupId(HttpContext context) => Guid.TryParse(context.Request.Headers["X-SPCStar-Change-Group"], out var id) ? id.ToString() : null;

    public ServiceResult<string> ResolveActor(HttpContext context, bool isImport = false)
    {
        var user = context.Request.Headers["X-SPCStar-User"].ToString();
        var token = context.Request.Headers["X-SPCStar-Session"].ToString();
        var permission = isImport ? PermissionNames.CanImportSetupData : PermissionNames.CanManageInspectionPlans;
        if (auth.ValidateSession(user, token) && permissions.UserHasPermission(user, permission))
            return ServiceResult<string>.Ok(user.Trim());
        return ServiceResult<string>.Fail(isImport ? "Import is restricted to signed-in System Managers."
            : "Sign in with Parts & Inspections access before updating setup.");
    }
}
