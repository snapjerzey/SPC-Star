using System.Text.Json;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ChangeHistoryServiceTests
{
    private static InMemorySpcRepository Repository()
    {
        var r=new InMemorySpcRepository();
        var part=new Part {PartNum="61033",Description="Test part"};
        var process=new ManufacturingProcess {ProcessCode="Schneider",Description="Schneider"};
        var operation=new Operation {PartId=part.Id,ProcessId=process.Id,OperationSeq=10};
        var variable=new Characteristic {OperationId=operation.Id,Name="Overall Height - Upper",Type=CharacteristicType.Variable,UnitOfMeasure="in"};
        r.Parts.Add(part);r.Processes.Add(process);r.Operations.Add(operation);r.Characteristics.Add(variable);
        r.SpecLimits.Add(new(){CharacteristicId=variable.Id,Lsl=.30m,Usl=.32m});
        r.InspectionPlans.Add(new(){CharacteristicId=variable.Id,InspectionPhase="In Process",SampleSize=3,DisplayOrder=22,
            Lsl=.30m,Usl=.32m,AlertRuleSet="Custom",Frequency=new(){Type=FrequencyType.Quantity,Value=20000,Unit=FrequencyUnit.Pieces}});
        return r;
    }

    [Fact]
    public void SpecChange_LogsExactOldAndNewValuesWithoutChangingOrderOrAnyOtherData()
    {
        var r=Repository();var service=new ChangeHistoryService(r);
        var before=service.Capture();var initial=JsonSerializer.Serialize(r);
        r.InspectionPlans[0].Usl=.31m;
        var expected=JsonSerializer.Serialize(service.Capture());
        var entry=service.RecordSaved(before,"qa","Parts & Inspections")!;
        var record=Assert.Single(entry.Changes);var field=Assert.Single(record.Fields);
        Assert.Equal("USL",field.Field);Assert.Equal("0.32",field.Before);Assert.Equal("0.31",field.After);
        Assert.Equal("61033",record.PartNum);Assert.Equal("Schneider 10",record.Operation);Assert.Equal("In Process",record.Phase);
        Assert.Equal(22,r.InspectionPlans[0].DisplayOrder);Assert.Equal(3,r.InspectionPlans[0].SampleSize);
        Assert.Equal(expected,JsonSerializer.Serialize(service.Capture()));
        r.InspectionPlans[0].Usl=.32m;r.ChangeHistory.Clear();Assert.Equal(initial,JsonSerializer.Serialize(r));
    }

    [Fact]
    public void UnchangedSave_DoesNotCreateHistoryOrNormalizeAnything()
    {
        var r=Repository();var service=new ChangeHistoryService(r);var initial=JsonSerializer.Serialize(r);
        Assert.Null(service.RecordSaved(service.Capture(),"qa","Parts & Inspections"));
        Assert.Equal(initial,JsonSerializer.Serialize(r));Assert.Empty(r.ChangeHistory);
    }

    [Fact]
    public void AddedAndDeletedVariablesRetainActualFieldsAndContext()
    {
        var r=Repository();var service=new ChangeHistoryService(r);var before=service.Capture();
        var added=new InspectionPlan {CharacteristicId=r.Characteristics[0].Id,InspectionPhase="Setup",SampleSize=6,DisplayOrder=4,Usl=.320m,AlertRuleSet="Custom"};
        r.InspectionPlans.Add(added);
        var addition=Assert.Single(service.RecordSaved(before,"qa","Parts & Inspections")!.Changes);
        Assert.Equal("Added",addition.Action);Assert.Equal("Setup",addition.Phase);
        Assert.Contains(addition.Fields,item=>item.Field=="Sample Size"&&item.After=="6");
        Assert.Contains(addition.Fields,item=>item.Field=="USL"&&item.After=="0.320");
        before=service.Capture();r.InspectionPlans.Remove(added);
        var deletion=Assert.Single(service.RecordSaved(before,"qa","Parts & Inspections")!.Changes);
        Assert.Equal("Deleted",deletion.Action);Assert.Contains(deletion.Fields,item=>item.Field=="Sample Size"&&item.Before=="6"&&item.After==null);
    }

    [Fact]
    public void ImportsProduceOnlyOneShortEntry_NoFieldDetails()
    {
        var r=Repository();var service=new ChangeHistoryService(r);var before=service.Capture();
        r.InspectionPlans[0].Usl=.31m;r.Parts[0].Description="Changed by import";
        var entry=service.RecordSaved(before,"manager","Parts & Inspections workbook imported: current.xlsx",isImport:true)!;
        Assert.Single(r.ChangeHistory);Assert.Empty(entry.Changes);Assert.Equal("Imports",entry.Source);
        Assert.Equal("Parts & Inspections workbook imported: current.xlsx",entry.Summary);
    }

    [Fact]
    public void UserChangesNeverStorePasswordsHashesOrSalts()
    {
        var r=Repository();var service=new ChangeHistoryService(r);
        var user=new User {UserName="operator",PasswordHash="old-sensitive-hash",PasswordSalt="old-sensitive-salt"};r.Users.Add(user);
        var before=service.Capture();user.PasswordHash="new-sensitive-hash";user.PasswordSalt="new-sensitive-salt";
        var field=Assert.Single(Assert.Single(service.RecordSaved(before,"qa","Users")!.Changes).Fields);
        Assert.Equal("Password",field.Field);Assert.Equal("Reset",field.After);
        Assert.DoesNotContain("sensitive",JsonSerializer.Serialize(r.ChangeHistory));
        before=service.Capture();user.Roles.Add(new(){Name="LineTech"});user.Shift="5 Day";
        var changes=Assert.Single(service.RecordSaved(before,"qa","Users")!.Changes).Fields;
        Assert.Contains(changes,item=>item.Field=="Roles"&&item.After=="LineTech");
    }

    [Fact]
    public void CorrectionsOnlyCaptureTheCorrectedEntry_NotOtherSavedReadings()
    {
        var r=Repository();var reading=new InspectionMeasurement {JobNum="J",PartNum="61033",ProcessCode="Schneider",OperationSeq=10,
            ResourceId="4S-1",CharacteristicName="Height",OperatorUserId="op",Value=.32m};r.Measurements.Add(reading);
        var service=new ChangeHistoryService(r);var target=new ChangeHistoryTarget("Measurement",reading.Id);var before=service.Capture(target);
        reading.Value=.31m;
        var entry=service.RecordSaved(before,"qa","Inspection Corrections",target:target,reason:"Corrected entry")!;
        Assert.Equal("Corrected entry",entry.Reason);Assert.Equal("Inspection Corrections",Assert.Single(entry.Changes).Area);
        Assert.Equal("Value",Assert.Single(entry.Changes[0].Fields).Field);
    }

    [Fact]
    public void MultiplePhaseWritesForOneSaveReadAsOneEntry_WithAllActualChanges()
    {
        var r=Repository();var service=new ChangeHistoryService(r);var group=Guid.NewGuid().ToString();
        var before=service.Capture();r.InspectionPlans[0].Usl=.31m;service.RecordSaved(before,"qa","Parts & Inspections",groupId:group);
        before=service.Capture();r.Parts[0].Description="Updated";service.RecordSaved(before,"qa","Parts & Inspections",groupId:group);
        var entry=Assert.Single(service.Read(new()).Entries);Assert.Equal(2,entry.Changes.Count);Assert.Equal(2,r.ChangeHistory.Count);
        Assert.Single(service.Read(new(PartNum:"61033",UserName:"qa",Area:"Parts & Inspections",Search:"USL")).Entries);
        Assert.Empty(service.Read(new(PartNum:"Other")).Entries);
    }

    [Fact]
    public void RecordingDoesNotChangeAnyResultOfTheExistingSetupSave()
    {
        var folder=Path.Combine(Path.GetTempPath(),"spc-audit-isolation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"data.db");var seed=new SqliteBackedSpcRepository(path);seed.ImportFrom(Repository());seed.SaveChanges();
            var baseline=new SqliteBackedSpcRepository(path);var recorded=new SqliteBackedSpcRepository(path);
            var request=new UpsertInspectionSetupRequest("61033","Test part","General Production","Schneider","Schneider",10,
                "Overall Height - Upper",CharacteristicType.Variable,null,.30m,.31m,null,null,"in",3,FrequencyType.Quantity,20000,FrequencyUnit.Pieces,"Custom");
            Assert.True(new SetupManagementService(baseline).UpsertInspectionSetup(request).Succeeded);
            var service=new ChangeHistoryService(recorded);var before=service.Capture();
            Assert.True(new SetupManagementService(recorded).UpsertInspectionSetup(request).Succeeded);
            service.RecordSaved(before,"qa","Parts & Inspections");
            Assert.Single(recorded.ChangeHistory);recorded.ChangeHistory.Clear();
            Assert.Equal(JsonSerializer.Serialize(baseline),JsonSerializer.Serialize(recorded));
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public void ClearAndRestoreKeepChangeHistoryAndRecordOnlyTheirOwnAction()
    {
        var folder=Path.Combine(Path.GetTempPath(),"spc-audit-admin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var r=new SqliteBackedSpcRepository(Path.Combine(folder,"data.db"));SeedData.SeedSecurity(r);
            var history=new ChangeHistoryService(r);history.AddAction("manager","Imports","Initial import");r.SaveChanges();
            var backups=Path.Combine(folder,"backups");Directory.CreateDirectory(backups);r.BackupTo(Path.Combine(backups,"known.db"));
            var service=new BackupService(r,r,new CredentialService(r),new PermissionService(r),backups,Path.Combine(folder,"quarantine"),history);
            Assert.True(service.ClearHistoryData(new("Archon","archon","CLEAR HISTORY DATA")).Succeeded);
            Assert.Equal(2,r.ChangeHistory.Count);
            Assert.True(service.RestoreLatestBackup(new("Archon","archon","RESTORE")).Succeeded);
            Assert.Equal(3,r.ChangeHistory.Count);Assert.Equal(3,new SqliteBackedSpcRepository(r.StoragePath).ChangeHistory.Count);
            Assert.Contains(r.ChangeHistory,entry=>entry.Summary=="Inspection history cleared");
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public void HistoryPersistsAndCannotDisappearWhenAnOlderBackupIsRestored()
    {
        var folder=Path.Combine(Path.GetTempPath(),"spc-audit-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var db=Path.Combine(folder,"data.db");var backup=Path.Combine(folder,"before.db");
            var r=new SqliteBackedSpcRepository(db);var service=new ChangeHistoryService(r);r.SaveChanges();r.BackupTo(backup);
            service.AddAction("manager","Imports","Machine workbook imported: machines.xlsx");r.SaveChanges();
            Assert.Single(new SqliteBackedSpcRepository(db).ChangeHistory);
            r.RestoreFrom(backup);Assert.Single(r.ChangeHistory);Assert.Single(new SqliteBackedSpcRepository(db).ChangeHistory);
        }
        finally {Directory.Delete(folder,true);}
    }
}
