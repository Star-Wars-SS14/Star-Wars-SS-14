using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    private NetEntity[] _supplySources = [];
    private NetEntity[] _supplyRecipients = [];

    private void InitializeSupplyPurchase()
    {
        SupplySourcePicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplySource = args.Id >= 0 && args.Id < _supplySources.Length ? _supplySources[args.Id] : null;
            InvalidateSupplyQuote();
            RefreshSupply();
        };
        SupplyRecipientPicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyRecipient = args.Id >= 0 && args.Id < _supplyRecipients.Length ? _supplyRecipients[args.Id] : null;
            InvalidateSupplyQuote();
            RefreshSupply();
        };
        SupplyQuantity.OnTextChanged += _ =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyQuantity = SupplyQuantity.Text;
            InvalidateSupplyQuote();
            RefreshSupply();
        };
        SupplyPurchaseBacta.OnToggled += _ =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyPurchaseBacta = SupplyPurchaseBacta.Pressed;
            InvalidateSupplyQuote();
            RefreshSupply();
        };
        SupplyPreview.OnPressed += _ =>
        {
            if (SupplyPreview.Disabled || _state?.Supply is not { CanPurchase: true } ||
                !int.TryParse(FlowDraft.SupplyQuantity, out var quantity))
                return;
            SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.PreviewPurchase, FlowDraft.SupplyKind, Source: FlowDraft.SupplySource,
                Recipient: FlowDraft.SupplyRecipient, Quantity: quantity, Bacta: FlowDraft.SupplyPurchaseBacta,
                HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyBuy.OnPressed += _ =>
        {
            if (SupplyBuy.Disabled || _state?.Supply is not { CanPurchase: true } ||
                FlowDraft.SupplyQuoteRequest is not { } request || FlowDraft.SupplyQuote is not { } quote)
                return;
            SendSupply(request with { Request = Guid.NewGuid(), Action = SWGlobalWarSupplyAction.Purchase,
                Quote = request.Request, ExpectedRevision = quote.LedgerRevision });
        };
    }

    private void InvalidateSupplyQuote()
    {
        FlowDraft.SupplyQuoteRequest = null;
        FlowDraft.SupplyQuote = null;
    }

    private void RefreshSupplyPurchase(SWGlobalWarSupplyView? view)
    {
        SupplyPurchaseControls.Visible = view?.CanPurchase == true;
        if (view?.CanPurchase != true)
        {
            SupplySourcePicker.Clear();
            SupplyRecipientPicker.Clear();
            foreach (var label in new[] { SupplyQuoteDetails, SupplyQuoteMaterials, SupplyQuotePhysical,
                         SupplyQuoteArsenals, SupplyQuoteFailure, SupplyQuantityError })
                SetText(label, string.Empty);
            return;
        }
        var sources = view.Stores.Where(s => !s.Arsenal).ToArray();
        var recipients = view.Stores.Where(s => s.Arsenal).ToArray();
        _supplySources = sources.Select(s => s.Entity).ToArray();
        _supplyRecipients = recipients.Select(s => s.Entity).ToArray();
        SupplySourcePicker.Clear();
        SupplyRecipientPicker.Clear();
        SupplySourcePicker.AddItem(Loc.GetString("sw-global-war-supply-select-source"), -1);
        SupplyRecipientPicker.AddItem(Loc.GetString("sw-global-war-supply-select-recipient"), -1);
        for (var i = 0; i < sources.Length; i++)
            SupplySourcePicker.AddItem(StoreLabel(sources[i]), i);
        for (var i = 0; i < recipients.Length; i++)
            SupplyRecipientPicker.AddItem(StoreLabel(recipients[i]), i);
        SupplySourcePicker.SelectId(FlowDraft.SupplySource is { } source ? Array.IndexOf(_supplySources, source) : -1);
        SupplyRecipientPicker.SelectId(FlowDraft.SupplyRecipient is { } recipient ? Array.IndexOf(_supplyRecipients, recipient) : -1);
        SupplyQuantity.Text = FlowDraft.SupplyQuantity;
        SupplyPurchaseBacta.Visible = FlowDraft.SupplyKind == SWGlobalWarSupplyKind.Medicine;
        SupplyPurchaseBacta.Pressed = FlowDraft.SupplyPurchaseBacta;
        var pending = FlowDraft.PendingSupply != null;
        SupplySourcePicker.Disabled = SupplyRecipientPicker.Disabled = SupplyPurchaseBacta.Disabled = pending;
        SupplyQuantity.Editable = !pending;
        var validQuantity = int.TryParse(FlowDraft.SupplyQuantity, out var quantity) && quantity is >= 1 and <= 120;
        SetText(SupplyQuantityError, validQuantity ? string.Empty : Loc.GetString("sw-global-war-supply-invalid-quantity"));
        SupplyPreview.Disabled = pending || !validQuantity || SupplySourcePicker.SelectedId < 0 ||
            SupplyRecipientPicker.SelectedId < 0 || view.WarHeadDecisionRequired && !FlowDraft.SupplyHead;
        var quote = FlowDraft.SupplyQuote;
        var currentLine = view.Lines.First(l => l.Kind == FlowDraft.SupplyKind);
        var stale = quote != null && quote.LineRevision != currentLine.Revision;
        SupplyBuy.Disabled = pending || quote == null || stale || quote.Failure != SWGlobalWarSupplyFailure.None ||
            view.WarHeadDecisionRequired && !FlowDraft.SupplyHead;
        if (quote == null)
        {
            SetText(SupplyQuoteDetails, Loc.GetString("sw-global-war-supply-quote-help"));
            SetText(SupplyQuoteMaterials, string.Empty);
            SetText(SupplyQuotePhysical, string.Empty);
            SetText(SupplyQuoteArsenals, string.Empty);
            SetText(SupplyQuoteFailure, string.Empty);
            return;
        }
        SetText(SupplyQuoteDetails, Loc.GetString("sw-global-war-supply-quote-details",
            ("item", SupplyItem(FlowDraft.SupplyKind, FlowDraft.SupplyPurchaseBacta)), ("quantity", quote.Quantity),
            ("ready", quote.Ready), ("missing", quote.Missing), ("output", quote.Output),
            ("market", quote.FreeMarket), ("local", quote.FreeLocal), ("seconds", (int) Math.Ceiling(quote.Duration.TotalSeconds)),
            ("cost", quote.Cost)));
        SetText(SupplyQuoteMaterials, quote.Inputs.Count == 0 ? (quote.Missing == 0 ?
            Loc.GetString("sw-global-war-supply-no-new-inputs") : string.Empty) :
            string.Join("\n", quote.Inputs.Select(input => Loc.GetString("sw-global-war-supply-input",
                ("item", input.Physical ? _prototypes.Index<EntityPrototype>(input.Prototype).Name :
                    Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(input.Prototype).Name)),
                ("quantity", input.Quantity)))));
        SetText(SupplyQuotePhysical, quote.Missing > 0 && !quote.ConsumerRegistered ?
            Loc.GetString("sw-global-war-supply-no-consumer") : string.Empty);
        SetText(SupplyQuoteArsenals, Loc.GetString("sw-global-war-supply-quote-arsenals",
            ("points", string.Join(", ", (quote.AllowedArsenals ?? []).Select(StoreLabel)))));
        SetText(SupplyQuoteFailure, stale ? Loc.GetString("sw-global-war-supply-quote-stale") : SupplyFailure(quote.Failure));
    }

    private string StoreLabel(SWGlobalWarSupplyStoreView store)
    {
        return Loc.GetString("sw-global-war-supply-store", ("planet", PlanetName(store.Planet)),
            ("name", string.IsNullOrWhiteSpace(store.Name) ? Loc.GetString(store.Arsenal ?
                "sw-global-war-supply-recipient" : "sw-global-war-supply-source") : store.Name));
    }
}
