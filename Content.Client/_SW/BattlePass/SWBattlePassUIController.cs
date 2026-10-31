using System.Linq;
using System.Numerics;
using Content.Client._SW.Loadouts;
using Content.Client._SW.UserInterface;
using Content.Client.Info;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.Cosmetics;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._SW.BattlePass;

/// <summary>
/// The client side of the battle pass: the lobby button, the button after F1, the window, and wearing items.
/// </summary>
public sealed class SWBattlePassUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    private static readonly Color Ice = Color.FromHex("#9BE7FF");
    private static readonly ProtoId<StartingGearPrototype> PreviewGear = "CitizenGear";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1.5);

    [Dependency] private readonly IClientPreferencesManager _prefs = default!;
    [Dependency] private readonly JobRequirementsManager _playTime = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IStateManager _state = default!;

    private SWBattlePassWindow? _window;
    private Button? _lobbyButton;
    private SWBattlePassTopButton? _topButton;
    private SWBattlePassState? _last;
    private TimeSpan _nextSave;

    public IGameTiming Timing => _timing;

    public bool InLobby => _state.CurrentState is LobbyState;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<SWBattlePassStateEvent>(OnState);
        SubscribeNetworkEvent<SWBattlePassCaseResultEvent>(OnCaseResult);
        SubscribeNetworkEvent<SWBattlePassRefusedEvent>(OnRefused);
        SubscribeLocalEvent<SWLoadoutLockHintEvent>(OnLockHint);

        // Ownership lives in the play-time trackers, and it arrives a moment after the claim.
        _playTime.Updated += RefreshWindow;

        var gameplayLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayLoad.OnScreenLoad += OnScreenLoad;
        gameplayLoad.OnScreenUnload += OnScreenUnload;
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby is not { } lobby || FindBanner(lobby) is not { ChildCount: > 0 } banner)
            return;

        _lobbyButton = new Button { StyleBoxOverride = null };
        _lobbyButton.Label.FontColorOverride = Ice;
        _lobbyButton.OnPressed += _ => Toggle();

        // Next to the links and away from Ready, the one main button of the screen. LobbyGui.xaml stays untouched.
        banner.GetChild(0).AddChild(_lobbyButton);

        UpdateButtons();
    }

    private static LinkBanner? FindBanner(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is LinkBanner banner)
                return banner;

            if (FindBanner(child) is { } found)
                return found;
        }

        return null;
    }

    public void OnStateExited(LobbyState state)
    {
        // The lobby keeps its screen between visits, so a button left there would pile up.
        _lobbyButton?.Orphan();
        _lobbyButton = null;
    }

    private void OnScreenLoad()
    {
        if (UIManager.ActiveScreen?.GetWidget<GameTopMenuBar>() is not { } menu)
            return;

        _topButton?.Orphan();
        _topButton = new SWBattlePassTopButton();
        _topButton.OnPressed += _ => Toggle();
        menu.AddChild(_topButton);

        UpdateButtons();
    }

    private void OnScreenUnload()
    {
        _topButton?.Orphan();
        _topButton = null;
    }

    private void Toggle()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        _window = new SWBattlePassWindow(this);
        _window.OnClose += () => _window = null;
        // Above the middle, so that the hotbar and the character stay in sight.
        _window.OpenCenteredAt(new Vector2(0.5f, 0.41f));

        RefreshWindow();
        Send(new SWBattlePassRequestStateEvent(true));
    }

    private void RefreshWindow()
    {
        if (_last != null)
            _window?.UpdateState(_last, GetOwned());
    }

    private void OnState(SWBattlePassStateEvent ev, EntitySessionEventArgs args)
    {
        _last = ev.State;
        UpdateButtons();
        RefreshWindow();

        // Said here and not when drawing: a later redraw of this same state must not say it again.
        // The line asks to claim the scarf, so it has no use once the scarf is claimed.
        var gift = Loc.GetString("sw-battlepass-gift");
        if (ev.State.Claimed.Contains(SWBattlePassRules.RewardKey(1, SWBattlePassTrack.Free)))
            _window?.ClearStatus(gift);
        else if (ev.State.FirstOpenGift)
            _window?.ShowStatus(gift);
    }

    /// <summary>
    /// Tells the loadout window the way to a closed item, as this player stands now.
    /// </summary>
    private void OnLockHint(ref SWLoadoutLockHintEvent args)
    {
        if (_last is not { Season: { } seasonId } state || !_proto.TryIndex<SWBattlePassSeasonPrototype>(seasonId, out var season))
            return;

        var tier = 0;
        var paid = false;
        for (var i = 1; i <= season.Tiers.Count && tier == 0; i++)
        {
            if (Gives(season.Tiers[i - 1].Free, args.Item))
            {
                tier = i;
            }
            else if (Gives(season.Tiers[i - 1].Paid, args.Item))
            {
                tier = i;
                paid = true;
            }
        }

        if (tier == 0)
        {
            var item = args.Item;
            var inCase = season.OvertimeCase is { } caseId && _proto.Index(caseId).Entries.Any(e => e.Cosmetic == item);
            if (!inCase)
                return;

            args.Order = season.Tiers.Count + 1;
            args.Hint = FormattedMessage.FromUnformatted(Loc.GetString("sw-battlepass-lock-case"));
            return;
        }

        args.Order = tier;
        var now = SWBattlePassRules.TierOf(season, state.Xp);
        var text = !state.ClaimsOpen ? Loc.GetString("sw-battlepass-lock-ended")
            : paid && !state.Paid ? Loc.GetString("sw-battlepass-lock-paid", ("tier", tier))
            : now >= tier ? Loc.GetString("sw-battlepass-lock-claim", ("tier", tier))
            : Loc.GetString("sw-battlepass-lock-tier", ("tier", tier), ("now", now));
        args.Hint = FormattedMessage.FromUnformatted(text);
    }

    private static bool Gives(SWBattlePassReward? reward, ProtoId<LoadoutPrototype> item)
    {
        return reward != null && (reward.Cosmetic == item || reward.Also.Contains(item));
    }

    private void OnCaseResult(SWBattlePassCaseResultEvent ev, EntitySessionEventArgs args)
    {
        _window?.ShowCaseResult(ev);
    }

    private void OnRefused(SWBattlePassRefusedEvent ev, EntitySessionEventArgs args)
    {
        _window?.ShowStatus(ev.Reason);
    }

    private void UpdateButtons()
    {
        var count = CountClaimable();

        if (_lobbyButton != null)
        {
            _lobbyButton.Visible = _last is { ButtonUnlocked: true };
            _lobbyButton.Text = count > 0
                ? Loc.GetString("sw-battlepass-lobby-button-ready", ("count", count))
                : Loc.GetString("sw-battlepass-lobby-button");
        }

        _topButton?.SetClaimable(count);
    }

    private int CountClaimable()
    {
        if (_last is not { ClaimsOpen: true, Season: { } id } || !_proto.TryIndex<SWBattlePassSeasonPrototype>(id, out var season))
            return 0;

        return SWBattlePassRules.CountClaimable(season, _last.Xp, _last.Paid, _last.Claimed)
            + Math.Max(0, SWBattlePassRules.OvertimeEarned(season, _last.Xp) - _last.OvertimeClaimed);
    }

    /// <summary>
    /// The pass items this player owns. A sold item has its own way in and is not listed here.
    /// </summary>
    private HashSet<string> GetOwned()
    {
        var owned = new HashSet<string>();
        if (_player.LocalSession is not { } session)
            return owned;

        var times = _playTime.GetPlayTimes(session);
        foreach (var loadout in _proto.EnumeratePrototypes<LoadoutPrototype>())
        {
            if (loadout.Effects.OfType<SWCosmeticLoadoutEffect>().Any(e => !e.Seasonal) && SWCosmeticTrackers.IsOwned(times, loadout.ID))
                owned.Add(loadout.ID);
        }

        return owned;
    }

    private void Send(EntityEventArgs ev)
    {
        EntityManager.EntityNetManager.SendSystemNetworkMessage(ev);
    }

    public void Claim(int tier, SWBattlePassTrack track)
    {
        Send(new SWBattlePassClaimEvent(tier, track));
    }

    public void ClaimAll()
    {
        Send(new SWBattlePassClaimAllEvent());
    }

    public void ClaimOvertime()
    {
        Send(new SWBattlePassClaimOvertimeEvent());
    }

    public void OpenCase(ProtoId<SWBattlePassCasePrototype> id)
    {
        Send(new SWBattlePassOpenCaseEvent(id));
    }

    public void BuyEntry(ProtoId<SWBattlePassCasePrototype> id, int entry)
    {
        Send(new SWBattlePassBuyEntryEvent(id, entry));
    }

    public void PlayCaseSound(SoundSpecifier sound)
    {
        EntityManager.System<AudioSystem>().PlayGlobal(sound, Filter.Local(), false);
    }

    private HumanoidCharacterProfile? SelectedProfile => _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile;

    public string? GetCharacterName()
    {
        return SelectedProfile?.Name;
    }

    /// <summary>
    /// A dummy of the player's character wearing the item, for the window to show. The caller deletes it.
    /// </summary>
    public EntityUid? SpawnPreview(IReadOnlyList<ProtoId<LoadoutPrototype>> items)
    {
        if (SelectedProfile is not { } profile)
            return null;

        var lobby = UIManager.GetUIController<LobbyUIController>();
        var dummy = lobby.LoadProfileEntity(profile, null, false);
        var spawning = EntityManager.System<Content.Client.Station.StationSpawningSystem>();
        foreach (var item in items)
        {
            if (_proto.TryIndex(item, out var loadout))
                spawning.EquipStartingGear(dummy, loadout);
        }

        // Plain clothes that leave the head and the neck free, so the item is what catches the eye.
        spawning.EquipStartingGear(dummy, PreviewGear);
        return dummy;
    }

    /// <summary>
    /// Whether the species of the selected character has no slot for this item.
    /// </summary>
    public bool IsBlockedForCharacter(LoadoutPrototype loadout)
    {
        return SelectedProfile is { } profile
            && loadout.Effects.OfType<SWCosmeticLoadoutEffect>().Any(e => e.BlockedSpecies.Contains(profile.Species));
    }

    private string? GroupOf(LoadoutPrototype loadout)
    {
        foreach (var group in _proto.EnumeratePrototypes<LoadoutGroupPrototype>())
        {
            if (group.Loadouts.Contains(loadout.ID))
                return group.ID;
        }

        return null;
    }

    /// <summary>
    /// What of a reward this character would wear: their side's half of a pair. Without a job that can wear any of it,
    /// the set that goes with the reward's first item, so a uniform still shows with its own helmet.
    /// </summary>
    public List<ProtoId<LoadoutPrototype>> ItemsForCharacter(SWBattlePassReward reward)
    {
        var all = new List<ProtoId<LoadoutPrototype>>();
        if (reward.Cosmetic is { } first)
            all.Add(first);

        all.AddRange(reward.Also);
        if (all.Count < 2)
            return all;

        var job = SelectedProfile?.JobPriorities.Where(p => p.Value == JobPriority.High).Select(p => p.Key).FirstOrDefault();
        if (job != null && _proto.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.Value), out var role))
        {
            var mine = all.Where(item => InRole(item, role)).ToList();
            if (mine.Count > 0)
                return mine;
        }

        var roles = _proto.EnumeratePrototypes<RoleLoadoutPrototype>().Where(r => InRole(all[0], r)).ToList();
        return all.Where(item => item == all[0] || roles.Any(r => InRole(item, r))).ToList();
    }

    private bool InRole(ProtoId<LoadoutPrototype> item, RoleLoadoutPrototype role)
    {
        return _proto.TryIndex(item, out var loadout) && GroupOf(loadout) is { } group && role.Groups.Contains(group);
    }

    /// <summary>
    /// The jobs whose loadout offers this item, by name.
    /// </summary>
    public List<string> JobsFor(LoadoutPrototype loadout)
    {
        var names = new List<string>();
        if (GroupOf(loadout) is not { } group)
            return names;

        foreach (var job in _proto.EnumeratePrototypes<JobPrototype>())
        {
            if (job.SetPreference
                && _proto.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID), out var role)
                && role.Groups.Contains(group))
            {
                names.Add(job.LocalizedName);
            }
        }

        names.Sort(StringComparer.CurrentCulture);
        return names;
    }

    public bool IsWorn(ProtoId<LoadoutPrototype> id)
    {
        return SelectedProfile is { } profile
            && _proto.TryIndex(id, out var loadout)
            && GroupOf(loadout) is { } group
            && SWCosmeticWear.IsWorn(profile, id, group, _proto);
    }

    /// <summary>
    /// Puts an item on the selected character or takes it off, and says what happened.
    /// </summary>
    public void SetWorn(ProtoId<LoadoutPrototype> id, bool worn)
    {
        // Two saves of one character slot at once throw on the server, in code that nothing catches.
        if (_timing.RealTime < _nextSave)
        {
            _window?.ShowStatus(Loc.GetString("sw-battlepass-wear-wait"), SaveDelay);
            return;
        }

        if (SelectedProfile is not { } profile
            || !_proto.TryIndex(id, out var loadout)
            || GroupOf(loadout) is not { } group)
        {
            return;
        }

        var result = worn
            ? SWCosmeticWear.Wear(profile, id, group, _player.LocalSession, EntityManager, _proto)
            : SWCosmeticWear.TakeOff(profile, id, group, _player.LocalSession, EntityManager, _proto);

        if (result.Changed > 0)
        {
            _nextSave = _timing.RealTime + SaveDelay;
            _prefs.UpdateCharacter(result.Profile, _prefs.Preferences!.SelectedCharacterIndex);

            if (_state.CurrentState is LobbyState)
                UIManager.GetUIController<LobbyUIController>().ReloadCharacterSetup();
        }

        _window?.ShowStatus(Loc.GetString(worn ? "sw-battlepass-wear-done" : "sw-battlepass-take-off-done",
            ("count", result.Changed),
            ("kept", result.KeptOther)));
    }
}
