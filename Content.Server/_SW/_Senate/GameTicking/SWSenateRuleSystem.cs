using Content.Server._SW._Senate.Core;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._SW._Senate.GameTicking;

public sealed class SWSenateRuleSystem : GameRuleSystem<SWSenateRuleComponent>
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    protected override void Started(EntityUid uid, SWSenateRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _time.ModeActive = true;
    }

    protected override void Ended(EntityUid uid, SWSenateRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _time.ModeActive = false;
    }
}
