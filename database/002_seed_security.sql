INSERT INTO Permissions (Name) VALUES
('CanEnterInspections'),
('CanOverrideDriftLock'),
('CanManageInspectionPlans'),
('CanImportSetupData'),
('CanExportQAData'),
('CanManageUsers'),
('CanUseGodMode');

INSERT INTO Roles (Id, Name) VALUES
('role-operator', 'Operator'),
('role-linetech', 'LineTech'),
('role-qa', 'QA'),
('role-admin', 'Admin'),
('role-god', 'GOD');

INSERT INTO RolePermissions (RoleId, PermissionName) VALUES
('role-operator', 'CanEnterInspections'),
('role-linetech', 'CanEnterInspections'),
('role-linetech', 'CanOverrideDriftLock'),
('role-qa', 'CanOverrideDriftLock'),
('role-qa', 'CanExportQAData'),
('role-admin', 'CanEnterInspections'),
('role-admin', 'CanManageInspectionPlans'),
('role-admin', 'CanImportSetupData'),
('role-admin', 'CanManageUsers'),
('role-god', 'CanEnterInspections'),
('role-god', 'CanOverrideDriftLock'),
('role-god', 'CanManageInspectionPlans'),
('role-god', 'CanImportSetupData'),
('role-god', 'CanExportQAData'),
('role-god', 'CanManageUsers'),
('role-god', 'CanUseGodMode');
