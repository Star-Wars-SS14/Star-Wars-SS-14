using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Content.Shared._SW.Artillery;
using Content.Shared.Communications;
using Content.Shared.Popups;

namespace Content.Client._SW.Artillery.UI
{
    public sealed class ArtilleryConsoleBoundUserInterface : BoundUserInterface
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        [ViewVariables]
        private ArtilleryConsoleMenu? _menu;
        [ViewVariables]
        public bool CountdownStarted { get; private set; }

        public TimeSpan? CountDownLeft;

        public int AttackLeft;

        public ArtilleryConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
           
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not ArtilleryConsoleInterfaceState artState)
                return;
            CountDownLeft = artState.CountDownLeft;
            AttackLeft = artState.AttackLeft;
            if (_menu != null)
            {
                _menu.UpdateCountdown();
                _menu.ArtilleryButton.Disabled = !artState.CanCall;
            }
        }

        protected override void Open()
        {
            base.Open();

            _menu = new ArtilleryConsoleMenu(this);
            _menu.OnClose += Close;
            _menu.OpenCentered();
        }

        public void CallArtileryButtonPressed(string xInString, string yInString)
        {
            int x, y;
            if (!Int32.TryParse(xInString, out x) || !Int32.TryParse(yInString, out y))
            {
                EntMan.System<SharedPopupSystem>().PopupCursor(Loc.GetString("sw-artillery-bad-coordinates"), PopupType.Medium);
                return;
            }
            SendMessage(new ArtilleryConsoleCallArtileryMessage(x, y));
        }

        
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            _menu?.Dispose();
        }
    }
}
