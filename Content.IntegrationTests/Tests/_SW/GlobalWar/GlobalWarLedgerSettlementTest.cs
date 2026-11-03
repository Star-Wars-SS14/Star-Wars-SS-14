using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarLedgerSettlementTest
{
    [Test]
    public async Task SettlementsAreAtomicReplayExactAndPublishOnlyCompletedAccounting()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var campaigns = server.System<SWGlobalWarCampaignSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(campaigns.TryGetCampaign(out var campaign), Is.True);
            Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
            var generation = campaign.Comp.Generation;
            var faction = SWGlobalWarFaction.Republic;
            var initiator = new NetUserId(Guid.NewGuid());
            long Total() => Enum.GetValues<SWGlobalWarAccount>().Sum(a => ledger.GetBalance(faction, a).Balance);
            SWGlobalWarLedgerCommand Money(SWGlobalWarLedgerAction action, SWGlobalWarAccount account,
                long amount, Guid reservation = default) => new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                action, faction, account, amount, SWGlobalWarAccount.Industry, reservation, Purpose: "settlement-test");
            var reserve = Money(SWGlobalWarLedgerAction.Reserve, SWGlobalWarAccount.War, 3000);
            Assert.That(ledger.TryExecuteSystem(reserve, out var order), Is.True);
            var pay = Money(SWGlobalWarLedgerAction.CommitTransfer, SWGlobalWarAccount.War, 1500, order);
            var failedId = Guid.NewGuid();
            var failed = new[] { pay, Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Treasury, long.MaxValue) };
            var beforeRevision = accounts.Comp.Revision;
            Assert.That(ledger.TryExecuteSystemBatch(failedId, failed), Is.False);
            Assert.That(ledger.TryExecuteSystemBatch(failedId, failed), Is.False);
            Assert.That(Total(), Is.EqualTo(900000));
            Assert.That(accounts.Comp.Revision, Is.EqualTo(beforeRevision));
            Assert.That(ledger.TryGetReservation(order, out var unchanged), Is.True);
            Assert.That(unchanged.Remaining, Is.EqualTo(3000));
            Assert.That(ledger.TryExecuteSystem(pay, out _), Is.False, "A failed batch cannot be replayed piecemeal.");
            Assert.That(ledger.TryExecuteSystem(pay, out _, initiator), Is.False,
                "The journal author must not change the system request namespace.");

            var operation = Guid.NewGuid();
            var plan = new[]
            {
                pay with { Request = Guid.NewGuid() },
                Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Treasury, 100),
            };
            var callbacks = 0;
            var goodsComplete = false;
            probe.LedgerChanged = () =>
            {
                callbacks++;
                Assert.That(goodsComplete, Is.True);
                Assert.That(ledger.TryExecuteSystemBatch(operation, plan), Is.True);
                Assert.That(Total(), Is.EqualTo(900100));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War),
                    Is.EqualTo(new SWGlobalWarAccountBalance(178500, 1500)));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(121500));
            };
            try
            {
                using (ledger.DeferNotifications())
                {
                    Assert.That(ledger.TryExecuteSystemBatch(operation, plan), Is.True);
                    Assert.That(callbacks, Is.Zero);
                    goodsComplete = true;
                }
                Assert.That(callbacks, Is.EqualTo(1));
            }
            finally
            {
                probe.LedgerChanged = null;
            }
            Assert.That(ledger.TryExecuteSystemBatch(operation,
                [plan[0] with { ObservedRevision = accounts.Comp.Revision }, plan[1]]), Is.False);
            Assert.That(ledger.TryExecuteSystemBatch(Guid.NewGuid(), plan), Is.False);
            Assert.That(ledger.TryExecuteSystem(plan[0], out _), Is.True);
            Assert.That(ledger.TryExecuteSystem(plan[0], out _, initiator), Is.True);
            Assert.That(ledger.TryExecuteSystem(plan[1], out _, initiator), Is.True);
            Assert.That(Total(), Is.EqualTo(900100));

            var authored = Money(SWGlobalWarLedgerAction.Debit, SWGlobalWarAccount.Treasury, 1);
            Assert.That(ledger.TryExecuteSystem(authored, out _, initiator), Is.True);
            Assert.That(ledger.TryExecuteSystem(authored, out _), Is.True);
            Assert.That(ledger.TryExecuteSystemBatch(Guid.NewGuid(), [authored]), Is.False);
            Assert.That(Total(), Is.EqualTo(900099));
            Assert.That(accounts.Comp.Journal.Single(e => e.Command.Request == authored.Request).Author,
                Is.EqualTo(initiator), "Deduplication must not discard the original audit author.");

            // The second credit overflows only in aggregate; neither command may take effect.
            var aggregate = new[]
            {
                Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Industry, long.MaxValue - 121500),
                Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Industry, 1),
            };
            Assert.That(ledger.TryExecuteSystemBatch(Guid.NewGuid(), aggregate), Is.False);
            Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(121500));

            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Credit,
                SWGlobalWarAccount.Industry, long.MaxValue - 121500), out _), Is.True);
            Assert.That(ledger.TryExecuteSystem(pay with { Request = Guid.NewGuid() }, out _), Is.False);
            Assert.That(ledger.TryGetReservation(order, out var remaining), Is.True);
            Assert.That(remaining.Remaining, Is.EqualTo(1500));
            Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(178500));
            Assert.That(campaigns.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
            Assert.That(ledger.TryExecuteSystemBatch(operation, plan), Is.False);
            ticker.EndGameRule(campaign.Owner);
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(ledger.TryGetLedger(out var next), Is.True);
            Assert.That(next.Comp.SystemBatches, Is.Empty);
            Assert.That(Total(), Is.EqualTo(900000));
            Assert.That(ledger.TryExecuteSystemBatch(operation, plan), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
