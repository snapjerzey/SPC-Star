using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class SetupWorkbookMaintenanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfflineUpdateUsesExistingParserAndOnlyPersistsSuccessfulImports(bool valid)
    {
        var repository = new InMemorySpcRepository();
        SeedData.SeedSecurity(repository);
        repository.JobNotes.Add(new JobNote { JobNum="Existing", PartNum="Existing", ResourceId="M", OperatorUserId="operator", NoteText="Keep this" });
        var retained = JsonSerializer.Serialize(new {repository.Users,repository.Roles,repository.JobNotes});
        var before = JsonSerializer.Serialize(repository);
        var persistence = new TestPersistence();
        var path = Path.Combine(Path.GetTempPath(),$"spc-offline-{Guid.NewGuid():N}.xlsx");
        try
        {
            WriteWorkbook(path,valid);
            var result = new SetupWorkbookMaintenanceService(repository,persistence).Apply(path,"Windows updater");
            Assert.Equal(valid,result.Succeeded);
            Assert.Equal(valid ? 1 : 0,persistence.Saves);
            Assert.Equal(retained,JsonSerializer.Serialize(new {repository.Users,repository.Roles,repository.JobNotes}));
            if(valid)
            {
                var entry=Assert.Single(repository.ChangeHistory);
                Assert.Equal("Windows updater",entry.UserName);
                Assert.Empty(entry.Changes);
                Assert.Contains(Path.GetFileName(path),entry.Summary);
                Assert.Single(repository.InspectionPlans);
            }
            else Assert.Equal(before,JsonSerializer.Serialize(repository));
        }
        finally {File.Delete(path);}
    }

    private static void WriteWorkbook(string path,bool valid)
    {
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg="http://schemas.openxmlformats.org/package/2006/relationships";
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Write(string name,XElement root) {using var stream=zip.CreateEntry(name).Open();new XDocument(root).Save(stream);}
        Write("xl/workbook.xml",new XElement(ns+"workbook",new XElement(ns+"sheets",new XElement(ns+"sheet",new XAttribute("name","SPC-Star Import"),new XAttribute("sheetId",1),new XAttribute(rel+"id","rId1")))));
        Write("xl/_rels/workbook.xml.rels",new XElement(pkg+"Relationships",new XElement(pkg+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Target","worksheets/sheet1.xml"),new XAttribute("Type",rel.NamespaceName+"/worksheet"))));
        var rows=new[]{new[]{"Section","Part Number","Part Description","Product Group","Operation","Inspection Item","Attribute/Variable","Setup Required","Setup Sample Size","Sort Order"},new[]{"Inspection",valid ? "OFFLINE-TEST" : "","Test part","General Production","Test Operation","Visual","Attribute","X","1","1"}};
        Write("xl/worksheets/sheet1.xml",new XElement(ns+"worksheet",new XElement(ns+"sheetData",rows.Select((row,i)=>new XElement(ns+"row",new XAttribute("r",i+1),row.Select((value,j)=>new XElement(ns+"c",new XAttribute("r",$"{(char)('A'+j)}{i+1}"),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",value)))))))));
    }

    private sealed class TestPersistence : IRepositoryPersistence
    {
        public string StoragePath=>"unused";
        public int Saves {get;private set;}
        public void SaveChanges()=>Saves++;
        public void BackupTo(string path)=>throw new NotSupportedException();
        public void RestoreFrom(string path)=>throw new NotSupportedException();
    }
}
