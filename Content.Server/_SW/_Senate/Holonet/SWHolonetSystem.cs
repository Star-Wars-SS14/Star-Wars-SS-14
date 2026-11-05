using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.PressVideo;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared.Access.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW._Senate.Holonet;

/// <summary>
/// Round-scoped Holonet portal. A personal mailbox belongs to the Mind, so it survives a body change and
/// is not inherited by a new character. Actor, device and rights are resolved again for every request and
/// every state; views go to one session only because PDA interface states are shared and retained.
/// </summary>
public sealed partial class SWHolonetSystem : EntitySystem
{
    [Dependency] private readonly SWSenatePressSystem _press = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedIdCardSystem _idCards = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly EuiManager _euis = default!;

    private const float AccessPollSeconds = 1f;
    private const int MaxRememberedRequests = 32;
    private const int OfficePrefixLength = 7; // "office:", the key format of the press office targets.

    private readonly Dictionary<(NetUserId User, EntityUid Device), Viewer> _viewers = new();
    private readonly List<(NetUserId User, EntityUid Device)> _closedViewers = new();
    private readonly Dictionary<EntityUid, Persona> _personas = new();
    private readonly Dictionary<NetUserId, SWHolonetEui> _terminals = new();
    private bool _allDirty;
    private float _accessPoll;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWHolonetTerminalComponent, ActivateInWorldEvent>(OnTerminalActivate);
        // The already mapped public screen is the existing route into the network from the world.
        SubscribeLocalEvent<SWPressViewerComponent, GetVerbsEvent<InteractionVerb>>(OnScreenVerbs);
        SubscribeLocalEvent<SWHolonetCartridgeComponent, CartridgeMessageEvent>(OnCartridgeMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        InitializeContext();
        _press.PublicationsChanged += MarkAllDirty;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _press.PublicationsChanged -= MarkAllDirty;
    }

    private void MarkAllDirty() => _allDirty = true;

    private void Reset()
    {
        // The context counter survives on purpose: numbers from the previous round must stay invalid.
        foreach (var viewer in _viewers.Values)
            Revoke(viewer);

        foreach (var terminal in _terminals.Values.ToArray())
        {
            if (!terminal.IsShutDown)
                terminal.Close();
        }

        _terminals.Clear();
        _viewers.Clear();
        _personas.Clear();
        ResetForum();
        ResetMail();
        _allDirty = false;
    }

    private void OnTerminalActivate(Entity<SWHolonetTerminalComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;
        args.Handled = OpenTerminal(ent.Owner, args.User);
    }

    private void OnScreenVerbs(Entity<SWPressViewerComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        var device = ent.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("sw-holonet-verb-open"),
            Act = () => OpenTerminal(device, user),
        });
    }

    private bool OpenTerminal(EntityUid device, EntityUid user)
    {
        if (!EntityManager.EntityExists(device) || !_interaction.InRangeUnobstructed(user, device)
            || !TryComp<ActorComponent>(user, out var player) || player.PlayerSession.AttachedEntity != user)
            return false;

        var id = player.PlayerSession.UserId;
        if (_terminals.TryGetValue(id, out var previous) && !previous.IsShutDown)
            previous.Close();

        var terminal = new SWHolonetEui(device);
        _terminals[id] = terminal;
        _euis.OpenEui(terminal, player.PlayerSession);
        return true;
    }

    public void ForgetTerminal(NetUserId user, SWHolonetEui terminal)
    {
        if (_terminals.TryGetValue(user, out var current) && current == terminal)
            _terminals.Remove(user);
    }

    public void HandleTerminal(SWHolonetEui terminal, EntityUid actor, SWHolonetRequest request)
    {
        Handle(actor, terminal.Device, null, terminal, request);
    }

    private void OnCartridgeMessage(Entity<SWHolonetCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not SWHolonetCartridgeMessage message)
            return;
        Handle(args.Actor, GetEntity(args.LoaderUid), ent.Owner, null, message.Request);
    }

    private void Handle(EntityUid actor, EntityUid device, EntityUid? cartridge, SWHolonetEui? terminal, SWHolonetRequest request)
    {
        if (!TryComp<ActorComponent>(actor, out var player))
            return;

        var session = player.PlayerSession;
        var key = (session.UserId, device);

        // A reconnect makes a new session object whose client counts epochs from the start again.
        if (_viewers.TryGetValue(key, out var viewer) && viewer.Session != session)
        {
            Revoke(viewer);
            _viewers.Remove(key);
            viewer = null;
        }

        if (viewer == null)
        {
            viewer = new Viewer(session, device);
            _viewers[key] = viewer;
        }

        if (viewer.Actor != actor)
            Revoke(viewer);

        viewer.Actor = actor;
        viewer.Cartridge = cartridge;
        viewer.Terminal = terminal;

        if (request is SWHolonetNavigateRequest navigate)
        {
            HandleNavigation(viewer, navigate);
            return;
        }

        viewer.Dirty = true;

        // Before every private action: current session, actor, Mind and device access, then the number.
        if (!TryAcceptContext(viewer, request.Context, out var who))
        {
            Acknowledge(viewer, RequestIdOf(request), false, Loc.GetString("sw-holonet-context-stale"));
            return;
        }

        switch (request)
        {
            case SWHolonetCreateThreadRequest create:
                CreateThread(viewer, who, create);
                break;
            case SWHolonetReplyRequest reply:
                Reply(viewer, who, reply);
                break;
            case SWHolonetCloseThreadRequest close:
                CloseThread(viewer, who, close);
                break;
            case SWHolonetSendMailRequest mail:
                SendMail(viewer, who, mail);
                break;
        }
    }

    /// <summary>The single authority on who is using which device right now.</summary>
    private bool TryResolve(Viewer viewer, out Who who)
    {
        who = default;
        var actor = viewer.Actor;
        if (viewer.Session.Status != SessionStatus.InGame || viewer.Session.AttachedEntity != actor
            || !_press.TryLivePressActor(actor, out var user) || user != viewer.Session.UserId
            || !_mind.TryGetMind(actor, out var mindId, out var mind) || mind.UserId != user
            || !CanUseDevice(viewer))
            return false;

        if (!_personas.TryGetValue(mindId, out var persona))
        {
            var name = SWHolonetText.TryClean(mind.CharacterName ?? Identity.Name(actor, EntityManager),
                SWHolonetLimits.MailSubject, false, out var clean) ? clean : Loc.GetString("sw-holonet-persona-default");
            persona = new Persona(mindId, ++_nextPersonaNumber, name);
            _personas[mindId] = persona;
            MarkPersonalBox(persona);
        }

        var offices = CurrentOffices(actor, user);
        string? role = offices.Count > 0 ? offices[0].Title : null;
        if (role == null && _idCards.TryFindIdCard(actor, out var card))
            role = card.Comp.LocalizedJobTitle;
        if (string.IsNullOrWhiteSpace(role))
            role = Loc.GetString("sw-holonet-role-default");

        who = new Who(actor, persona, offices, role);
        return true;
    }

    /// <summary>
    /// Every office the actor holds right now. <see cref="SWSenatePressSystem.TryGetOfficialIdentity"/> stops at
    /// the first match, so it gates access and leads the list; the rest apply the same binding and card checks.
    /// </summary>
    private IReadOnlyList<(string Key, string Title)> CurrentOffices(EntityUid actor, NetUserId user)
    {
        if (!_press.TryGetOfficialIdentity(actor, out var primaryKey, out var primaryTitle)
            || !_idCards.TryFindIdCard(actor, out var card))
            return Array.Empty<(string, string)>();

        var offices = new List<(string Key, string Title)> { (primaryKey, primaryTitle) };
        foreach (var (key, label) in _press.GetPressOfficeTargets())
        {
            if (key == primaryKey || !_press.TryGetPressOfficeBinding(key, out var boundActor, out var boundUser)
                || boundActor != actor || boundUser != user)
                continue;

            var id = key[OfficePrefixLength..];
            bool valid;
            if (id == SWSenateDilemmaSystem.RepublicOwner)
                valid = TryComp<SWSenatePositionCardComponent>(card, out var position) && position.Position == SWSenatePosition.Chancellor;
            else if (_senate.SectorHolders.ContainsKey(id))
                valid = TryComp<SWSenateSectorComponent>(card, out var sector) && sector.Sector == id;
            else
                valid = _ministry.IsActiveHolder(id, actor) && _ministry.GetHolderCard(id) == card.Owner;

            if (valid)
                offices.Add((key, label));
        }

        return offices;
    }

    /// <summary>Public content needs a working device and nothing about who is using it.</summary>
    private bool CanShowPublic(Viewer viewer) =>
        _time.ModeActive && viewer.Session.Status == SessionStatus.InGame
        && viewer.Session.AttachedEntity == viewer.Actor && CanUseDevice(viewer);

    private bool CanUseDevice(Viewer viewer)
    {
        if (!EntityManager.EntityExists(viewer.Device) || !IsDeviceOpen(viewer))
            return false;
        return _interaction.InRangeUnobstructed(viewer.Actor, viewer.Device);
    }

    private bool IsDeviceOpen(Viewer viewer)
    {
        if (viewer.Cartridge is not { } cartridge)
            return viewer.Terminal is { IsShutDown: false };

        return TryComp<CartridgeLoaderComponent>(viewer.Device, out var loader)
            && loader.ActiveProgram == cartridge
            && _ui.IsUiOpen(viewer.Device, loader.UiKey, viewer.Actor);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_viewers.Count == 0)
        {
            _allDirty = false;
            return;
        }

        // Identity, office, ID card and life state change in many unrelated systems, so they are polled.
        _accessPoll += frameTime;
        var poll = _accessPoll >= AccessPollSeconds;
        if (poll)
            _accessPoll = 0f;

        _closedViewers.Clear();
        foreach (var (key, viewer) in _viewers)
        {
            if (viewer.Session.Status == SessionStatus.Disconnected
                || poll && (!EntityManager.EntityExists(viewer.Device) || !IsDeviceOpen(viewer)))
            {
                Revoke(viewer);
                _closedViewers.Add(key);
                continue;
            }

            // A terminal window belongs to the session and survives a body change; a PDA interface does not.
            if (viewer.Terminal != null && viewer.Session.AttachedEntity is { } attached && attached != viewer.Actor)
            {
                Revoke(viewer);
                viewer.Actor = attached;
                viewer.Dirty = true;
            }

            if (!viewer.Dirty && !_allDirty && !poll)
                continue;

            // Any transfer has finished by now, so this is the safe place to bind a new context.
            var resolved = TryResolve(viewer, out var who);
            if (resolved)
                BindContext(viewer, who);
            else
                Revoke(viewer);

            var isPublic = !resolved && CanShowPublic(viewer);
            var fingerprint = resolved
                ? $"{viewer.ContextId}|{string.Join(",", who.Offices.Select(o => o.Key))}|{who.Role}"
                : isPublic ? "public" : "denied";
            if (!viewer.Dirty && !_allDirty && fingerprint == viewer.Fingerprint)
                continue;

            viewer.Fingerprint = fingerprint;
            viewer.Dirty = false;
            var state = resolved
                ? BuildState(viewer, who)
                : isPublic ? BuildState(viewer, null) : BuildDenied(viewer);
            RaiseNetworkEvent(state, viewer.Session);
            viewer.Ack = Guid.Empty;
            viewer.AckOk = false;
            viewer.Notice = string.Empty;
        }

        _allDirty = false;
        foreach (var key in _closedViewers)
            _viewers.Remove(key);
    }

    private static void Acknowledge(Viewer viewer, Guid request, bool ok, string notice)
    {
        viewer.Ack = request;
        viewer.AckOk = ok;
        viewer.Notice = notice;
    }

    /// <summary>A retried request whose first attempt succeeded is confirmed again and not repeated.</summary>
    private static bool IsRepeat(Viewer viewer, Persona persona, Guid request, out bool invalid)
    {
        invalid = request == Guid.Empty;
        if (invalid)
        {
            viewer.Notice = Loc.GetString("sw-holonet-request-invalid");
            return false;
        }

        if (!persona.DoneRequests.Contains(request))
            return false;

        Acknowledge(viewer, request, true, Loc.GetString("sw-holonet-request-repeat"));
        return true;
    }

    private static void Remember(Persona persona, Guid request)
    {
        persona.DoneRequests.Add(request);
        persona.DoneOrder.Enqueue(request);
        while (persona.DoneOrder.Count > MaxRememberedRequests)
            persona.DoneRequests.Remove(persona.DoneOrder.Dequeue());
    }

    private string Stamp() => Loc.GetString("sw-holonet-stamp", ("day", _time.Day), ("time", _time.TimeString()));

    private sealed class Viewer(ICommonSession session, EntityUid device)
    {
        public ICommonSession Session = session;
        public readonly EntityUid Device = device;
        public EntityUid Actor;
        public EntityUid? Cartridge;
        public SWHolonetEui? Terminal;

        /// <summary>Zero means there is no private context; private data and actions are then unavailable.</summary>
        public ulong ContextId;
        public EntityUid BoundActor = EntityUid.Invalid;
        public EntityUid? BoundMind;

        /// <summary>Echoed to the client so that it can tell answers to its current view from older ones.</summary>
        public ulong Epoch;
        public bool Dirty;
        public string Fingerprint = string.Empty;
        public Guid Ack;
        public bool AckOk;
        public string Notice = string.Empty;
        public SWHolonetNavigateRequest View = new();
    }

    private sealed class Persona(EntityUid mind, int number, string name)
    {
        public readonly EntityUid Mind = mind;
        public readonly string BoxKey = "p:" + number;

        /// <summary>
        /// Sent to the owner to tell characters apart. Random, so it says nothing about the Mind and can not
        /// repeat for another character after a restart of the round or of the server.
        /// </summary>
        public readonly string Key = Guid.NewGuid().ToString("N");
        public readonly string Name = name;
        public readonly HashSet<Guid> DoneRequests = new();
        public readonly Queue<Guid> DoneOrder = new();
        public TimeSpan NextThread;
        public TimeSpan NextPost;
        public TimeSpan NextMail;
        public int ThreadCount;
        public int PostCount;
        public int MailCount;
    }

    private readonly record struct Who(EntityUid Actor, Persona Persona, IReadOnlyList<(string Key, string Title)> Offices, string Role);
}
