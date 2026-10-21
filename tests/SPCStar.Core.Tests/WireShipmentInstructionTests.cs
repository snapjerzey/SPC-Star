using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class WireShipmentInstructionTests
{
    [Fact]
    public void JobDataInstruction_ImportsExportsAndPersistsWithoutReplacingSavedJobValue()
    {
        var repository=new InMemorySpcRepository();
        const string text="Verify this number matches the wire shipment documentation for the material being used.";
        const string csv="Section,Part Number,Part Description,Product Group,Inspection Item,Requirement Text,Setup Required,In Process Required\nJobData,71355,Drilled needle,Ethicon Cutting Edge - Drilled,Wire Shipment (W/S) #,"+text+",X,X";
        var result=new SetupImportService(repository).ImportCsv(csv);Assert.True(result.Succeeded,string.Join(";",result.Errors));
        var fields=new SetupQueryService(repository).GetSetupSnapshot().PartJobDataFields;Assert.Equal(2,fields.Count);Assert.All(fields,f=>{Assert.True(f.IsRequired);Assert.Equal(text,f.Instruction);});
        Assert.Empty(repository.Measurements);Assert.Empty(repository.JobPhaseCompletions);
        var exported=new SetupTemplateExportService(repository).ExportCsv();
        var reloaded=new InMemorySpcRepository();Assert.True(new SetupImportService(reloaded).ImportCsv(exported).Succeeded);
        Assert.All(new SetupQueryService(reloaded).GetSetupSnapshot().PartJobDataFields,f=>Assert.Equal(text,f.Instruction));
        Assert.True(new SetupImportService(repository).ImportCsv(csv).Succeeded);Assert.Equal(2,repository.PartJobDataFields.Count);
    }
}
