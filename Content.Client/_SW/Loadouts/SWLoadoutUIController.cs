using System.Linq;
using Content.Client.Lobby;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.Loadouts;

/// <summary>
/// Opens the loadout window for the selected character from outside the profile editor, as the join window does.
/// There is no save button here, so each change is saved at once and is in place when the player joins.
/// </summary>
public sealed class SWLoadoutUIController : UIController
{
    [Dependency] private readonly IClientPreferencesManager _preferences = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IDependencyCollection _collection = default!;

    private SWLoadoutWindow? _window;

    /// <summary>
    /// Whether the job has a loadout to open.
    /// </summary>
    public bool CanOpen(JobPrototype job)
    {
        return _proto.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID), out var role)
            && role.Groups.Count > 0;
    }

    /// <summary>
    /// Whether the selected character wears something other than the job's own gear in this job.
    /// </summary>
    public bool HasChoice(JobPrototype job)
    {
        return _preferences.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile
            && profile.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(job.ID), out var loadout)
            && loadout.SelectedLoadouts.Any(group =>
                _proto.TryIndex(group.Key, out var groupProto)
                && (groupProto.MinLimit == 0
                    ? group.Value.Count > 0
                    : group.Value.Count > 0 && group.Value[0].Prototype != groupProto.Loadouts[0]));
    }

    public void Open(JobPrototype job, Action? onChanged = null)
    {
        _window?.Close();

        var roleId = LoadoutSystem.GetJobPrototype(job.ID);
        if (_player.LocalSession is not { } session
            || !_preferences.ServerDataLoaded
            || _preferences.Preferences?.SelectedCharacter is not HumanoidCharacterProfile profile
            || !_proto.TryIndex<RoleLoadoutPrototype>(roleId, out var role))
        {
            return;
        }

        var slot = _preferences.Preferences.SelectedCharacterIndex;
        var loadout = profile.GetLoadoutOrDefault(roleId, session, profile.Species, EntityManager, _proto).Clone();

        _window = new SWLoadoutWindow(profile, loadout, role, job, "sw-loadout-hint-saved", session, _collection);
        _window.OnLoadoutChanged += changed =>
        {
            profile = profile.WithLoadout(changed);
            _preferences.UpdateCharacter(profile, slot);
            onChanged?.Invoke();
        };
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }
}
