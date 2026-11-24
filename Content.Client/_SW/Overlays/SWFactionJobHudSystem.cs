using Content.Shared._SW.Overlays;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.Overlays;

public sealed class SWFactionJobHudSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFactionMemberComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<SWFactionMemberComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_player.LocalSession?.AttachedEntity is not { } viewer ||
            !TryComp<SWFactionMemberComponent>(viewer, out var viewerFaction) ||
            viewerFaction.Faction != ent.Comp.Faction)
            return;

        // Only the worn ID (or the card inside the worn PDA) identifies the target.
        // Cards in hands, pockets and storage must not override it.
        var idSlots = _inventory.GetSlotEnumerator(ent.Owner, SlotFlags.IDCARD);
        if (!idSlots.NextItem(out var card))
            return;

        if (TryComp<PdaComponent>(card, out var pda))
        {
            if (pda.ContainedId is not { } containedId)
                return;

            card = containedId;
        }

        if (!TryComp<IdCardComponent>(card, out var id) || id.JobDepartments.Count == 0)
            return;

        // Read current equipment and card state so unequipping, faction changes and
        // ID console edits take effect without maintaining a separate HUD cache.
        var equipment = _inventory.GetSlotEnumerator(viewer, SlotFlags.HEAD | SlotFlags.EYES);
        while (equipment.NextItem(out var item, out var slot))
        {
            if (!TryComp<SWFactionJobHudComponent>(item, out var hud) ||
                hud.Faction != viewerFaction.Faction ||
                (hud.SlotFlags & slot.SlotFlags) == 0 ||
                !hud.Departments.IsSupersetOf(id.JobDepartments))
                continue;

            if (_prototype.Resolve(id.JobIcon, out var icon) && !args.StatusIcons.Contains(icon))
                args.StatusIcons.Add(icon);

            // Wearing both a helmet and visor must only add one job icon.
            return;
        }
    }
}
