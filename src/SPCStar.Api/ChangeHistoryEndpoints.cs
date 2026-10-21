using SPCStar.Core.Services;

public static class ChangeHistoryEndpoints
{
    public static void MapChangeHistory(this WebApplication app)
    {
        app.MapGet("/admin/change-history", (HttpContext context, AuthSessionService auth,
            PermissionService permissions, ChangeHistoryService history, string? search, string? userName,
            string? area, string? partNum, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize) =>
        {
            if (!Allowed(context,auth,permissions)) return Results.StatusCode(403);
            return Results.Ok(history.Read(new(search,userName,area,partNum,from,to,page ?? 1,pageSize ?? 50)));
        });
        app.MapGet("/admin/change-history/export.csv", (HttpContext context, AuthSessionService auth,
            PermissionService permissions, ChangeHistoryService history, string? search, string? userName,
            string? area, string? partNum, DateTimeOffset? from, DateTimeOffset? to) =>
        {
            if (!Allowed(context,auth,permissions)) return Results.StatusCode(403);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(history.Export(new(search,userName,area,partNum,from,to))),
                "text/csv", $"SPC-Star Change History {DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        });
    }
    private static bool Allowed(HttpContext context,AuthSessionService auth,PermissionService permissions)
    {
        var user=context.Request.Headers["X-SPCStar-User"].ToString();
        return auth.ValidateSession(user,context.Request.Headers["X-SPCStar-Session"].ToString()) &&
            permissions.UserHasPermission(user,SPCStar.Core.Domain.PermissionNames.CanViewChangeHistory);
    }
}
