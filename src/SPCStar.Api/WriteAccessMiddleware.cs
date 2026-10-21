using SPCStar.Core.Domain;
using SPCStar.Core.Services;

public sealed class WriteAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuthSessionService auth, PermissionService permissions)
    {
        var required = RequiredPermission(context.Request.Method, context.Request.Path.Value ?? "");
        if (required is not null)
        {
            var user = context.Request.Headers["X-SPCStar-User"].ToString();
            var token = context.Request.Headers["X-SPCStar-Session"].ToString();
            if (!auth.ValidateSession(user,token))
            {
                context.Response.StatusCode=401;
                await context.Response.WriteAsJsonAsync(new {errors=new[]{"Your session is no longer valid. Sign in again."}});
                return;
            }
            if (!permissions.UserHasPermission(user,required))
            {
                context.Response.StatusCode=403;
                await context.Response.WriteAsJsonAsync(new {errors=new[]{"Your signed-in access level does not allow this action."}});
                return;
            }
        }
        await next(context);
    }

    public static string? RequiredPermission(string method,string path)
    {
        if (path.StartsWith("/setup/backups/files/",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup/archive/files/",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanUseGodMode;
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return null;
        if (path.StartsWith("/admin/job-notes/",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanViewChangeHistory;
        if (path.Equals("/admin/backups",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup/archive",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup/backups/",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup/database/",StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/certs/ethicon/seed-test-data",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanUseGodMode;
        if (path.StartsWith("/certs/",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanGenerateCerts;
        if (path.StartsWith("/setup/",StringComparison.OrdinalIgnoreCase) && path.Contains("import",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanImportSetupData;
        if (path.StartsWith("/setup/users",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanManageUsers;
        if (path.StartsWith("/setup/",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanManageInspectionPlans;
        if (path.StartsWith("/review/",StringComparison.OrdinalIgnoreCase) ||
            (method=="PATCH" && path.StartsWith("/material-changes/",StringComparison.OrdinalIgnoreCase))) return PermissionNames.CanEditInspectionHistory;
        if (path.StartsWith("/inspections/",StringComparison.OrdinalIgnoreCase) || path.StartsWith("/material-changes",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/alerts/",StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/jobs/",StringComparison.OrdinalIgnoreCase) && (path.EndsWith("/tags",StringComparison.OrdinalIgnoreCase) || path.EndsWith("/notes",StringComparison.OrdinalIgnoreCase))) ||
            path.Equals("/sync/offline-changes",StringComparison.OrdinalIgnoreCase)) return PermissionNames.CanEnterInspections;
        return null;
    }
}
