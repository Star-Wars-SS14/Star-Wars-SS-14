using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Nuke;
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Shared.Destructible;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Timing;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// This handles...
/// </summary>
public sealed class StarWarsEmpireRuleSystem : GameRuleSystem<StarWarsEmpireRuleComponent>
{
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <inheritdoc/>

    public override void Initialize()
    {
        SubscribeLocalEvent<SWShieldGuardComponent, DestructionEventArgs>(OnDestructuion);
        SubscribeLocalEvent<NukeExplodedEvent>(OnExploded);
    }

    protected override void Started(EntityUid uid, StarWarsEmpireRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        Console.WriteLine("Started");

        component.NextRoundEndCheck = _timing.CurTime + component.EndCheckDelay;
        component.CheckRoundEnd = _timing.CurTime + component.EndRoundDelay;
    }

    protected override void ActiveTick(EntityUid uid, StarWarsEmpireRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.NextRoundEndCheck > _timing.CurTime)
            return;
        CheckRoundEnd(component);
        component.NextRoundEndCheck = _timing.CurTime + component.EndCheckDelay;
        if (!component.CheckRoundEnd.HasValue)
        {
            component.CheckRoundEnd = _timing.CurTime + component.EndRoundDelay;
        }
    }

    private void CheckRoundEnd(StarWarsEmpireRuleComponent сomponent)
    {

        if (_timing.CurTime > сomponent.CheckRoundEnd && !_roundEnd.IsRoundEndRequested())
        {
            foreach (var station in _station.GetStations())
            {
                _chat.DispatchStationAnnouncement(station, Loc.GetString("Привет!"), colorOverride: Color.Crimson);
            }
            _roundEnd.EndRound();
        }
    }

    private void OnDestructuion(EntityUid uid, SWShieldGuardComponent component, DestructionEventArgs args)
    {
        if (EntityManager.HasComponent<SWShieldGuardComponent>(uid))
        {
            _roundEnd.EndRound();
        }
    }

    protected override void AppendRoundEndText(EntityUid uid, StarWarsEmpireRuleComponent component, GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        args.AddLine(Loc.GetString(" "));
    }

    private void OnExploded(NukeExplodedEvent ev)
    {
            _roundEnd.EndRound();
    }
}
