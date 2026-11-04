using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarCoachTest
{
    private static SWGlobalWarCoachFacts Facts(SWGlobalWarRole role) =>
        new(role, AtPeace: true, PeaceTimeOver: false, AtHelm: false, Routed: false, HasKit: false, ArsenalReady: true, CanLeave: true);

    [Test]
    public void HeadWaitsThenDeclaresThenHasNoLine()
    {
        var facts = Facts(SWGlobalWarRole.Head);
        Assert.That(SWGlobalWarCoachRules.Pick(facts), Is.EqualTo(SWGlobalWarCoachHint.HeadWait));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { PeaceTimeOver = true }), Is.EqualTo(SWGlobalWarCoachHint.HeadDeclare));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { PeaceTimeOver = true, AtPeace = false }), Is.EqualTo(SWGlobalWarCoachHint.None));
    }

    [TestCase(SWGlobalWarRole.Soldier)]
    [TestCase(SWGlobalWarRole.JuniorOfficer)]
    [TestCase(SWGlobalWarRole.SeniorOfficer)]
    public void FightersGetTheirKitThenLeaveThenWait(SWGlobalWarRole role)
    {
        var facts = Facts(role);
        Assert.That(SWGlobalWarCoachRules.Pick(facts), Is.EqualTo(SWGlobalWarCoachHint.Kit));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { HasKit = true }), Is.EqualTo(SWGlobalWarCoachHint.Teleporter));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { HasKit = true, CanLeave = false }), Is.EqualTo(SWGlobalWarCoachHint.Countdown));

        // A step that cannot be done is skipped, so the line does not ask for it.
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { ArsenalReady = false }), Is.EqualTo(SWGlobalWarCoachHint.Teleporter));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { ArsenalReady = false, CanLeave = false }), Is.EqualTo(SWGlobalWarCoachHint.Countdown));

        // The lines are for the quiet first minutes only.
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { PeaceTimeOver = true }), Is.EqualTo(SWGlobalWarCoachHint.None));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { AtPeace = false }), Is.EqualTo(SWGlobalWarCoachHint.None));
    }

    [Test]
    public void TheRouteLineShowsUntilTheFirstRoute()
    {
        var facts = Facts(SWGlobalWarRole.Pilot);
        Assert.That(SWGlobalWarCoachRules.Pick(facts), Is.EqualTo(SWGlobalWarCoachHint.None));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { AtHelm = true }), Is.EqualTo(SWGlobalWarCoachHint.Route));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { AtHelm = true, AtPeace = false }), Is.EqualTo(SWGlobalWarCoachHint.None));
        Assert.That(SWGlobalWarCoachRules.Pick(facts with { AtHelm = true, Routed = true }), Is.EqualTo(SWGlobalWarCoachHint.None));
    }

    [TestCase(SWGlobalWarCoachHint.Kit, 0, 0, true)]
    [TestCase(SWGlobalWarCoachHint.Kit, 59, 0, true)]
    [TestCase(SWGlobalWarCoachHint.Kit, 61, 0, false)]
    [TestCase(SWGlobalWarCoachHint.Kit, 302, 0, true)]
    [TestCase(SWGlobalWarCoachHint.Kit, 310, 0, false)]
    [TestCase(SWGlobalWarCoachHint.Countdown, 5, 900, true)]
    [TestCase(SWGlobalWarCoachHint.Countdown, 12, 890, false)]
    [TestCase(SWGlobalWarCoachHint.Countdown, 300, 602, true)]
    [TestCase(SWGlobalWarCoachHint.Countdown, 900, 59, true)]
    public void LinesComeAndGo(SWGlobalWarCoachHint hint, double since, double remaining, bool shown)
    {
        Assert.That(SWGlobalWarCoachRules.IsShown(hint, since, remaining), Is.EqualTo(shown));
    }

    [TestCase(SWGlobalWarRole.Civilian)]
    [TestCase(SWGlobalWarRole.WarMinister)]
    [TestCase(SWGlobalWarRole.Intelligence)]
    [TestCase(SWGlobalWarRole.ForceUser)]
    public void OtherRolesHaveNoLineYet(SWGlobalWarRole role)
    {
        Assert.That(SWGlobalWarCoachRules.Pick(Facts(role)), Is.EqualTo(SWGlobalWarCoachHint.None));
    }

    [Test]
    public async Task EveryLineHasText()
    {
        await using var pair = await PoolManager.GetServerClient();
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();

        foreach (var hint in Enum.GetValues<SWGlobalWarCoachHint>())
        {
            if (SWGlobalWarCoachRules.LocKey(hint) is { } key)
                Assert.That(loc.HasString(key), Is.True, key);
        }

        await pair.CleanReturnAsync();
    }
}
