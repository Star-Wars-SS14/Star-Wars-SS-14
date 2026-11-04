#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._SW.Artillery;
using Content.Server._SW.Economy.BuyConsole;
using Content.Server._SW.TurretConsole;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._SW;
using Content.Shared._SW.Artillery;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Access.Components;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.Echo.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Sends the same messages a modified client could send and checks that the server refuses them.
/// Messages go straight to the bus, as the engine does after its own range check.
/// </summary>
[TestFixture]
public sealed class SWConsoleValidationTest
{
    private static readonly ProtoId<SWCurrencyPrototype> Ic = "SWEmpireCreditCurrency";
    private static readonly ProtoId<SWCurrencyPrototype> Rc = "SWRepublicCreditCurrency";
    private static readonly ProtoId<SWCurrencyPrototype> Rs = "SWReinforcementScore";

    private static readonly ProtoId<SWCargoProductPrototype> Grenade = "SWGrenadeShrapnelCP";
    private static readonly ProtoId<SWCargoProductPrototype> RepublicRifle = "SWH206RenegadeCP";

    private static void Send(TestPair pair, EntityUid console, BoundUserInterfaceMessage message, EntityUid actor = default)
    {
        message.Actor = actor;
        message.UiKey = SWBuyConsoleUIKey.Key;
        pair.Server.EntMan.EventBus.RaiseLocalEvent(console, (object) message, true);
    }

    private static Dictionary<ProtoId<SWCurrencyPrototype>, float> Balance(TestPair pair, EntityUid console)
    {
        return pair.Server.EntMan.GetComponent<SWBankAccountAccessorComponent>(console).Account!.Value.Comp.CurrentAmount;
    }

    [TestCase("sw-console-no-account")]
    [TestCase("sw-economy-not-enough-money")]
    [TestCase("sw-economy-payout-disabled")]
    [TestCase("sw-economy-payout-invalid")]
    [TestCase("sw-economy-deposit-disabled")]
    [TestCase("sw-economy-deposit-nothing")]
    [TestCase("sw-economy-order-invalid-amount")]
    [TestCase("sw-economy-order-unavailable")]
    [TestCase("sw-economy-exchange-same-currency")]
    [TestCase("sw-economy-exchange-invalid-amount")]
    [TestCase("sw-economy-exchange-unavailable")]
    [TestCase("sw-economy-exchange-too-small")]
    [TestCase("sw-economy-bill-cannot-pay")]
    [TestCase("sw-artillery-bad-coordinates")]
    [TestCase("sw-artillery-cooldown")]
    [TestCase("sw-artillery-no-attacks")]
    [TestCase("sw-capture-console-not-member")]
    [TestCase("sw-capture-console-own-station")]
    [TestCase("sw-capture-console-timeout")]
    public async Task RefusalHasText(string key)
    {
        await using var pair = await PoolManager.GetServerClient();
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();
        Assert.That(loc.HasString(key), key);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurchaseIsValidated()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity("SWBuyConsoleEmpire", map.GridCoords);
            var balance = Balance(pair, console);
            var price = server.ResolveDependency<IPrototypeManager>().Index(Grenade).Cost[Ic];

            // Amount -10 used to pay out 10 prices into an empty wallet.
            balance[Ic] = 0;
            Send(pair, console, new SWTryPurchaseMessage(Grenade, -10));
            Assert.That(balance[Ic], Is.EqualTo(0));

            balance[Ic] = 10000;
            Send(pair, console, new SWTryPurchaseMessage(Grenade, 0));
            Send(pair, console, new SWTryPurchaseMessage(Grenade, int.MinValue));
            Send(pair, console, new SWTryPurchaseMessage(Grenade, 101));
            Send(pair, console, new SWTryPurchaseMessage(RepublicRifle, 1));
            Send(pair, console, new SWTryPurchaseMessage("SWNoSuchProduct", 1));
            Assert.That(balance[Ic], Is.EqualTo(10000));

            Send(pair, console, new SWTryPurchaseMessage(Grenade, 2));
            Assert.That(balance[Ic], Is.EqualTo(10000 - price * 2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PayoutIsValidated()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var console = server.EntMan.SpawnEntity("SWBuyConsoleEmpire", map.GridCoords);
            var balance = Balance(pair, console);
            balance[Ic] = 0;

            // Payout -500 used to leave 500 in an empty wallet.
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = -500 }));
            Assert.That(balance[Ic], Is.EqualTo(0));

            balance[Ic] = 1000;
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = 0.5f }));
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = float.NaN }));
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = float.NegativeInfinity }));
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = 0 }));
            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = 1001 }));
            Send(pair, console, new SWTryPayoutMessage(new() { [Rs] = 1 }));
            Send(pair, console, new SWTryPayoutMessage(new() { ["SWNoSuchCurrency"] = 1 }));
            Assert.That(balance[Ic], Is.EqualTo(1000));

            Send(pair, console, new SWTryPayoutMessage(new() { [Ic] = 400 }));
            Assert.That(balance[Ic], Is.EqualTo(600));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExchangeIsValidated()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var console = server.EntMan.SpawnEntity("SWExchangeConsole", map.GridCoords);
            var balance = Balance(pair, console);
            balance[Ic] = 100;
            balance[Rc] = 1;
            balance[Rs] = 100;

            Send(pair, console, new SWTryExchangeMessage(Ic, Rc, -10));
            Send(pair, console, new SWTryExchangeMessage(Ic, Rc, 0));
            Send(pair, console, new SWTryExchangeMessage(Ic, Ic, 10));
            Send(pair, console, new SWTryExchangeMessage(Rs, Ic, 10));
            Send(pair, console, new SWTryExchangeMessage(Ic, Rs, 10));
            Send(pair, console, new SWTryExchangeMessage("SWNoSuchCurrency", Ic, 10));
            Send(pair, console, new SWTryExchangeMessage(Ic, Rc, 101));
            // 1 RC rounds down to 0 IC.
            Send(pair, console, new SWTryExchangeMessage(Rc, Ic, 1));
            Assert.Multiple(() =>
            {
                Assert.That(balance[Ic], Is.EqualTo(100));
                Assert.That(balance[Rc], Is.EqualTo(1));
                Assert.That(balance[Rs], Is.EqualTo(100));
            });

            Send(pair, console, new SWTryExchangeMessage(Ic, Rc, 10));
            Assert.Multiple(() =>
            {
                Assert.That(balance[Ic], Is.EqualTo(90));
                Assert.That(balance[Rc], Is.EqualTo(16));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BillIsTakenFromServer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var ticker = server.System<GameTicker>();
        var bank = server.System<SWSharedBankSystem>();

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("FactionsOnlyGamerule");
            var console = server.EntMan.SpawnEntity("SWFinanceConsole", map.GridCoords);
            var account = server.EntMan.GetComponent<SWBankAccountAccessorComponent>(console).Account!.Value;
            var bills = server.EntMan.GetComponent<SWBillContainerComponent>(account).Bills;
            bills.Clear();

            var bill = new SWBill { Name = "test", CurrencyRequests = new() { [Ic] = 700 } };
            bank.SendBill(account.Owner, ref bill);
            account.Comp.CurrentAmount[Ic] = 1000;

            // The old message carried the whole bill, so a client could pay it for 0.
            Send(pair, console, new SWTryPayBillMessage(bill.Id + 100000));
            Assert.Multiple(() =>
            {
                Assert.That(account.Comp.CurrentAmount[Ic], Is.EqualTo(1000));
                Assert.That(bills, Has.Count.EqualTo(1));
            });

            Send(pair, console, new SWTryPayBillMessage(bill.Id));
            Assert.Multiple(() =>
            {
                Assert.That(account.Comp.CurrentAmount[Ic], Is.EqualTo(300));
                Assert.That(bills, Is.Empty);
            });

            // Paid already: the same message must not charge twice.
            Send(pair, console, new SWTryPayBillMessage(bill.Id));
            Assert.That(account.Comp.CurrentAmount[Ic], Is.EqualTo(300));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArtilleryNeedsCoordinatesOnGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity("SWArtilleryConsole", map.GridCoords);
            entMan.RemoveComponent<AccessReaderComponent>(console);
            var comp = entMan.GetComponent<ArtilleryConsoleComponent>(console);
            comp.CurrentDelay = 0;
            var attacks = comp.AttacksLeft;
            var actor = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Send(pair, console, new ArtilleryConsoleCallArtileryMessage(100000, 100000), actor);
            Send(pair, console, new ArtilleryConsoleCallArtileryMessage(int.MaxValue, int.MinValue), actor);
            Assert.That(comp.AttacksLeft, Is.EqualTo(attacks));

            Send(pair, console, new ArtilleryConsoleCallArtileryMessage(0, 0), actor);
            Assert.That(comp.AttacksLeft, Is.EqualTo(attacks - 1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretJobMustBeKnown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity("SWTurretConsoleEmpire", map.GridCoords);
            entMan.RemoveComponent<AccessReaderComponent>(console);
            var comp = entMan.GetComponent<SWTurretConsoleComponent>(console);
            var actor = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var jobs = comp.JobsAllowed.Count;

            // An unknown key used to throw inside the message handler.
            Send(pair, console, new SWTurretJobToggleMessage("SWNoSuchJob"), actor);
            Assert.That(comp.JobsAllowed, Has.Count.EqualTo(jobs));

            var job = comp.JobsAllowed.Keys.First();
            var allowed = comp.JobsAllowed[job];
            Send(pair, console, new SWTurretJobToggleMessage(job), actor);
            Assert.That(comp.JobsAllowed[job], Is.EqualTo(!allowed));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaptureButtonIsValidated()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var stations = server.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            var station = entMan.SpawnEntity("StandardNanotrasenStation", MapCoordinates.Nullspace);
            stations.AddGridToStation(station, map.Grid);
            var capturable = entMan.EnsureComponent<CapturableStationComponent>(station);
            capturable.Fraction = "EmpireFaction";

            var console = entMan.SpawnEntity("ComputerCapture", map.GridCoords);
            var comp = entMan.GetComponent<CaptureConsoleComponent>(console);

            var civilian = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var imperial = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<SWFactionMemberComponent>(imperial).Faction = "EmpireFaction";
            var republican = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<SWFactionMemberComponent>(republican).Faction = "RepublicFaction";

            // A stranger used to lock the button for 15 s for everybody.
            Send(pair, console, new CaptureConsoleButtonPressedMessage(), civilian);
            Assert.That(comp.IsTimeout, Is.False);

            // Capturing your own station used to pay the reward again.
            Send(pair, console, new CaptureConsoleButtonPressedMessage(), imperial);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Capturing, Is.False);
                Assert.That(comp.IsTimeout, Is.False);
            });

            Send(pair, console, new CaptureConsoleButtonPressedMessage(), republican);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Capturing, Is.True);
                Assert.That(comp.IsTimeout, Is.True);
            });

            // The window keeps the button off during the timeout, the server has to as well.
            Send(pair, console, new CaptureConsoleButtonPressedMessage(), republican);
            Assert.That(comp.Capturing, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
