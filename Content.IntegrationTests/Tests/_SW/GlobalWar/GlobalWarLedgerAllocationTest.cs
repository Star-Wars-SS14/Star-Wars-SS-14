using System;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarLedgerAllocationTest
{
    [Test]
    public async Task WorkClaimsShareParentReservationAndRemainAtomicAcrossCompetingSpends()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var ticker = server.System<GameTicker>();
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(server.System<SWGlobalWarCampaignSystem>().TryGetCampaign(out var campaign), Is.True);
            var generation = campaign.Comp.Generation;
            var faction = SWGlobalWarFaction.Republic;
            var account = SWGlobalWarAccount.Logistics;
            SWGlobalWarLedgerCommand Money(SWGlobalWarLedgerAction action, long amount,
                Guid reservation = default, Guid allocation = default) => new(generation, Guid.NewGuid(), 0,
                action, faction, account, amount, Reservation: reservation, Purpose: "hub-allocation-test", Allocation: allocation);
            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Reserve, 1000), out var parent), Is.True);
            var operation = Guid.NewGuid();
            Assert.That(ledger.TryAllocateReservation(generation, operation, parent, 300, "hub-upgrade"), Is.True);
            Assert.That(ledger.TryAllocateReservation(generation, operation, parent, 300, "hub-upgrade"), Is.True);
            Assert.That(ledger.TryAllocateReservation(generation, operation, parent, 301, "hub-upgrade"), Is.False);
            Assert.That(ledger.TryAllocateReservation(generation, Guid.NewGuid(), parent, 701, "another-work"), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(150000, 1000)),
                "The 300 claim is already part of the 1000 reservation, not another account encumbrance.");
            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit, 701, parent), out _), Is.False);
            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit, 700, parent), out _), Is.True);
            Assert.That(ledger.IsAllocationActive(generation, operation, parent, 300), Is.True);
            var commit = Money(SWGlobalWarLedgerAction.Commit, 300, parent, operation);
            var failed = new[] { commit, Money(SWGlobalWarLedgerAction.Debit, long.MaxValue) };
            Assert.That(ledger.TryExecuteSystemBatch(Guid.NewGuid(), failed), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(149300, 300)));
            Assert.That(ledger.IsAllocationActive(generation, operation, parent, 300), Is.True);
            Assert.That(ledger.TryExecuteSystem(commit, out _), Is.False, "A failed batch cannot be replayed piecemeal.");
            commit = commit with { Request = Guid.NewGuid() };
            Assert.That(ledger.TryExecuteSystem(commit, out _), Is.True);
            Assert.That(ledger.TryExecuteSystem(commit, out _), Is.True);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(149000, 0)));
            Assert.That(ledger.IsAllocationActive(generation, operation, parent, 300), Is.False);
            Assert.That(ledger.TryReleaseAllocation(generation, operation), Is.False);
            Assert.That(ledger.TryAllocateReservation(generation, operation, parent, 300, "hub-upgrade"), Is.False);

            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Reserve, 1000), out var next), Is.True);
            var cancelled = Guid.NewGuid();
            Assert.That(ledger.TryAllocateReservation(generation, cancelled, next, 400, "hub-upgrade"), Is.True);
            Assert.That(ledger.TryReleaseAllocation(generation, cancelled), Is.True);
            Assert.That(ledger.TryReleaseAllocation(generation, cancelled), Is.True);
            Assert.That(ledger.TryAllocateReservation(generation, cancelled, next, 400, "hub-upgrade"), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(149000, 1000)));
            var replacement = Guid.NewGuid();
            Assert.That(ledger.TryAllocateReservation(generation, replacement, next, 1000, "hub-upgrade"), Is.True);
            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release, 0, next), out _), Is.True);
            Assert.That(ledger.IsAllocationActive(generation, replacement, next, 1000), Is.False);
            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit, 1000, next, replacement), out _), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(149000, 0)),
                "Releasing an unspent parent is not a refund income.");

            Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Reserve, 1000), out var transferParent), Is.True);
            var transferClaim = Guid.NewGuid();
            Assert.That(ledger.TryAllocateReservation(generation, transferClaim, transferParent, 400, "hub-upgrade"), Is.True);
            var transfer = Money(SWGlobalWarLedgerAction.CommitTransfer, 601, transferParent) with
            {
                Destination = SWGlobalWarAccount.Industry,
            };
            Assert.That(ledger.TryExecuteSystem(transfer, out _), Is.False);
            transfer = transfer with { Request = Guid.NewGuid(), Amount = 400, Allocation = transferClaim };
            Assert.That(ledger.TryExecuteSystem(transfer, out _), Is.True);
            Assert.That(ledger.TryExecuteSystem(transfer, out _), Is.True);
            Assert.That(ledger.TryExecuteSystem(transfer with { Request = Guid.NewGuid() }, out _), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(148600, 600)));
            Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(120400));
            Assert.That(ledger.TryReleaseAllocation(generation, transferClaim), Is.False);
            var endingClaim = Guid.NewGuid();
            Assert.That(ledger.TryAllocateReservation(generation, endingClaim, transferParent, 600, "hub-upgrade"), Is.True);
            Assert.That(ledger.TryGetLedger(out var endingLedger), Is.True);
            Assert.That(server.System<SWGlobalWarCampaignSystem>()
                .TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
            Assert.That(ledger.TryReleaseAllocation(generation, endingClaim), Is.False);
            Assert.That(ledger.TryExecuteSystem(transfer, out _), Is.False, "Ending also rejects old paid receipts.");
            Assert.That(ledger.IsAllocationActive(generation, endingClaim, transferParent, 600), Is.False);
            Assert.That(endingLedger.Comp.Allocations[endingClaim].Status, Is.EqualTo(SWGlobalWarMoneyAllocationStatus.Active),
                "Ending denies operations; it must not pretend Stopped cleanup already happened.");
            ticker.EndGameRule(campaign.Owner);
            Assert.That(endingLedger.Comp.Reservations, Is.Empty);
            Assert.That(endingLedger.Comp.Allocations[endingClaim].Status, Is.EqualTo(SWGlobalWarMoneyAllocationStatus.Released));
            Assert.That(endingLedger.Comp.Balances[(faction, account)], Is.EqualTo(148600));
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(ledger.TryGetLedger(out var freshLedger), Is.True);
            Assert.That(freshLedger.Comp.Generation, Is.Not.EqualTo(generation));
            Assert.That(freshLedger.Comp.Allocations, Is.Empty);
            Assert.That(freshLedger.Comp.Requests, Is.Empty);
            Assert.That(ledger.TryAllocateReservation(generation, endingClaim, transferParent, 600, "hub-upgrade"), Is.False);
            Assert.That(ledger.TryExecuteSystem(transfer, out _), Is.False);
            Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(150000, 0)));
            ticker.EndGameRule(freshLedger.Owner);
        });
        await pair.CleanReturnAsync();
    }
}
