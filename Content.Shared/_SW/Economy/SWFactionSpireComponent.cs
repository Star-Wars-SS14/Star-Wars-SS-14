using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Economy;

/// <summary>
/// Faction control spire state. The server owns the values and dirties them on
/// transitions; clients render sprite layers and glow from the networked fields.
/// raiseAfterAutoHandleState is required for AfterAutoHandleStateEvent subscriptions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class SWFactionSpireComponent : Component
{
    [DataField, AutoNetworkedField]
    public string OwnerFaction = string.Empty;

    [DataField, AutoNetworkedField]
    public string CapturingFaction = string.Empty;

    [DataField, AutoNetworkedField]
    public bool IsCapturing;

    [DataField, AutoNetworkedField]
    public float CaptureProgress;

    [DataField, AutoNetworkedField]
    public float PayoutProgress;

    [DataField, AutoNetworkedField]
    public bool BeamActive;

    [DataField]
    public float CaptureTime = 180f;

    [DataField]
    public float PayoutInterval = 120f;

    [DataField]
    public float PayoutAmount = 150f;

    [DataField]
    public ProtoId<SWCurrencyPrototype> PayoutCurrency = "SWEmpireCreditCurrency";

    [DataField]
    public float BeamDuration = 0.56f;

    /// <summary>
    /// Optional per-placement display name applied on map init, so a single
    /// prototype can serve many uniquely named spires. Empty = prototype name.
    /// The radio intercept warning reports exactly this entity name.
    /// </summary>
    [DataField]
    public string? SpireName;

    // Server-only runtime state (not networked).
    public EntityUid? RewritingStream;
    public float BeamTimer;

    /// <summary>
    /// Previous card-presence snapshot used to push BUI state exactly when the
    /// card is inserted or ejected (container changes raise no spire event).
    /// </summary>
    public bool HadCard;

    /// <summary>
    /// Accumulator for periodic BUI pushes so progress bars/timers move smoothly
    /// between discrete transitions.
    /// </summary>
    public float PushTimer;
}