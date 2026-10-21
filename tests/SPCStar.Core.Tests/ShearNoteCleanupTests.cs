using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public sealed class ShearNoteCleanupTests
{
    private const string Header="Section,Part Number,Part Description,Product Group,Operation,Inspection Item,Attribute/Variable,Lower Spec,Unit,Startup Required,Startup Sample Size,Setup Required,Setup Sample Size,In Process Required,In Process Sample Size,In Process Frequency Type,In Process Frequency Qty,In Process Frequency Unit,Coil Change Required,Coil Change Sample Size\n";
    private static string Row(string part,string name,bool change,bool regular=true)=>
        $"Inspection,{part},Blade,Schneider,Schneider Production,{name},Variable,90,lbs,{(regular?"X":"")},{(regular?"1":"")},X,1,{(regular?"X":"")},{(regular?"3":"")},Quantity,2000,Pieces,{(change?"X":"")},{(change?"1":"")}\n";
    private static string Regular=>Row("70122","Shear Force - A",false)+Row("70122","Shear Force - B",false)+Row("70123","Shear Force",false);
    private static void Import(InMemorySpcRepository r,string csv) { var result=new SetupImportService(r).ImportCsv(csv);Assert.True(result.Succeeded,string.Join("; ",result.Errors)); }

    [Fact]
    public void CorrectedImport_RemovesOnlyExtraChangeAssignments_AndPreservesOldHistory()
    {
        var r=new InMemorySpcRepository();
        Import(r,Header+Regular+Row("70122","Contact Tape Shear - A",true,false)+Row("70122","Contact Tape Shear - B",true,false));
        Import(r,Header+Row("70123","Shear Force",true));
        var oldIds=r.Characteristics.ToDictionary(c=>c.Id,c=>c.Name);
        var history=new InspectionMeasurement {JobNum="Existing",PartNum="70122",ProcessCode="Schneider Production",OperationSeq=10,
            CharacteristicName="Contact Tape Shear - A",Value=95m,OperatorUserId="qa1",ResourceId="R"};r.Measurements.Add(history);
        Import(r,Header+Regular);var ids=r.InspectionPlans.Select(p=>p.Id).OrderBy(x=>x).ToArray();Import(r,Header+Regular);
        Assert.Equal(ids,r.InspectionPlans.Select(p=>p.Id).OrderBy(x=>x));
        Assert.All(oldIds,x=>Assert.Equal(x.Value,r.Characteristics.Single(c=>c.Id==x.Key).Name));
        var plans=new SetupQueryService(r).GetInspectionPlans();
        Assert.Equal(9,plans.Count);Assert.DoesNotContain(plans,p=>p.CharacteristicName.StartsWith("Contact Tape Shear"));
        Assert.DoesNotContain(plans,p=>p.PartNum=="70123"&&p.InspectionPhase=="Coil Change");
        Assert.All(plans,p=>{Assert.Equal(90m,p.Lsl);Assert.Null(p.Usl);Assert.Equal(p.InspectionPhase=="In Process"?3:1,p.SampleSize);});
        Assert.Same(history,Assert.Single(r.Measurements));Assert.Equal(95m,history.Value);Assert.Empty(r.JobPhaseCompletions);
    }

    [Fact]
    public void PartialImport_DoesNotRetireSeparateChecks()
    {
        var r=new InMemorySpcRepository();Import(r,Header+Row("70122","Contact Tape Shear - A",true,false));
        Import(r,Header+Row("70122","Shear Force - A",false));
        Assert.Contains(new SetupQueryService(r).GetInspectionPlans(),p=>p.CharacteristicName=="Contact Tape Shear - A");
    }

    [Theory]
    [InlineData("70122","Contact Tape Shear - A","Setup")]
    [InlineData("70122","Contact Tape Shear - B","Coil Change")]
    [InlineData("70123","Shear Force","Coil Change")]
    public void StaleChangeEntry_IsRejectedAfterCleanup(string part,string name,string phase)
    {
        var r=new InMemorySpcRepository();SeedData.SeedSecurity(r);TestSeedData.SeedUsers(r);
        Import(r,Header+Regular+Row("70122","Contact Tape Shear - A",true,false)+Row("70122","Contact Tape Shear - B",true,false));
        Import(r,Header+Row("70123","Shear Force",true));Import(r,Header+Regular);
        var result=new InspectionMeasurementService(r,new WesternElectricRuleService()).EnterMeasurement(
            new("ShearTest",part,"Schneider Production",10,"R",name,95m,DateTimeOffset.UtcNow,"qa1",InspectionPhase:phase));
        Assert.False(result.Succeeded);Assert.Empty(r.Measurements);Assert.Empty(r.Alerts);
    }
}
