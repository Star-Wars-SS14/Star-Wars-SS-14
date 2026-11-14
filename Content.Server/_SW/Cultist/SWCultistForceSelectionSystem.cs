using Content.Server.Antag;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Cultist;

/// <summary>Marks the cultist selection template; it does not replace tags on a selected body.</summary>
[RegisterComponent]
public sealed partial class SWCultistForceSelectionComponent : Component;

public sealed class SWCultistForceSelectionSystem : EntitySystem
{
    [Dependency] private readonly TagSystem _tags = default!;
    private static readonly ProtoId<TagPrototype> ForceUserTag = "ForceUser";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCultistForceSelectionComponent, AfterAntagEntitySelectedEvent>(OnSelected);
    }

    private void OnSelected(Entity<SWCultistForceSelectionComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        // Adding only this tag preserves all pre-existing body and equipment tags.
        _tags.AddTag(args.EntityUid, ForceUserTag);
    }
}
