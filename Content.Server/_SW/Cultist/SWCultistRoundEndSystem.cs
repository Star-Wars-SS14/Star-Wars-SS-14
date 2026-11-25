using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking;

namespace Content.Server._SW.Cultist;

public sealed class SWCultistRoundEndSystem : EntitySystem
{
    [Dependency] private readonly SWCultistGameRuleSystem _cultists = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText, before: [typeof(SWTasksGameRuleSystem)]);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        if (_ticker.RunLevel != GameRunLevel.PostRound)
            return;
        var rules = AllEntityQuery<SWCultistGameRuleComponent>();
        while (rules.MoveNext(out var uid, out var rule))
        {
            if (HasComp<GameRuleComponent>(uid))
                _cultists.AppendCultistRoundEndText(rule, args);
        }
    }
}
