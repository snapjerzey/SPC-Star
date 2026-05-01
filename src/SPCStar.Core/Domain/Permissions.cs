namespace SPCStar.Core.Domain;

public static class PermissionNames
{
    public const string CanOverrideDriftLock = nameof(CanOverrideDriftLock);
    public const string CanManageInspectionPlans = nameof(CanManageInspectionPlans);
    public const string CanImportSetupData = nameof(CanImportSetupData);
    public const string CanExportQAData = nameof(CanExportQAData);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanUseGodMode = nameof(CanUseGodMode);
}

public static class RoleNames
{
    public const string Operator = nameof(Operator);
    public const string LineTech = nameof(LineTech);
    public const string QA = nameof(QA);
    public const string Admin = nameof(Admin);
    public const string GOD = nameof(GOD);
}
