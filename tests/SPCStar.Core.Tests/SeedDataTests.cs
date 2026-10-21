using SPCStar.Core.Domain;
using SPCStar.Core.Infrastructure;
using SPCStar.Core.Services;
using Xunit;

namespace SPCStar.Core.Tests;

public class SeedDataTests
{
    [Fact]
    public void SeedAll_Expands70306WeldNubToSixReadingsWithoutChangingOtherPlans()
    {
        var repository = new InMemorySpcRepository();
        foreach (var partNum in new[] { "70306", "70309" })
        {
            var part = new Part { PartNum = partNum, Description = "Terminal", ProductGroup = "Schneider" };
            var operation = new Operation { PartId = part.Id, OperationSeq = 10 };
            var characteristic = new Characteristic { OperationId = operation.Id, Name = "Weld Nub Dia. (2X)", Type = CharacteristicType.Variable, UnitOfMeasure = "in" };
            repository.Parts.Add(part);
            repository.Operations.Add(operation);
            repository.Characteristics.Add(characteristic);
            foreach (var phase in new[] { "Setup", "Startup", "In Process" })
                repository.InspectionPlans.Add(new InspectionPlan { CharacteristicId = characteristic.Id, InspectionPhase = phase, SampleSize = 3, Lsl = .057m, Usl = .067m, DisplayOrder = 7, AlertRuleSet = "SpecLimitOnly" });
        }

        SeedData.SeedAll(repository);
        SeedData.SeedAll(repository);

        Assert.Equal(6, repository.InspectionPlans.Count);
        foreach (var plan in repository.InspectionPlans)
        {
            var characteristic = repository.Characteristics.Single(item => item.Id == plan.CharacteristicId);
            var operation = repository.Operations.Single(item => item.Id == characteristic.OperationId);
            var part = repository.Parts.Single(item => item.Id == operation.PartId);
            Assert.Equal(part.PartNum == "70306" ? 6 : 3, plan.SampleSize);
            Assert.Equal(.057m, plan.Lsl);
            Assert.Equal(.067m, plan.Usl);
            Assert.Equal(7, plan.DisplayOrder);
        }
    }

    [Fact]
    public void SeedAll_Splits70310MaterialPartNumbersWithoutDuplicates()
    {
        var repository = new InMemorySpcRepository();
        var part = new Part { PartNum = "70310", Description = "Jaw assembly", ProductGroup = "Schneider" };
        repository.Parts.Add(part);
        repository.PartMaterialFields.Add(new PartMaterialField
        {
            PartId = part.Id,
            InspectionPhase = "In Process",
            MaterialName = "Spring/contact component",
            MaterialPartNum = "61033 / 51510",
            MaterialDescription = "Listed spring/contact component from sheet",
            IsRequired = true,
            DisplayOrder = 3
        });

        SeedData.SeedAll(repository);
        SeedData.SeedAll(repository);

        Assert.Equal(2, repository.PartMaterialFields.Count);
        Assert.Equal(new[] { "61033", "51510" }, repository.PartMaterialFields.OrderBy(field => field.DisplayOrder).Select(field => field.MaterialPartNum));
        Assert.All(repository.PartMaterialFields, field => Assert.True(field.IsRequired));
    }

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
