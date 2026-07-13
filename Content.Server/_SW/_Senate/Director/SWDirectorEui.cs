using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._SW._Senate.Director;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._SW._Senate.Director;

public sealed class SWDirectorEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IEntityManager _entity = default!;

    private SWDirectorSystem Director => _entity.System<SWDirectorSystem>();

    public SWDirectorEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        Director.Updated += OnDirectorUpdated;
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        Director.Updated -= OnDirectorUpdated;
    }

    private void OnDirectorUpdated()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return Director.BuildEuiState(Player.UserId);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
            return;

        switch (msg)
        {
            case SWDirectorSetModeMessage setMode:
                Director.SetMode(setMode.Mode, Player.UserId, Player.Name);
                break;

            case SWDirectorActionMessage action:
                if (Director.Mode != SWDirectorMode.Manual || Director.Controller != Player.UserId)
                    break;
                Director.TryAction(action.Kind, action.SectorId, out _);
                break;
        }

        StateDirty();
    }
}
