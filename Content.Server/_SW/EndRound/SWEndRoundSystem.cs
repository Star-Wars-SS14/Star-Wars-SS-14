using Robust.Shared.Timing;
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Server.Chat.Managers;

namespace Content.Server._SW.EndRound;

public sealed class SWEndRoundSystem : EntitySystem
{
    [Dependency] IGameTiming _gameTiming = default!;
    [Dependency] private readonly IEntityManager _e = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<EndRoundByTimerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ticker = _e.System<GameTicker>();
            if (ticker.RoundStartTimeSpan + TimeSpan.FromSeconds(comp.DelayBeforeEndInSeconds) < _gameTiming.CurTime &&
                                                                                                ticker.RunLevel == GameRunLevel.InRound)
            {
                string msg = Loc.GetString(comp.AnnouncementTextEndByTimer);

                _chatManager.DispatchServerAnnouncement(msg, comp.Color);
                _e.System<RoundEndSystem>().EndRound();
            }
        }
    }

    public void DispatchEndByCommandMessage()
    {
        var query = EntityQueryEnumerator<EndRoundByTimerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            string msg = Loc.GetString(comp.AnnouncementTextEndByCommand);
            _chatManager.DispatchServerAnnouncement(msg, comp.Color);
        }
    }

    public void DelayTimer(float time)
    {
        var query = EntityQueryEnumerator<EndRoundByTimerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.DelayBeforeEndInSeconds += time;
        }
    }
}
