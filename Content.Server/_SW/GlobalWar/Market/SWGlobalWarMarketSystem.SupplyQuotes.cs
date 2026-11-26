using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private bool MatchesSupplyQuote(SWGlobalWarAssignment actor, SWGlobalWarSupplyCommand command)
    {
        if (!_supplyRequests.TryGetValue((actor.User, command.Quote), out var saved) || saved.Assignment != actor.Id ||
            saved.Command.Action != SWGlobalWarSupplyAction.PreviewPurchase || saved.Result.Quote is not { } quote ||
            saved.Command.Source != command.Source || saved.Command.Recipient != command.Recipient ||
            saved.Command.Kind != command.Kind || saved.Command.Bacta != command.Bacta ||
            saved.Command.Quantity != command.Quantity || saved.Command.HeadDecision != command.HeadDecision ||
            command.ExpectedRevision != quote.LedgerRevision || !TryQuoteSupplyPurchase(actor, command, out var current))
            return false;
        // Changes to the displayed plan require a fresh calculation; unrelated treasury taxes do not.
        // Fixed recipe inputs are determined by kind, composition and whole output, all compared here.
        return quote.Failure == SWGlobalWarSupplyFailure.None && current.Failure == SWGlobalWarSupplyFailure.None &&
            current.LineRevision == quote.LineRevision &&
            current.Ready == quote.Ready && current.Output == quote.Output && current.Cost == quote.Cost &&
            current.Market == quote.Market && current.Local == quote.Local && current.Duration == quote.Duration &&
            (current.AllowedArsenals ?? []).SequenceEqual(quote.AllowedArsenals ?? []);
    }

    /// <summary>Computes a non-binding physical and monetary plan without allocating goods, materials or money.</summary>
    private bool TryQuoteSupplyPurchase(SWGlobalWarAssignment actor, SWGlobalWarSupplyCommand command,
        out SWGlobalWarSupplyQuote quote)
    {
        quote = default!;
        if (command.Source is not { } sourceId || command.Recipient is not { } recipientId ||
            command.Quantity is < 1 or > 120 || command.Kind == SWGlobalWarSupplyKind.AdvancedElectronics ||
            command.Bacta && command.Kind != SWGlobalWarSupplyKind.Medicine ||
            !TryGetSupplyStore(command.Generation, GetEntity(sourceId), actor.Faction, out var source, out var inventory) ||
            !TryGetSupplyStore(command.Generation, GetEntity(recipientId), actor.Faction, out var recipient, out var receiving) ||
            source.Arsenal || !recipient.Arsenal || !_ledger.TryGetLedger(out var ledger))
            return false;
        var model = Simulation!;
        var program = model.SupplyPrograms[actor.Faction];
        var line = program.Lines[command.Kind];
        var request = new SWGlobalWarSupplyPurchaseRequest(command.Generation, command.Request, ledger.Comp.Revision,
            actor.Faction, GetEntity(sourceId), GetEntity(recipientId), command.Kind, command.Quantity, command.Bacta);
        var ready = inventory.ContainedEntities.Count(e => IsUnpaidSupplyItem(e, request));
        ready = Math.Min(ready, command.Quantity);
        var missing = command.Quantity - ready;
        var recipe = SupplyRecipe(command.Kind, command.Bacta);
        var batches = missing == 0 || line.LocalPercent == 100 ? 0 :
            (int) decimal.Ceiling((missing - line.MarketRemainder) / ((100m - line.LocalPercent) / 100m * recipe.Output));
        var output = batches * recipe.Output;
        var market = output == 0 ? 0 : (int) decimal.Floor(line.MarketRemainder + output * (100m - line.LocalPercent) / 100m);
        var local = output - market;
        var rate = model.EffectiveSupplyRate(actor.Faction, command.Kind);
        var duration = rate > 0 ? TimeSpan.FromTicks((long) decimal.Ceiling(output * 60m * TimeSpan.TicksPerSecond / rate)) : TimeSpan.Zero;
        var inputs = recipe.Raw.Select(p => new SWGlobalWarSupplyInputView(p.Key, false, p.Value * batches))
            .Concat(recipe.Physical.Select(p => new SWGlobalWarSupplyInputView(p.Key, true, p.Value * batches)))
            .Where(p => p.Quantity > 0).ToList();
        var cost = (long) command.Quantity * SWGlobalWarSupplyProgram.UnitPrice(command.Kind, command.Bacta);
        var failure = SWGlobalWarSupplyFailure.None;
        if (!HasSupplyPurchaseRoute(request))
            failure = SWGlobalWarSupplyFailure.TransportRoute;
        else if (program.RemainingQuota(command.Kind) < command.Quantity)
            failure = SWGlobalWarSupplyFailure.Quota;
        else if (_ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Available < cost)
            failure = SWGlobalWarSupplyFailure.Funds;
        else if (!HasSupplyPlace(GetEntity(recipientId), command.Kind, command.Quantity, receiving))
            failure = SWGlobalWarSupplyFailure.ReceiveSpace;
        else if (missing > 0)
        {
            if (command.Bacta != (command.Kind == SWGlobalWarSupplyKind.Medicine && line.Bacta))
                failure = SWGlobalWarSupplyFailure.Revision;
            else if (line.LocalPercent == 100)
                failure = SWGlobalWarSupplyFailure.LocalOnly;
            else if (rate <= 0)
                failure = SWGlobalWarSupplyFailure.Rate;
            else if (_supplyProduction.Values.Any(p => p.Request.Faction == actor.Faction && p.Request.Kind == command.Kind &&
                         p.Stage is SWGlobalWarSupplyStage.Reserved or SWGlobalWarSupplyStage.Producing))
                failure = SWGlobalWarSupplyFailure.LineBusy;
            else if (batches > 120 || !HasSupplyPlace(GetEntity(sourceId), command.Kind, output, inventory))
                failure = SWGlobalWarSupplyFailure.OutputSpace;
            else if (recipe.Raw.Any(p => model.Markets[source.Planet].Goods[p.Key].Available <
                         SWGlobalWarMarketUnits.FromUnits(p.Value * batches)) || recipe.Physical.Any(p =>
                         inventory.ContainedEntities.Where(e => !_supplyItems.ContainsKey(e) && MatchesSupplyMaterial(e, p.Key))
                             .Sum(e => (long) _supplyStacks.GetCount(e)) < (long) p.Value * batches))
                failure = SWGlobalWarSupplyFailure.Materials;
        }
        quote = new(command.Quantity, ready, missing, output, market, local, Math.Max(0, market - missing), local,
            duration, inputs, cost, ledger.Comp.Revision, line.Revision, failure,
            _supplyConsumers.Contains((GetEntity(sourceId), command.Kind)), SupplyArsenalPoints(request)
                .Select(p => new SWGlobalWarSupplyStoreView(GetNetEntity(p.Key), p.Value.Planet, p.Value.Name, true)).ToList());
        return true;
    }
}
