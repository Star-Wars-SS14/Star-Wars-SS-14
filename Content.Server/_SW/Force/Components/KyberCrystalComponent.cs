using static Robust.Shared.Maths.Color;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class KyberCrystalComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;

    public Dictionary<string, Color> Colors = new ()
    {
        {"White", White},
        {"Black", Black},
        {"White-blue", LightBlue},
        {"Green", Green},
        {"Indigo", Indigo},
        {"Orange", Orange},
        {"Pink", Pink},
        {"Purple", Purple},
        {"Red", Red},
        {"Red-orange", OrangeRed},
        {"Yellow", Yellow},
        {"Yellow-green", YellowGreen}
    };

    [DataField, AutoNetworkedField]
    public string HexColor = "White";

    public string CallSaberAction = "CallSaberAction";

    public string KyberActionPrototype = "ActionSubdueKyber";

    public string SaberFragmentTag = "SaberFragmentTag";

    public string SaberPrototype = "Lightsaber";

    [DataField, AutoNetworkedField]
    public EntityUid? KyberActionEntity;
}
