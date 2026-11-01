using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared._SW._Senate;
using Content.Shared.Mobs;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWSenatePressSystem
{
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<string, PressOfficeBinding> _pressOffices = new();
    private readonly List<OfficialPressRelease> _releases = new();
    private readonly HashSet<NetUserId> _knownOfficeUsers = new();
    private int _nextReleaseId;

    private void OnPressOfficeChanged(SWSenateOfficeHolderChangedEvent ev)
    {
        _pressOffices.Remove(ev.Owner);
        if (ev.Actor is { } actor && _mind.TryGetMind(actor, out _, out var mind)
            && mind.UserId is { } user)
        {
            _pressOffices[ev.Owner] = new PressOfficeBinding(actor, user);
            _knownOfficeUsers.Add(user);
        }
    }

    public bool TryLivePressActor(EntityUid actor, out NetUserId user)
    {
        user = default;
        if (!_time.ModeActive || !TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame || player.PlayerSession.AttachedEntity != actor
            || !TryComp<MobStateComponent>(actor, out var mob) || mob.CurrentState != MobState.Alive
            || !_mind.TryGetMind(actor, out _, out _))
            return false;
        user = player.PlayerSession.UserId;
        return true;
    }

    private bool HasBinding(string office, EntityUid actor, NetUserId user) =>
        _pressOffices.TryGetValue(office, out var binding) && binding.Actor == actor && binding.User == user;

    public bool TryGetOfficialOffice(EntityUid actor, out string office) =>
        TryGetOfficialIdentity(actor, out _, out office);

    public bool TryGetOfficialIdentity(EntityUid actor, out string key, out string office)
    {
        key = string.Empty; office = string.Empty;
        if (!TryLivePressActor(actor, out var user) || !_idCards.TryFindIdCard(actor, out var card)) return false;
        if (_senate.Chancellor != null && HasBinding(SWSenateDilemmaSystem.RepublicOwner, actor, user)
            && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position == SWSenatePosition.Chancellor)
        {
            key = "office:" + SWSenateDilemmaSystem.RepublicOwner;
            office = "Верховный канцлер";
            return true;
        }
        foreach (var sector in _senate.SectorHolders.Keys)
        {
            if (!HasBinding(sector, actor, user)
                || !TryComp<SWSenateSectorComponent>(card, out var sectorCard) || sectorCard.Sector != sector
                || !_prototypes.TryIndex<SWSectorPrototype>(sector, out var proto))
                continue;
            key = "office:" + sector;
            office = $"Сенатор: {proto.Name}";
            return true;
        }
        foreach (var ministry in _ministry.Holders.Keys)
        {
            if (!HasBinding(ministry, actor, user) || !_ministry.IsActiveHolder(ministry, actor)
                || _ministry.GetHolderCard(ministry) != card.Owner
                || !_prototypes.TryIndex<SWMinistryPrototype>(ministry, out var proto))
                continue;
            key = "office:" + ministry;
            office = proto.Name;
            return true;
        }
        return false;
    }

    // True only for known office keys whose appointment still exists; null binding is inconclusive.
    public bool IsPressOfficeOccupied(string key)
    {
        if (!key.StartsWith("office:", StringComparison.Ordinal)) return false;
        var id = key[7..];
        return id == SWSenateDilemmaSystem.RepublicOwner ? _senate.Chancellor != null
            : _senate.SectorHolders.ContainsKey(id) || _ministry.Holders.ContainsKey(id);
    }

    public bool TryGetPressOfficeBinding(string key, out EntityUid actor, out NetUserId user)
    {
        actor = default; user = default;
        if (!IsPressOfficeOccupied(key) || !_pressOffices.TryGetValue(key[7..], out var binding)) return false;
        actor = binding.Actor; user = binding.User;
        return true;
    }

    public bool HasKnownPressOffice(NetUserId user) => _knownOfficeUsers.Contains(user);

    // The address book describes appointed offices, not the currently connected player list.
    public IEnumerable<(string Key, string Label)> GetPressOfficeTargets()
    {
        if (_senate.Chancellor != null)
            yield return ("office:" + SWSenateDilemmaSystem.RepublicOwner, "Верховный канцлер");
        foreach (var sector in _senate.SectorHolders.Keys)
            if (_prototypes.TryIndex<SWSectorPrototype>(sector, out var sectorPrototype))
                yield return ("office:" + sector, $"Сенатор: {sectorPrototype.Name}");
        foreach (var ministry in _ministry.Holders.Keys)
            if (_prototypes.TryIndex<SWMinistryPrototype>(ministry, out var ministryPrototype))
                yield return ("office:" + ministry, ministryPrototype.Name);
    }

    public bool HasBoundPressOffice(NetUserId user)
    {
        foreach (var (id, binding) in _pressOffices)
            if (binding.User == user && IsPressOfficeOccupied("office:" + id)) return true;
        return false;
    }

    public bool PublishRelease(EntityUid actor, string title, string body)
    {
        if (!TryGetOfficialOffice(actor, out var office) || !ValidText(title, body) || _releases.Count >= 24)
            return false;
        _releases.Add(new OfficialPressRelease(++_nextReleaseId, Identity.Name(actor, EntityManager), office, title.Trim(), body.Trim(), Timestamp()));
        PublicationsChanged?.Invoke();
        return true;
    }

    public IReadOnlyList<OfficialPressRelease> GetReleases() => _releases.ToArray();
    private sealed record PressOfficeBinding(EntityUid Actor, NetUserId User);
}

public sealed record OfficialPressRelease(int Id, string Author, string Office, string Title, string Body, string Time);
