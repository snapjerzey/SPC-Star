INSERT INTO Permissions (Name) VALUES
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
('role-linetech', 'CanOverrideDriftLock'),
('role-qa', 'CanOverrideDriftLock'),
('role-qa', 'CanExportQAData'),
('role-admin', 'CanManageInspectionPlans'),
('role-admin', 'CanImportSetupData'),
('role-admin', 'CanManageUsers'),
('role-god', 'CanOverrideDriftLock'),
('role-god', 'CanManageInspectionPlans'),
('role-god', 'CanImportSetupData'),
('role-god', 'CanExportQAData'),
('role-god', 'CanManageUsers'),
('role-god', 'CanUseGodMode');
