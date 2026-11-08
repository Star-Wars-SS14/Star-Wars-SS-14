using Robust.Shared.GameStates;

namespace Content.Shared._SW.Barricade;

/// <summary>Uses the barricade's actual fixture when checking whether it can close.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWFoldingBarricadeComponent : Component;
