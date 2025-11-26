using Content.Shared._SW.Species;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System.Linq;
using System.Numerics;
using YamlDotNet.Core.Tokens;


[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class SWHumanTonedReducedSkinColoration : ISkinColorationStrategy
{
    [DataField]
    public Color ValidSkinColor = Color.FromHsv(new Vector4(0.07f, 0.2f, 1f, 1f));

    [DataField]
    public float MinTone = 0f;

    [DataField]
    public float MaxTone = 25f;
    public SkinColorationStrategyInput InputType => SkinColorationStrategyInput.Unary;

    public bool VerifySkinColor(Color color)
    {
        var tmp = new HumanTonedSkinColoration();
        var tone = tmp.ToUnary(color);
        return MinTone <= tone && tone <= MaxTone;
    }

    public Color ClosestSkinColor(Color color)
    {
        float unary = ToUnary(color);

        return FromUnary(unary);
    }

    public Color FromUnary(float color)
    {
        color /= 100; //normalize it to be in [0, 1], 100 - max human tone
        var new_tone = (int)float.Lerp(MinTone, MaxTone, color);

        var tmp = new HumanTonedSkinColoration();
        return tmp.FromUnary(new_tone);
    }

    public float ToUnary(Color color)
    {
        var tmp = new HumanTonedSkinColoration();
        var c = tmp.ToUnary(color) / (MaxTone - MinTone);
        var new_tone = float.Lerp(0, 100, c);
        return new_tone;
    }
}


[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class SWColorLimitsSkinColoration : ISkinColorationStrategy
{
    [DataField]
    public SWNumberIntervalSet HueLimit = new SWNumberIntervalSet(0, 1);

    [DataField]
    public SWNumberIntervalSet SaturationLimit = new SWNumberIntervalSet(0, 1);

    [DataField]
    public SWNumberIntervalSet ValueLimit = new SWNumberIntervalSet(0, 1);

    public SkinColorationStrategyInput InputType => SkinColorationStrategyInput.Color;

    public bool VerifySkinColor(Color color)
    {
        return FitLimits(color);
    }

    public Color ClosestSkinColor(Color color)
    {
        var hsv = Color.ToHsv(color);
        var h = hsv.X;
        var s = hsv.Y;
        var v = hsv.Z;

        var hLimits = HueLimit;
        var sLimits = SaturationLimit;
        var vLimits = ValueLimit;

        float closestH = ClosestValueInIntervalSet(h, hLimits);
        float closestS = ClosestValueInIntervalSet(s, sLimits);
        float closestV = ClosestValueInIntervalSet(v, vLimits);

        return Color.FromHsv(new Vector4(closestH, closestS, closestV, 1.0f));
    }

    private float ClosestValueInIntervalSet(float value, SWNumberIntervalSet intervalSet)
    {
        if (intervalSet == null || intervalSet.Intervals.Count == 0)
            return value;

        float closestValue = value;
        float minDistance = float.MaxValue;

        foreach (var interval in intervalSet.Intervals)
        {
            float clampedValue = Math.Clamp(value, interval.Left, interval.Right);
            float distance = Math.Abs(value - clampedValue);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestValue = clampedValue;
            }
        }

        return closestValue;
    }

    public Color GenerateValidColorLimits()
    {

        var h = HueLimit.Intervals.First().Left;
        var s = SaturationLimit.Intervals.First().Left;
        var v = ValueLimit.Intervals.First().Left;

        return Color.FromHsv(new Vector4(h, s, v, 1.0f));
    }

    public bool FitLimits(Color color)
    {
        var hsv = Color.ToHsv(color);
        bool hFit = HueLimit.Contains(hsv.X);
        bool sFit = SaturationLimit.Contains(hsv.Y);
        bool vFit = ValueLimit.Contains(hsv.Z);

        return hFit && sFit && vFit;
    }
}
