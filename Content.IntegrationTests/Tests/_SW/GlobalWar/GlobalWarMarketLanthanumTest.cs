using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarMarketLanthanumTest
{
    private const string Ln = "SWGlobalWarLn";
    private const string E = "SWGlobalWarE";
    private const SWGlobalWarFaction R = SWGlobalWarFaction.Republic;
    private const SWGlobalWarFaction Empire = SWGlobalWarFaction.Empire;
    private static int Q(decimal value) => SWGlobalWarMarketUnits.FromUnits(value);

    private static SWGlobalWarMarketSimulation Model(decimal industryStock = 0)
    {
        SWGlobalWarMarketDefinition World(string id, SWGlobalWarSectorKind kind, decimal electronics = 0,
            decimal stock = 0, bool enclave = false, bool reserve = false, SWGlobalWarFaction owner = R)
        {
            var profile = new SWGlobalWarEconomyProfilePrototype { HubCapacity = 60 };
            if (enclave)
                profile.Recipes.Add(new() { Output = Ln, PerMinute = 0 });
            return new(id, [new(Ln, Q(stock), 0), new(E, 0, Q(electronics))], owner, profile, kind, enclave, reserve);
        }
        var model = new SWGlobalWarMarketSimulation([new(Ln, 1), new(E, 1)],
        [
            World("home", SWGlobalWarSectorKind.Capital, 1.8m),
            World("industry1", SWGlobalWarSectorKind.Industry, 0.1m, industryStock),
            World("industry2", SWGlobalWarSectorKind.Industry, 0.1m, industryStock),
            World("enclave", SWGlobalWarSectorKind.Mining, 0.2m, enclave: true),
            World("short", SWGlobalWarSectorKind.Mining), World("bypass", SWGlobalWarSectorKind.Mining),
            World("reserve", SWGlobalWarSectorKind.Mining, reserve: true),
            World("enemyReserve", SWGlobalWarSectorKind.Mining, reserve: true, owner: Empire),
        ],
        [
            new("enclave", "short", TimeSpan.FromSeconds(1), 64),
            new("short", "industry1", TimeSpan.FromSeconds(1), 64),
            new("enclave", "bypass", TimeSpan.FromSeconds(10), 64),
            new("bypass", "industry1", TimeSpan.FromSeconds(10), 64),
            new("industry1", "industry2", TimeSpan.FromSeconds(1), 64),
            new("reserve", "industry1", TimeSpan.FromSeconds(1), 64),
            new("enemyReserve", "industry1", TimeSpan.FromSeconds(1), 64),
        ], Guid.NewGuid()) { DemographyEnabled = false };
        model.ConfigureFlows([]);
        model.TradePolicy = _ => new(true, string.Empty, 0, 0, 0, 0, 0);
        return model;
    }

    private static void Capture(SWGlobalWarMarketSimulation model, string planet, SWGlobalWarFaction owner)
    {
        model.CommitOwnershipBatch(model.Generation!.Value, [new(planet, owner, Guid.NewGuid())]);
        model.RefreshLanthanumOwnership();
    }

    [TestCase(0.5, 9.875, 197.5, 0.5)]
    [TestCase(1, 12.875, 257.5, 0.5)]
    [TestCase(1.5, 15.875, 317.5, 0.25)]
    public void NormativeDemandCountsCivilAndApprovedRecipeChainsOnce(double population, double demand,
        double threshold, double t2)
    {
        var model = Model();
        foreach (var market in model.Markets.Values)
            market.Population = (decimal) population;
        Capture(model, "enclave", Empire);
        var doctrine = model.LanthanumDoctrines[R];
        Assert.That(doctrine.Demand, Is.EqualTo((decimal) demand));
        Assert.That(20 * doctrine.Demand, Is.EqualTo((decimal) threshold));
        Assert.That(SWGlobalWarMarketSimulation.EmergencyT2Rate(doctrine.CivilElectronics), Is.EqualTo((decimal) t2));
        Assert.That(model.Markets["industry1"].Goods[E].Produced, Is.Zero);
        Assert.That(doctrine.Norm, Is.EqualTo(new SWGlobalWarBaseSupplyNorm()));
        Assert.That(SWGlobalWarMarketSimulation.LanthanumDemand(2, new(Bacta: 1)), Is.EqualTo(13.475m));
    }

    [Test]
    public void AvailabilitySearchUsesTheAllowedDetourAndAnyRemainingHomeIndustry()
    {
        var model = Model();
        var blockBypass = false;
        model.TradePolicy = request => new(request.Current.Id != "short" && request.Next.Id != "short" &&
            (!blockBypass || request.Current.Id != "bypass" && request.Next.Id != "bypass"), "blocked", 0, 0, 0, 0, 0);
        model.Advance(TimeSpan.FromSeconds(130));
        Assert.That(model.Markets["enclave"].Goods[Ln].Stock, Is.Zero);
        Assert.That(model.LanthanumDoctrines[R].Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Waiting));
        Assert.That(model.HasLanthanumPath("enclave", "industry1", R), Is.True);
        Capture(model, "industry2", Empire);
        Assert.That(model.LanthanumDoctrines[R].StartedAt, Is.Null);
        blockBypass = true;
        model.RefreshLanthanumOwnership();
        Assert.That(model.LanthanumDoctrines[R].StartedAt, Is.EqualTo(model.Time));
        Assert.That(model.LanthanumDoctrines[R].Demand, Is.EqualTo(13.175m), "The still-owned enclave remains in civil demand.");
        Capture(model, "enclave", Empire);
        Assert.That(model.LanthanumDoctrines[R].Demand, Is.EqualTo(12.575m));
        Capture(model, "industry1", Empire);
        Assert.That(model.LanthanumDoctrines[R].Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.NoRecipient));
        Assert.That(model.LanthanumDoctrines[R].Demand, Is.EqualTo(12.275m));
    }

    [Test]
    public void ThresholdExcludesIncomingAndOtherReservationsButIncludesProgramReservationsOnce()
    {
        var model = Model(128.75m);
        Capture(model, "enclave", Empire);
        var doctrine = model.LanthanumDoctrines[R];
        Assert.That(doctrine.Stock, Is.EqualTo(20 * doctrine.Demand));
        Assert.That(doctrine.StartedAt, Is.Null);
        var good = model.Markets["industry1"].Goods[Ln];
        good.Incoming = Q(200);
        good.ReservedIncoming = Q(100);
        var reserved = new SWGlobalWarStockRequest(model.Generation!.Value, Guid.NewGuid(), "industry1", Ln,
            1, R, SWGlobalWarMarketSimulation.BaseSupplyPurpose);
        Assert.That(model.TryReserveStock(reserved, out _), Is.True);
        model.RefreshLanthanumOwnership();
        Assert.That(doctrine.Stock, Is.EqualTo(257.5m));
        Assert.That(doctrine.StartedAt, Is.Null);
        var unrelated = reserved with { Operation = Guid.NewGuid(), Purpose = "unrelated-export" };
        Assert.That(model.TryReserveStock(unrelated, out _), Is.True);
        model.RefreshLanthanumOwnership();
        Assert.That(doctrine.Stock, Is.EqualTo(257.5m - 1m / 60000));
        Assert.That(doctrine.StartedAt, Is.EqualTo(TimeSpan.Zero));
        Assert.That(model.TryCancelStock(model.Generation.Value, unrelated.Operation), Is.True);
        model.RefreshLanthanumOwnership();
        Assert.That(doctrine.StartedAt, Is.EqualTo(TimeSpan.Zero), "Rising stock cannot cancel an already started episode.");
        model.TradePolicy = _ => new(false, "blocked", 0, 0, 0, 0, 0);
        Capture(model, "enclave", R);
        Assert.That(doctrine.Demand, Is.EqualTo(13.475m));
        Assert.That(20 * doctrine.Demand, Is.EqualTo(269.5m));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StartupMoneySpaceAndFractionalTimeNeverCreateProductionCredit(bool fragmented)
    {
        var model = Model();
        var cash = 399L;
        model.CanFundLanthanum = (_, _) => cash >= 400;
        model.SettleLanthanum = (_, complete) =>
        {
            if (cash < 400)
                return false;
            cash -= 400;
            complete();
            return true;
        };
        Capture(model, "enclave", Empire);
        var doctrine = model.LanthanumDoctrines[R];
        var deposit = model.LanthanumDeposits["reserve"];
        var good = model.Markets["reserve"].Goods[Ln];
        Advance(119);
        Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Starting));
        Advance(1);
        Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Releasing));
        Advance(1);
        Assert.That(deposit.Released, Is.Zero);
        Assert.That(cash, Is.EqualTo(399));
        Advance(60);
        Assert.That(deposit.Remaining, Is.EqualTo(1200));
        cash = 400;
        good.Capacity = Q(1) - 1;
        Advance(60);
        Assert.That(cash, Is.EqualTo(400));
        Assert.That(deposit.Released, Is.Zero);
        good.Capacity = Q(1);
        Advance(4);
        Assert.That(deposit.Released, Is.Zero);
        Advance(1);
        Assert.That(deposit.Released, Is.EqualTo(1));
        Assert.That(good.Stock, Is.EqualTo(Q(1)));
        Assert.That(cash, Is.Zero);
        Assert.That(model.SetLanthanumDoctrine(R, 0, false), Is.True);
        Advance(600);
        Assert.That(deposit.Released, Is.EqualTo(1));
        Capture(model, "enclave", R);
        Capture(model, "enclave", Empire);
        Assert.That(doctrine.Automatic, Is.False);
        Assert.That(model.SetLanthanumDoctrine(R, 1, true), Is.True);
        Assert.That(doctrine.StartedAt, Is.EqualTo(model.Time));
        Assert.That(deposit.Remaining, Is.EqualTo(1199));

        void Advance(int seconds)
        {
            if (!fragmented)
                model.Advance(TimeSpan.FromSeconds(seconds));
            else
                for (var i = 0; i < seconds * 4; i++)
                    model.Advance(TimeSpan.FromMilliseconds(250));
        }
    }

    [Test]
    public void BothPhysicalDepositsExhaustOnceAndCaptureCannotResetTheirReleasePace()
    {
        var model = Model();
        foreach (var id in new[] { "industry1", "industry2" })
            model.Markets[id].Goods[Ln].MinimumBuffer = Q(600);
        var releases = new List<SWGlobalWarLanthanumRelease>();
        model.CanFundLanthanum = (_, _) => true;
        model.SettleLanthanum = (plan, complete) => { releases.Add(plan); complete(); return true; };
        Capture(model, "enclave", Empire);
        model.Advance(TimeSpan.FromSeconds(127));
        Assert.That(model.LanthanumDeposits["reserve"].Released, Is.EqualTo(1));
        Capture(model, "enemyReserve", R);
        var started = model.LanthanumDoctrines[R].StartedAt;
        model.Advance(TimeSpan.FromSeconds(3));
        Assert.That(model.LanthanumDeposits["reserve"].Released, Is.EqualTo(2));
        Assert.That(model.LanthanumDeposits["enemyReserve"].Released, Is.Zero);
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(model.LanthanumDeposits["enemyReserve"].Released, Is.EqualTo(1));
        Assert.That(model.LanthanumDoctrines[R].StartedAt, Is.EqualTo(started));
        model.Advance(TimeSpan.FromSeconds(6200));
        // Fractional deliveries can leave less than one whole unit of space at a deposit. Actual use
        // frees that space; the remaining ore must never be released into a warehouse that cannot fit it.
        var withdrawn = 0;
        foreach (var deposit in model.LanthanumDeposits.Values.Where(d => d.Remaining > 0))
        {
            Assert.That(deposit.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.WaitingSpace));
            var use = new SWGlobalWarStockRequest(model.Generation!.Value, Guid.NewGuid(), deposit.Planet, Ln, Q(1), R, "test-factory-use");
            Assert.That(model.TryReserveStock(use, out _), Is.True);
            Assert.That(model.TryCommitStock(use.Generation, use.Operation, out _), Is.True);
            withdrawn += Q(1);
        }
        model.Advance(TimeSpan.FromSeconds(10));
        Assert.That(model.LanthanumDeposits.Values.Sum(d => d.Released), Is.EqualTo(2400));
        Assert.That(model.LanthanumDeposits.Values.Sum(d => d.Remaining), Is.Zero);
        foreach (var series in releases.GroupBy(p => p.Planet))
            Assert.That(series.Zip(series.Skip(1), (a, b) => b.At - a.At).All(d => d >= TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(model.Markets.Values.Sum(m => (long) m.Goods[Ln].Stock) + model.Shipments.Sum(s => (long) s.Quantity) + withdrawn,
            Is.EqualTo(Q(2400)));
        model.Advance(TimeSpan.FromMinutes(10));
        Assert.That(releases, Has.Count.EqualTo(2400));
        var fresh = Model();
        Assert.That(fresh.Generation, Is.Not.EqualTo(model.Generation));
        Assert.That(fresh.LanthanumDeposits.Values.Sum(d => d.Remaining), Is.EqualTo(2400));
    }
}
