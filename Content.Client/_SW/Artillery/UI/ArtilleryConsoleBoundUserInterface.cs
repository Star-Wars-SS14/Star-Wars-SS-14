using Content.Shared._SW;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Communications;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Content.Shared._SW.Artillery;

namespace Content.Client._SW.Artillery.UI
{
    public sealed class ArtilleryConsoleBoundUserInterface : BoundUserInterface
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        [ViewVariables]
        private ArtilleryConsoleMenu? _menu;

        [ViewVariables]
        public bool CanAnnounce { get; private set; }
        [ViewVariables]
        public bool CanBroadcast { get; private set; }

        [ViewVariables]
        public bool CanCall { get; private set; }

        [ViewVariables]
        public bool CanReinforcements { get; private set; }

        [ViewVariables]
        public bool CountdownStarted { get; private set; }

        [ViewVariables]
        public bool AlertLevelSelectable { get; private set; }

        [ViewVariables]
        public string CurrentLevel { get; private set; } = default!;

        [ViewVariables]
        private TimeSpan? _expectedCountdownTime;

        public int Countdown => _expectedCountdownTime == null ? 0 : Math.Max((int) _expectedCountdownTime.Value.Subtract(_gameTiming.CurTime).TotalSeconds, 0);

        public ArtilleryConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
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
            if(!Int32.TryParse(xInString, out x) || !Int32.TryParse(yInString, out y))
            {
                return;
            }
            SendMessage(new ArtilleryConsoleCallArtileryMessage(x, y));
        }

       

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);


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
