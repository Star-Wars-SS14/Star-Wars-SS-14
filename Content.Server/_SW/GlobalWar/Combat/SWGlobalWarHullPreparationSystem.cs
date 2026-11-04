using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>
/// Converts historical vessel-map debug equipment once, before a purchased hull becomes accessible.
/// No repair, capture, reload or reconnect path calls this to refill an existing vessel.
/// </summary>
public sealed class SWGlobalWarHullPreparationSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;

    public bool Prepare(EntityUid ship)
    {
        if (HasComp<SWGlobalWarPreparedHullComponent>(ship)) return true;
        var replacements = new List<(EntityUid Entity, string Prototype)>();
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var metadata, out var transform))
        {
            if (transform.GridUid != ship || metadata.EntityPrototype is not { } prototype) continue;
            var replacement = prototype.ID switch
            {
                "DebugGenerator" or "GeneratorBasic" or "GeneratorBasic15kW" or
                    "GeneratorWallmountBasic" or "GeneratorWallmountAPU" => "SWGlobalWarShipGenerator",
                "DebugGyroscope" => "Gyroscope",
                "SWShuttleGunPerforator" => "SWShuttleGunPerforator",
                "SWShuttleGunDuster" => "SWShuttleGunDuster",
                _ => null,
            };
            if (replacement != null) replacements.Add((uid, replacement));
        }
        foreach (var (old, prototype) in replacements)
        {
            var coordinates = Transform(old).Coordinates;
            var rotation = Transform(old).LocalRotation;
            // Removing the obsolete gun also removes its old gun_magazine and self-recharging cage.
            Del(old);
            var replacement = Spawn((EntProtoId) prototype, coordinates);
            _transform.SetLocalRotation(replacement, rotation);
            if (prototype != "SWShuttleGunDuster") continue;
            var magazine = Spawn("MagazineGrenadeFrag", coordinates);
            if (_slots.TryInsert(replacement, "gun_magazine", magazine, null)) continue;
            Del(magazine);
            return false;
        }
        // Spare legacy cages must not provide a second infinite ammunition source.
        var cages = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (cages.MoveNext(out var uid, out var metadata, out var transform))
        {
            if (transform.GridUid != ship ||
                metadata.EntityPrototype?.ID.StartsWith("PowerCage", StringComparison.Ordinal) != true) continue;
            RemComp<BatterySelfRechargerComponent>(uid);
            RemComp<PredictedBatterySelfRechargerComponent>(uid);
        }
        EnsureComp<SWGlobalWarPreparedHullComponent>(ship);
        return true;
    }
}

/// <summary>Once-only finite starting equipment has been prepared on this physical grid.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarPreparedHullComponent : Component;
