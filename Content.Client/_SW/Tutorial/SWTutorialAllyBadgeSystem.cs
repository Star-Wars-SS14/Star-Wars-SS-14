using Content.Shared._SW.Tutorial;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// The badge over tutorial allies. The real one needs a helmet with a faction HUD and a worn ID with departments,
/// and a tutorial faction would need its own of each; the student only has to see that friends have a mark.
/// </summary>
public sealed class SWTutorialAllyBadgeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTutorialAllyComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<SWTutorialAllyComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_proto.TryIndex(ent.Comp.Icon, out var icon) && !args.StatusIcons.Contains(icon))
            args.StatusIcons.Add(icon);
    }
}
