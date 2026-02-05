using Content.Shared.Inventory;
using Robust.Shared.GameStates;

namespace Content.Server._SW.Lathe.MagnetPickup;

/// <summary>
/// Applies an ongoing pickup area around the attached entity.
/// </summary>
[RegisterComponent]
public sealed partial class SWMagnetPickupComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("nextScan")]
    public TimeSpan NextScan = TimeSpan.Zero;

    [DataField]
    public string InputFixtureId = "in_ore";
}
