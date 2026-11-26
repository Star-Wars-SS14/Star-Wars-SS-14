using Content.Server._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarAideTest
{
    private static readonly TimeSpan Start = TimeSpan.FromMinutes(30);

    [Test]
    public void ALineIsSaidOnce()
    {
        var queue = new SWGlobalWarAideQueue();
        queue.Offer(SWGlobalWarAideTrigger.Boarded, Start);
        Assert.That(queue.Take(Start, null), Is.EqualTo(SWGlobalWarAideTrigger.Boarded));

        var later = Start + TimeSpan.FromHours(1);
        queue.Offer(SWGlobalWarAideTrigger.Boarded, later);
        Assert.That(queue.Take(later, null), Is.Null);
    }

    [Test]
    public void LinesKeepTheirInterval()
    {
        var queue = new SWGlobalWarAideQueue();
        queue.Offer(SWGlobalWarAideTrigger.Boarded, Start);
        queue.Take(Start, null);

        var soon = Start + TimeSpan.FromMinutes(1);
        queue.Offer(SWGlobalWarAideTrigger.Delivery, soon);
        Assert.That(queue.Take(soon, null), Is.Null);

        // It gave up waiting in the meantime, and something new can be said once the interval is over.
        var after = Start + SWGlobalWarAideQueue.Interval;
        Assert.That(queue.Take(after, null), Is.Null);
        queue.Offer(SWGlobalWarAideTrigger.Delivery, after);
        Assert.That(queue.Take(after, null), Is.EqualTo(SWGlobalWarAideTrigger.Delivery));
    }

    [Test]
    public void ALineWaitsOutAFightAndGivesUp()
    {
        var queue = new SWGlobalWarAideQueue();
        queue.Offer(SWGlobalWarAideTrigger.Boarded, Start);
        Assert.That(queue.Take(Start + TimeSpan.FromSeconds(5), Start), Is.Null);

        // Fighting went on for too long, so it is dropped, not said. The next time counts again.
        var late = Start + SWGlobalWarAideQueue.GiveUpAfter + TimeSpan.FromSeconds(1);
        Assert.That(queue.Take(late, late), Is.Null);
        Assert.That(queue.Take(late, null), Is.Null);

        queue.Offer(SWGlobalWarAideTrigger.Boarded, late);
        Assert.That(queue.Take(late, null), Is.EqualTo(SWGlobalWarAideTrigger.Boarded));
    }

    [Test]
    public void TheWarGoesAheadOfATip()
    {
        var queue = new SWGlobalWarAideQueue();
        queue.Offer(SWGlobalWarAideTrigger.Delivery, Start);
        queue.Offer(SWGlobalWarAideTrigger.War, Start);
        queue.Offer(SWGlobalWarAideTrigger.Boarded, Start);
        Assert.That(queue.Take(Start, null), Is.EqualTo(SWGlobalWarAideTrigger.War));
    }

    [Test]
    public async Task EveryTriggerHasALineForEachSideAndUpstreamTipsAreOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var config = pair.Server.ResolveDependency<IConfigurationManager>();

        Assert.Multiple(() =>
        {
            foreach (var trigger in Enum.GetValues<SWGlobalWarAideTrigger>())
            {
                foreach (var side in new[] { "republic", "empire" })
                {
                    var key = $"sw-global-war-aide-{trigger.ToString().ToLowerInvariant()}-{side}";
                    Assert.That(loc.HasString(key), Is.True, key);
                }
            }

            Assert.That(prototypes.HasIndex<EntityPrototype>("SWGlobalWarAideRepublic"), Is.True);
            Assert.That(prototypes.HasIndex<EntityPrototype>("SWGlobalWarAideEmpire"), Is.True);
            Assert.That(config.GetCVar(CCVars.TipsEnabled), Is.False, "Upstream tips talk about another game.");
        });

        await pair.CleanReturnAsync();
    }
}
