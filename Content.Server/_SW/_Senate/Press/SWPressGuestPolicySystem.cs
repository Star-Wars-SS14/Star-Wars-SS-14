using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Planner;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Press;

/// <summary>Press invitations use confirmed role bindings and opaque server-issued correspondent keys.</summary>
public sealed class SWPressGuestPolicySystem : EntitySystem
{
    [Dependency] private readonly SWSenatePressSystem _press = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    public event Action? Changed;
    public event Action? AccessPoll;
    private float _accessElapsed;
    public void NotifyChanged() => Changed?.Invoke();
    private readonly Dictionary<string, NetUserId> _tokens = new();
    private readonly Dictionary<NetUserId, string> _userTokens = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ActorComponent, SWGuestRequestAuthorizeEvent>(OnAuthorize);
        SubscribeLocalEvent<ActorComponent, SWGuestRequestRespondAuthorizeEvent>(OnRespond);
        SubscribeLocalEvent<SWGuestRequestValidateEvent>(OnValidate);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _tokens.Clear(); _userTokens.Clear(); });
    }

    public override void Update(float frameTime)
    {
        if (AccessPoll == null) return;
        _accessElapsed += frameTime;
        if (_accessElapsed < 2f) return;
        _accessElapsed = 0f;
        AccessPoll.Invoke();
    }

    private string? Token(NetUserId user)
    {
        if (_userTokens.TryGetValue(user, out var existing)) return existing;
        if (_tokens.Count >= 128) return null;
        var token = "reporter:" + Guid.NewGuid().ToString("N");
        _tokens[token] = user;
        _userTokens[user] = token;
        return token;
    }

    private EntityUid? OnlineActor(NetUserId user)
    {
        var session = _players.Sessions.FirstOrDefault(s => s.UserId == user && s.Status == SessionStatus.InGame);
        return session?.AttachedEntity;
    }

    public IReadOnlyList<PressInviteTarget> GetTargets(EntityUid actor)
    {
        var isReporter = _press.TryGetCorrespondent(actor, out var current);
        var isOfficial = _press.TryGetOfficialOffice(actor, out _);
        if (!isReporter && !isOfficial) return Array.Empty<PressInviteTarget>();
        var targets = new List<PressInviteTarget>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } target || target == actor)
                continue;
            if (isReporter && _press.TryGetOfficialIdentity(target, out var officeKey, out var office))
                targets.Add(new PressInviteTarget(officeKey, $"{office} — {Identity.Name(target, EntityManager, actor)}"));
            else if (isOfficial && _press.TryGetCorrespondent(target, out var user) && user != current
                && Token(user) is { } token)
                targets.Add(new PressInviteTarget(token, $"Корреспондент — {Identity.Name(target, EntityManager, actor)}"));
        }
        return targets.OrderBy(t => t.Label, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<PressInviteTarget> GetMailTargets(EntityUid actor)
    {
        if (!_press.TryLivePressActor(actor, out var sender)) return Array.Empty<PressInviteTarget>();
        var isReporter = _press.TryGetCorrespondent(actor, out _);
        var targets = new List<PressInviteTarget>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.UserId == sender || session.AttachedEntity is not { } target)
                continue;
            if (_press.TryGetCorrespondent(target, out var user) && Token(user) is { } token)
                targets.Add(new PressInviteTarget(token, $"Корреспондент — {Identity.Name(target, EntityManager, actor)}"));
            else if (isReporter && _press.TryGetOfficialIdentity(target, out var officeKey, out var office))
                targets.Add(new PressInviteTarget(officeKey, $"{office} — {Identity.Name(target, EntityManager, actor)}"));
        }
        return targets.OrderBy(t => t.Label, StringComparer.Ordinal).ToArray();
    }

    public bool TryResolveMailTarget(EntityUid actor, string key, out EntityUid target, out NetUserId user)
    {
        target = default; user = default;
        if (key == null || !_press.TryLivePressActor(actor, out var sender)) return false;
        if (key.StartsWith("office:", StringComparison.Ordinal))
        {
            if (!_press.TryGetCorrespondent(actor, out _) || !_press.TryGetPressOfficeBinding(key, out target, out user)
                || !_press.TryGetOfficialOffice(target, out _)) return false;
        }
        else
        {
            if (!_tokens.TryGetValue(key, out user) || OnlineActor(user) is not { } body
                || !_press.TryGetCorrespondent(body, out var verified) || user != verified) return false;
            target = body;
        }
        return sender != user && target != actor;
    }

    private void OnAuthorize(Entity<ActorComponent> ent, ref SWGuestRequestAuthorizeEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Press) return;
        if (ev.TargetKey.StartsWith("office:", StringComparison.Ordinal)
            && _press.TryGetCorrespondent(ent.Owner, out _)
            && _press.TryGetPressOfficeBinding(ev.TargetKey, out var target, out var targetUser))
        {
            ev.Authorized = true; ev.TargetUserId = targetUser;
            if (EntityManager.EntityExists(target) && Identity.Name(target, EntityManager, ent.Owner) == Name(target)
                && Identity.Name(ent.Owner, EntityManager, target) == Name(ent.Owner))
                ev.TargetDisplayName = Name(target);
        }
        else if (ev.TargetKey.StartsWith("reporter:", StringComparison.Ordinal)
            && _press.TryGetOfficialOffice(ent.Owner, out _)
            && _tokens.TryGetValue(ev.TargetKey, out var user))
        {
            ev.Authorized = true; ev.TargetUserId = user;
            if (OnlineActor(user) is { } reporterActor && _press.TryGetCorrespondent(reporterActor, out _)
                && Identity.Name(reporterActor, EntityManager, ent.Owner) == Name(reporterActor)
                && Identity.Name(ent.Owner, EntityManager, reporterActor) == Name(ent.Owner))
                ev.TargetDisplayName = Name(reporterActor);
        }
    }

    private void OnRespond(Entity<ActorComponent> ent, ref SWGuestRequestRespondAuthorizeEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Press) return;
        if (ev.TargetKey.StartsWith("office:", StringComparison.Ordinal)
            && _press.TryGetPressOfficeBinding(ev.TargetKey, out var target, out var user)
            && target == ent.Owner && user == ev.ExpectedTargetUserId && _press.TryGetOfficialOffice(ent.Owner, out _))
            ev.Authorized = true;
        else if (_tokens.TryGetValue(ev.TargetKey, out var reporter) && reporter == ev.ExpectedTargetUserId
            && _press.TryGetCorrespondent(ent.Owner, out var actual) && actual == reporter)
            ev.Authorized = true;
    }

    private void OnValidate(ref SWGuestRequestValidateEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Press) return;
        if (ev.TargetKey.StartsWith("office:", StringComparison.Ordinal))
        {
            if (!_press.IsPressOfficeOccupied(ev.TargetKey)) { ev.Validity = SWGuestRequestValidity.Invalid; return; }
            if (!_press.TryGetPressOfficeBinding(ev.TargetKey, out _, out var target)) return;
            if (target != ev.ExpectedTargetUserId) { ev.Validity = SWGuestRequestValidity.Invalid; return; }
            if (OnlineActor(ev.OwnerUserId) is { } owner && _press.TryGetCorrespondent(owner, out var user) && user == ev.OwnerUserId)
                ev.Validity = SWGuestRequestValidity.Valid;
            return;
        }
        if (!_tokens.TryGetValue(ev.TargetKey, out var reporter) || reporter != ev.ExpectedTargetUserId)
        {
            ev.Validity = SWGuestRequestValidity.Invalid; return;
        }
        if (!_press.HasBoundPressOffice(ev.OwnerUserId))
        {
            if (_press.HasKnownPressOffice(ev.OwnerUserId)) ev.Validity = SWGuestRequestValidity.Invalid;
            return;
        }
        if (OnlineActor(ev.OwnerUserId) is not { } official || !_press.TryGetOfficialOffice(official, out _)
            || OnlineActor(reporter) is not { } targetActor || !_press.TryGetCorrespondent(targetActor, out var targetUser)
            || targetUser != reporter)
            return;
        ev.Validity = SWGuestRequestValidity.Valid;
    }
}

public sealed record PressInviteTarget(string Key, string Label);
