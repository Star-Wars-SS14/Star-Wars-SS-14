using Content.Shared.Cargo;
using Content.Client.Cargo.UI;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.IdentityManagement;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;
using Content.Client._SW.Economy.UI.BuyConsole;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Content.Client._SW.Economy.UI.BasePayAble;
using Content.Client._SW.Economy.UI;
using Robust.Client.UserInterface;
using System.Linq;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;

namespace Content.Client.Cargo.BUI
{
    public sealed class SWCargoOrderConsoleBoundUserInterface : SWBasePayableBoundUserInterface
    {
        [ViewVariables]
        private SWCargoConsoleMenu? _menu;

        /// <summary>
        /// This is the separate popup window for individual orders.
        /// </summary>
        [ViewVariables]
        private SWCargoConsoleOrderMenu? _orderMenu;

        [ViewVariables]
        public string? AccountName { get; private set; }

        [ViewVariables]
        public int BankBalance { get; private set; }

        [ViewVariables]
        public int OrderCount { get; private set; }

        /// <summary>
        /// Currently selected product
        /// </summary>
        [ViewVariables]
        private SWCargoProductPrototype? _product;

        public SWCargoOrderConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            var spriteSystem = EntMan.System<SpriteSystem>();
            var dependencies = IoCManager.Instance!;
            _menu = new SWCargoConsoleMenu(Owner, EntMan, dependencies.Resolve<IPrototypeManager>(), spriteSystem);
            var localPlayer = dependencies.Resolve<IPlayerManager>().LocalEntity;
            var description = new FormattedMessage();

            string orderRequester;

            if (EntMan.EntityExists(localPlayer))
                orderRequester = Identity.Name(localPlayer.Value, EntMan);
            else
                orderRequester = string.Empty;

            _orderMenu = new SWCargoConsoleOrderMenu();
            ApplyOwnerAccent(_menu);
            ApplyOwnerAccent(_orderMenu);

            _menu.OnClose += Close;

            _menu.OnItemSelected += (args) =>
            {
                if (args.Button.Parent is not SWProductRow row)
                    return;

                description.Clear();
                description.PushColor(Color.White); // Rich text default color is grey
                if (row.MainButton.ToolTip != null)
                    description.AddText(row.MainButton.ToolTip);

                _orderMenu.Description.SetMessage(description);
                _product = row.Product;
                _orderMenu.ProductName.Text = row.ProductName.Text;
                _orderMenu.PointCost.Text = row.PointCost.Text;
                _orderMenu.Amount.Value = 1;

                _orderMenu.OpenCentered();
            };

            _orderMenu.SubmitButton.OnPressed += (_) => TryPurchase();
            _menu.OnWithdrawalButtonPressed += OpenWithdrawal;
            _menu.OnAddMoneyButtonPressed += OnAddMoney;
            _menu.OpenCentered();
        }

        private void OpenWithdrawal()
        {
            if (_menu == null)
            {
                return;
            }
            OpenWithdrawal(_menu.Balance.Keys.ToList());
        }
        private void Populate()
        {
            if (_menu == null)
                return;

            _menu.PopulateProducts();
            _menu.PopulateCategories();
        }
        protected override void OnActionSucceeded(SWConsoleAction action)
        {
            base.OnActionSucceeded(action);
            if (action == SWConsoleAction.Purchase)
            {
                _orderMenu?.Close();
            }
        }

        public override void PopulateBalance(Dictionary<ProtoId<SWCurrencyPrototype>, float> amount)
        {
            //if (amount.Keys.Count == 0)
            //{
            //    return;
            //}
            _menu?.PopulateBalance(amount);
        }
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not SWBuyConsoleBounderUserInterfaceState cState)
                return;

            //OrderCount = cState.Count;
            //BankBalance = 
            //AccountName = cState.Name;

            if (_menu == null)
                return;
            _menu.CurrencyPrototypeIds = cState.Currencies;
            _menu.ProductCatalogue = cState.Products.ToHashSet();
            if (_orderMenu != null)
            {
                _orderMenu.Amount.IsValid = n => n >= 1 && n <= cState.MaxOrderAmount;
            }

            Populate();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            _menu?.Dispose();
            _orderMenu?.Dispose();
        }

        private void TryPurchase()
        {
            if (_product != null)
            {
                SendMessage(new SWTryPurchaseMessage(_product.ID,
                    _orderMenu?.Amount.Value ?? 0));
            }
        }
    }
}
