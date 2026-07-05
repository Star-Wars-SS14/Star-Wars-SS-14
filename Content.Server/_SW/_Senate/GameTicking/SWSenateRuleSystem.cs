using Content.Server._SW._Senate.Core;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using Content.Server.RoundEnd;
using Content.Shared.GameTicking.Components;

namespace Content.Server._SW._Senate.GameTicking;

public sealed class SWSenateRuleSystem : GameRuleSystem<SWSenateRuleComponent>
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;

    protected override void Started(EntityUid uid, SWSenateRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _time.ModeActive = true;
        component.StartDay = _time.Day;
        component.EndTriggered = false;
    }

    protected override void Ended(EntityUid uid, SWSenateRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _time.ModeActive = false;
    }

    protected override void ActiveTick(EntityUid uid, SWSenateRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (component.EndTriggered || _time.DayActive || _time.Day - component.StartDay < component.RoundDays - 1)
            return;

        component.EndTriggered = true;
        _time.ModeActive = false;
        _chatManager.DispatchServerAnnouncement(
            "Рабочая сессия Сената завершена. Созыв распускается - подводятся итоги.",
            Color.FromHex("#9B59B6"));
        _roundEnd.EndRound();
    }
}
