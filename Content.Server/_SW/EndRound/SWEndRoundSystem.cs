using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.RoundEnd;
using Content.Server.Voting.Managers;
using Content.Shared._SW.TaskSystem;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using System.Linq;

namespace Content.Server._SW.EndRound;

public sealed class SWEndRoundGameruleSystem : GameRuleSystem<SWEndRoundGameruleComponent>
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly IVoteManager _voteManager = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void Started(EntityUid uid, SWEndRoundGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.NextTimeStartVote = component.DelayBeforeVote;
    }

    protected override void ActiveTick(EntityUid uid, SWEndRoundGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (_gameTicker.RoundDuration() > component.DelayBeforeFirstCheck && _gameTicker.RunLevel == GameRunLevel.InRound)
        {
            var query = EntityQueryEnumerator<SWFactionMemberComponent>();

            var anyAlive = component.Factions.ToDictionary(key => key, key => false);

            while (query.MoveNext(out var mobUid, out var factionComp))
            {
                if (!anyAlive.ContainsKey(factionComp.Faction))
                {
                    continue;
                }
                anyAlive[factionComp.Faction] = anyAlive[factionComp.Faction] || IsAlive(mobUid);
            }

            if (anyAlive.Values.ToList().Contains(false))
            {
                string msg = Loc.GetString(component.AnnouncementTextEndByTimer);
                _chatManager.DispatchServerAnnouncement(msg, component.Color);
                _roundEndSystem.EndRound();
            }
        }

        if (_gameTicker.RoundDuration() > component.NextTimeStartVote)
        {
            _voteManager.CreateStandardVote(null, Shared.Voting.StandardVoteType.Restart);
            component.NextTimeStartVote = component.NextTimeStartVote + component.DelayBetweenVotes;
        }
    }


    private bool IsAlive(EntityUid entity)
    {
        if (!TryComp<MobStateComponent>(entity, out var comp))
        {
            return false;
        }

        return comp.CurrentState == Shared.Mobs.MobState.Alive;
    }


    public void DispatchEndByCommandMessage()
    {
        var query = EntityQueryEnumerator<SWEndRoundGameruleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            string msg = Loc.GetString(comp.AnnouncementTextEndByCommand);
            _chatManager.DispatchServerAnnouncement(msg, comp.Color);
        }
    }

    //public void DelayTimer(float time)
    //{
    //    var query = EntityQueryEnumerator<SWEndRoundGameruleComponent>();
    //    while (query.MoveNext(out var uid, out var comp))
    //    {
    //        comp.DelayBeforeFirstCheck += time;
    //    }
    //}
}
