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
    public Color ValidHumanSkinTone = Color.FromHsv(new Vector4(0.07f, 0.2f, 1f, 1f));

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
        return ValidHumanSkinTone;
    }

    public Color FromUnary(float color)
    {
        color /= 100; //normalize it to be in [0, 1], 100 - max human tone
        var new_tone = (int)float.Lerp(MinTone, MaxTone, color);

        var tmp = new HumanTonedSkinColoration();
        return tmp.FromUnary(color);
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
public sealed partial class ColorLimitsSkinColoration : ISkinColorationStrategy
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
        return GenerateValidColorLimits();
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
