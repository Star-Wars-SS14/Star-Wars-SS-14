using Content.Shared.Access.Systems;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.IDCard
{
    public sealed class SWCivilianIDCardBoundUserInterface : BoundUserInterface
    {
        private SWAgentIDCardWindow? _window;

        public SWCivilianIDCardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<SWAgentIDCardWindow>();

            _window.OnNameChanged += OnNameChanged;
        }

        private void OnNameChanged(string newName)
        {
            SendMessage(new AgentIDCardNameChangedMessage(newName));
        }

        /// <summary>
        /// Update the UI state based on server-sent info
        /// </summary>
        /// <param name="state"></param>
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            if (_window == null || state is not AgentIDCardBoundUserInterfaceState cast)
                return;

            _window.SetCurrentName(cast.CurrentName);
        }
    }
}
