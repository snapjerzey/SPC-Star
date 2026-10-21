using SPCStar.Core.Infrastructure;

namespace SPCStar.Core.Services;

// Used by the stopped-server updater, never exposed as an HTTP endpoint.
public sealed class SetupWorkbookMaintenanceService(ISpcRepository repository, IRepositoryPersistence persistence)
{
    public ServiceResult Apply(string workbookPath, string windowsUser)
    {
        using var stream = File.OpenRead(workbookPath);
        var csv = XlsxImportSupport.ReadImportSheetAsCsv(stream);
        var result = new SetupImportService(repository).ImportCsv(csv, replaceSetup: true);
        if (!result.Succeeded) return result;
        new ChangeHistoryService(repository).AddAction(windowsUser, "Imports",
            $"Server update workbook imported: {Path.GetFileName(workbookPath)}");
        persistence.SaveChanges();
        return ServiceResult.Ok();
    }
}
