using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public class SeedDataTests
{
    [Fact]
    public void SeedAll_CleansGeneratedOneSidedSpecSentinels()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "70305", Description = "Schneider Jaw Assy", ProductGroup = "Schneider" };
        var process = new ManufacturingProcess { ProcessCode = "Schneider Production", Description = "Schneider Production" };
        var operation = new Operation { PartId = part.Id, ProcessId = process.Id, OperationSeq = 10 };
        var characteristic = new Characteristic
        {
            OperationId = operation.Id,
            Name = "Brazed Contact To Jaw Shear Force",
            Type = CharacteristicType.Variable,
            UnitOfMeasure = "lbs"
        };

        repository.Parts.Add(part);
        repository.Processes.Add(process);
        repository.Operations.Add(operation);
        repository.Characteristics.Add(characteristic);
        repository.InspectionPlans.Add(new InspectionPlan
        {
            CharacteristicId = characteristic.Id,
            InspectionPhase = "In Process",
            SampleSize = 3,
            DisplayOrder = 1,
            AlertRuleSet = "SpecLimitOnly",
            Frequency = new InspectionFrequency { Type = FrequencyType.Quantity, Value = 5000, Unit = FrequencyUnit.Pieces }
        });
        repository.SpecLimits.Add(new SpecLimit { CharacteristicId = characteristic.Id, Nominal = 5139.5m, Lsl = 280m, Usl = 9999m });
        repository.ControlLimits.Add(new ControlLimitSet
        {
            PartNum = part.PartNum,
            ProcessCode = process.ProcessCode,
            OperationSeq = operation.OperationSeq,
            CharacteristicName = characteristic.Name,
            CenterLine = 5139.5m,
            Lcl = 280m,
            Ucl = 9999m
        });

        SeedData.SeedAll(repository);

        var plan = Assert.Single(repository.InspectionPlans);
        Assert.Null(plan.Nominal);
        Assert.Equal(280m, plan.Lsl);
        Assert.Null(plan.Usl);
        Assert.Empty(repository.SpecLimits);
        Assert.Empty(repository.ControlLimits);
    }
}
