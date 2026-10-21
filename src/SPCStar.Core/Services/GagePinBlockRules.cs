using System.Globalization;

namespace SPCStar.Core.Services;

public sealed record GagePinBlockCalibration(decimal BlockSize, decimal FirstPinDiameter, decimal LastPassingPinDiameter);

public static class GagePinBlockRules
{
    private const decimal PinStep = 0.0001m;
    // Supplied gage-block workbook: conversion calibration, not acceptance limits.
    private static readonly decimal[] Blocks = [0.0106m, 0.0115m, 0.0118m, 0.0122m, 0.0126m,
        0.0130m, 0.0134m, 0.0145m, 0.0160m, 0.0165m, 0.0168m, 0.0175m, 0.0185m,
        0.0187m, 0.0189m, 0.0202m, 0.0217m, 0.0225m, 0.0237m, 0.0248m, 0.0260m, 0.0276m, 0.0289m];

    public static GagePinBlockCalibration? TryResolveBlock(
        string? productGroup,
        string? characteristicName,
        string? inspectionMethod,
        string? holeSize)
    {
        if (!IsGagePinBlockHoleDiameter(productGroup, characteristicName, inspectionMethod) ||
            !decimal.TryParse(holeSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var blockSize))
        {
            return null;
        }

        blockSize = blockSize switch { 0.0161m => 0.0160m, 0.0169m => 0.0168m, _ => blockSize };
        if (!Blocks.Contains(blockSize)) return null;
        var first = blockSize == 0.0115m ? 0.0114m : blockSize - 0.0003m;
        return new GagePinBlockCalibration(blockSize, first, first + 5 * PinStep);
    }

    public static decimal? TryMapPinToDiameter(
        string? productGroup,
        string? characteristicName,
        string? inspectionMethod,
        string? holeSize,
        int pinNumber)
    {
        var block = TryResolveBlock(productGroup, characteristicName, inspectionMethod, holeSize);
        if (pinNumber is < 1 or > 7 || block is null)
        {
            return null;
        }

        return block.FirstPinDiameter + ((pinNumber - 1) * PinStep);
    }

    public static bool IsGagePinBlockHoleDiameter(
        string? productGroup,
        string? characteristicName,
        string? inspectionMethod)
    {
        var group = (productGroup ?? "").ToLowerInvariant();
        if (!group.Contains("ethicon") || !group.Contains("drilled"))
        {
            return false;
        }

        var name = Normalize(characteristicName);
        var method = Normalize(inspectionMethod);
        return (name == "hole diameter" || name == "hole dia") &&
            method.Contains("gage pin block");
    }

    private static string Normalize(string? value)
    {
        return string.Join(" ", (value ?? "")
            .ToLowerInvariant()
            .Replace(".", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
