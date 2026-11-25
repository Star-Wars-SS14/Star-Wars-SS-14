using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Content.Shared.GameTicking.Components;

namespace Content.Server._SW.Cultist;

public sealed class SWCultistRoundEndSystem : EntitySystem
{
    [Dependency] private readonly SWCultistGameRuleSystem _cultists = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText, before: [typeof(SWTasksGameRuleSystem)]);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        var rules = AllEntityQuery<SWCultistGameRuleComponent>();
        while (rules.MoveNext(out var uid, out var rule))
        {
            if (HasComp<GameRuleComponent>(uid))
                _cultists.AppendCultistRoundEndText(rule, args);
        }
    }
}
