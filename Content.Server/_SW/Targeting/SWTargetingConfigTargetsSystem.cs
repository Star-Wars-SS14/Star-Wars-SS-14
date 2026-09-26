using System.Linq;
using Content.Server._SW.TurretConsole;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;

namespace Content.Server._SW.Targeting;

public sealed class SWTargetingConfigTargetsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWTargetingConfigTargetsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWTargetingConfigTargetsComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<SWTargetingConfigTargetsComponent, ComponentShutdown>(OnProviderShutdown);
        SubscribeLocalEvent<DeviceLinkSourceComponent, PortDisconnectedEvent>(OnTargetPortDisconnected);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWTargetingConsoleSettingsChangedEvent>(OnSettingsChanged);
    }

    public void RegisterTarget(EntityUid provider, EntityUid target)
    {
        if (!TryComp<SWTurretConsoleComponent>(provider, out var console))
            return;

        var targets = EnsureComp<SWTargetingConfigTargetsComponent>(provider);
        targets.Targets.Add(target);
        ApplyConfigToTarget(provider, target, console, targets);
    }

    public void UnregisterTarget(EntityUid provider, EntityUid target)
    {
        if (!TryComp<SWTargetingConfigTargetsComponent>(provider, out var targets))
            return;

        targets.Targets.Remove(target);
        RefreshTargetConfig(target);
    }

    public void UnregisterTargetFromAll(EntityUid target)
    {
        var query = EntityQueryEnumerator<SWTargetingConfigTargetsComponent>();
        while (query.MoveNext(out var uid, out var targets))
        {
            targets.Targets.Remove(target);
        }

        DisableTargetConfig(target);
    }

    public void RefreshProvider(EntityUid provider)
    {
        if (!TryComp<SWTargetingConfigTargetsComponent>(provider, out var targets))
            return;

        if (!TryComp<SWTurretConsoleComponent>(provider, out var console))
            return;

        if (IsProviderEnabled(provider))
            ApplyConfigToTargets(provider, console, targets);
        else
            DisableConfigForTargets(targets);
    }

    private void OnMapInit(Entity<SWTargetingConfigTargetsComponent> ent, ref MapInitEvent args)
    {
        SyncPortTargets(ent);
    }

    private void OnNewLink(Entity<SWTargetingConfigTargetsComponent> ent, ref NewLinkEvent args)
    {
        if (args.Sink != ent.Owner)
            return;

        RegisterTarget(ent.Owner, args.Source);
    }

    private void OnProviderShutdown(Entity<SWTargetingConfigTargetsComponent> ent, ref ComponentShutdown args)
    {
        foreach (var target in ent.Comp.Targets.ToArray())
        {
            ent.Comp.Targets.Remove(target);
            RefreshTargetConfig(target);
        }

        ent.Comp.Targets.Clear();
    }

    private void OnTargetPortDisconnected(Entity<DeviceLinkSourceComponent> ent, ref PortDisconnectedEvent args)
    {
        UnregisterPortTarget(ent.Owner);
    }

    private void OnSettingsChanged(Entity<SWTurretConsoleComponent> ent, ref SWTargetingConsoleSettingsChangedEvent args)
    {
        if (!TryComp<SWTargetingConfigTargetsComponent>(ent.Owner, out var targets))
            return;

        ApplyConfigToTargets(ent.Owner, ent.Comp, targets);
    }

    private void SyncPortTargets(Entity<SWTargetingConfigTargetsComponent> ent)
    {
        if (!TryComp<SWTurretConsoleComponent>(ent.Owner, out var console) ||
            !TryComp<DeviceLinkSinkComponent>(ent.Owner, out var sink))
        {
            return;
        }

        foreach (var oldTarget in ent.Comp.Targets.ToArray())
        {
            if (HasLinkedSource(sink, oldTarget))
                continue;

            ent.Comp.Targets.Remove(oldTarget);
            DisableTargetConfig(oldTarget);
        }

        foreach (var source in sink.LinkedSources)
        {
            ent.Comp.Targets.Add(source);
            ApplyConfigToTarget(ent.Owner, source, console, ent.Comp);
        }
    }

    private bool HasLinkedSource(DeviceLinkSinkComponent sink, EntityUid source)
    {
        foreach (var linkedSource in sink.LinkedSources)
        {
            if (linkedSource == source)
                return true;
        }

        return false;
    }

    private void UnregisterPortTarget(EntityUid target)
    {
        var query = EntityQueryEnumerator<SWTargetingConfigTargetsComponent, DeviceLinkSinkComponent>();
        while (query.MoveNext(out _, out var targets, out _))
        {
            targets.Targets.Remove(target);
        }

        RefreshTargetConfig(target);
    }

    private void ApplyConfigToTargets(EntityUid provider, SWTurretConsoleComponent console, SWTargetingConfigTargetsComponent targets)
    {
        foreach (var target in targets.Targets.ToArray())
        {
            if (TerminatingOrDeleted(target))
            {
                targets.Targets.Remove(target);
                continue;
            }

            ApplyConfigToTarget(provider, target, console, targets);
        }
    }

    private void ApplyConfigToTarget(EntityUid provider, EntityUid target, SWTurretConsoleComponent console, SWTargetingConfigTargetsComponent targets)
    {
        if (!IsProviderEnabled(provider))
        {
            RefreshTargetConfig(target);
            return;
        }

        var config = EnsureComp<SWTargetingConfigComponent>(target);
        config.AllowedJobs = new Dictionary<string, bool>(console.JobsAllowed);
        config.TargetUnconfiguredJobs = targets.TargetUnconfiguredJobs;
        config.TargetHumanoidsWithoutId = targets.TargetHumanoidsWithoutId;
        config.TargetAnimals = targets.TargetAnimals;
    }

    private void RefreshTargetConfig(EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        var query = EntityQueryEnumerator<SWTurretConsoleComponent, SWTargetingConfigTargetsComponent>();
        while (query.MoveNext(out var provider, out var console, out var targets))
        {
            if (!IsProviderEnabled(provider) ||
                !targets.Targets.Contains(target))
                continue;

            ApplyConfigToTarget(provider, target, console, targets);
            return;
        }

        DisableTargetConfig(target);
    }

    private void DisableTargetConfig(EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        RemComp<SWTargetingConfigComponent>(target);
    }

    private void DisableConfigForTargets(SWTargetingConfigTargetsComponent targets)
    {
        foreach (var target in targets.Targets)
        {
            RefreshTargetConfig(target);
        }
    }

    private bool IsProviderEnabled(EntityUid provider)
    {
        var ev = new SWTargetingProviderAvailableEvent(null, false);
        RaiseLocalEvent(provider, ev);
        return !ev.Cancelled;
    }
}
