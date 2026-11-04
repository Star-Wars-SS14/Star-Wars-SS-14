using Robust.Shared.Network;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    /// <summary>
    /// Takes the exact carrier of this body out of the registry for an accepted return. The caller either commits by
    /// keeping it out, or undoes it with <see cref="RestoreForceCarrier"/> before anyone else can act.
    /// </summary>
    public bool TryReleaseForceCarrier(NetUserId user, EntityUid body, out SWGlobalWarForceCarrier? carrier)
    {
        carrier = null;
        if (!TryGetRegistry(out var registry) || !registry.Comp.ForceCarriers.TryGetValue(user, out var current) ||
            current.Body != body)
            return false;
        registry.Comp.ForceCarriers.Remove(user);
        carrier = current;
        Changed(registry);
        return true;
    }

    /// <summary>Puts back a carrier whose return failed; a newer carrier of the same player is never replaced.</summary>
    public void RestoreForceCarrier(SWGlobalWarForceCarrier carrier)
    {
        if (TryGetRegistry(out var registry) && registry.Comp.Generation == carrier.Generation &&
            registry.Comp.ForceCarriers.TryAdd(carrier.User, carrier))
            Changed(registry);
    }
}
