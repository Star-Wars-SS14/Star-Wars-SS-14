using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

// Political parties: senator-only blocs spanning sectors. A party forms with a leader; it counts as
// "formed" at MinMembers. Membership, leadership and leadership-change votes are all keyed by sectorId
// (a senator == their claimed sector), so a senator who vacates their seat automatically leaves.
public sealed class SWSenatePartySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private const float TickInterval = 1f;
    private const int MinMembers = 3;
    // Once formed, dropping below MinMembers gives this much game-time to recruit before reverting.
    private const float GraceGameMinutes = 120f; // 2 game hours
    private const int NameLimit = 48;
    private const int DescLimit = 240;

    private static readonly Color[] Palette =
    {
        Color.FromHex("#C0392B"), Color.FromHex("#2980B9"), Color.FromHex("#27AE60"), Color.FromHex("#8E44AD"),
        Color.FromHex("#D35400"), Color.FromHex("#16A085"), Color.FromHex("#F39C12"), Color.FromHex("#E84393"),
    };

    private readonly List<SWSenateParty> _parties = new();
    private int _nextId = 1;
    private float _accum;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _parties.Clear();
        _nextId = 1;
    }

    private SWSenateParty? Get(int id) => _parties.FirstOrDefault(p => p.Id == id);

    private SWSenateParty? MemberOf(string sectorId) => _parties.FirstOrDefault(p => p.Members.Contains(sectorId));

    private SWSenateParty? PendingOf(string sectorId) => _parties.FirstOrDefault(p => p.Pending.Contains(sectorId));

    public int GetPartyIdOf(string? sectorId) =>
        sectorId != null && MemberOf(sectorId) is { } party ? party.Id : -1;

    public bool CreateParty(string sectorId, string name, string description)
    {
        if (!_senate.IsClaimed(sectorId) || MemberOf(sectorId) != null)
            return false;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var p in _parties)
            p.Pending.Remove(sectorId);

        var id = _nextId++;
        var party = new SWSenateParty
        {
            Id = id,
            Name = Clamp(name, NameLimit),
            Description = Clamp(description.Trim(), DescLimit),
            Color = Palette[(id - 1) % Palette.Length],
            Leader = sectorId,
        };
        party.Members.Add(sectorId);
        _parties.Add(party);
        return true;
    }

    public bool RequestJoin(string sectorId, int partyId)
    {
        if (!_senate.IsClaimed(sectorId) || MemberOf(sectorId) != null || PendingOf(sectorId) != null)
            return false;

        var party = Get(partyId);
        if (party == null)
            return false;

        party.Pending.Add(sectorId);
        return true;
    }

    public bool CancelJoin(string sectorId, int partyId)
    {
        var party = Get(partyId);
        return party != null && party.Pending.Remove(sectorId);
    }

    public bool ConfirmJoin(string leaderSector, int partyId, string sectorId)
    {
        var party = Get(partyId);
        if (party == null || party.Leader != leaderSector || !party.Pending.Remove(sectorId))
            return false;

        // The applicant may have vacated their seat or joined elsewhere while waiting.
        if (!_senate.IsClaimed(sectorId) || MemberOf(sectorId) != null)
            return false;

        party.Members.Add(sectorId);
        return true;
    }

    public bool RejectJoin(string leaderSector, int partyId, string sectorId)
    {
        var party = Get(partyId);
        return party != null && party.Leader == leaderSector && party.Pending.Remove(sectorId);
    }

    public bool Leave(string sectorId, int partyId)
    {
        var party = Get(partyId);
        if (party == null || !party.Members.Contains(sectorId))
            return false;

        RemoveMember(party, sectorId);
        return true;
    }

    public bool Disband(string sectorId, int partyId)
    {
        var party = Get(partyId);
        if (party == null || party.Leader != sectorId)
            return false;

        _parties.Remove(party);
        return true;
    }

    public bool InitiateVote(string sectorId, int partyId)
    {
        var party = Get(partyId);
        if (party == null || !party.Members.Contains(sectorId) || party.Leader == sectorId || party.VoteTarget != null)
            return false;

        party.VoteKind = SWPartyVoteKind.Leader;
        party.VoteTarget = sectorId;
        party.Ballots.Clear();
        party.Ballots[sectorId] = true; // the initiator backs themselves
        EvaluateVote(party);
        return true;
    }

    public bool InitiateEvict(string leaderSector, int partyId, string targetSector)
    {
        var party = Get(partyId);
        if (party == null
            || party.Leader != leaderSector
            || party.VoteTarget != null
            || targetSector == leaderSector
            || !party.Members.Contains(targetSector))
            return false;

        party.VoteKind = SWPartyVoteKind.Evict;
        party.VoteTarget = targetSector;
        party.Ballots.Clear();
        party.Ballots[leaderSector] = true; // the leader pushes the eviction
        party.Ballots[targetSector] = false; // the targeted member opposes by default
        EvaluateVote(party);
        return true;
    }

    public bool CastVote(string sectorId, int partyId, bool support)
    {
        var party = Get(partyId);
        if (party == null || party.VoteTarget == null || !party.Members.Contains(sectorId))
            return false;

        party.Ballots[sectorId] = support;
        EvaluateVote(party);
        return true;
    }

    private void EvaluateVote(SWSenateParty party)
    {
        if (party.VoteTarget == null)
            return;

        var needed = party.Members.Count / 2 + 1; // strict majority
        var yes = party.Ballots.Count(b => party.Members.Contains(b.Key) && b.Value);
        var no = party.Ballots.Count(b => party.Members.Contains(b.Key) && !b.Value);
        var allVoted = party.Members.All(party.Ballots.ContainsKey);

        if (yes >= needed)
        {
            var target = party.VoteTarget;
            var kind = party.VoteKind;
            ClearVote(party);
            if (kind == SWPartyVoteKind.Leader)
                party.Leader = target;
            else
                RemoveMember(party, target); // also handles leader handoff / disband edge cases
        }
        else if (no >= needed || allVoted)
        {
            // Majority can no longer be reached (or everyone has voted and it fell short) — abandon.
            ClearVote(party);
        }
    }

    private static void ClearVote(SWSenateParty party)
    {
        party.VoteTarget = null;
        party.Ballots.Clear();
    }

    private void RemoveMember(SWSenateParty party, string sectorId)
    {
        party.Members.Remove(sectorId);
        party.Pending.Remove(sectorId);
        party.Ballots.Remove(sectorId);
        if (party.VoteTarget == sectorId)
            ClearVote(party);

        if (party.Members.Count == 0)
        {
            _parties.Remove(party);
            return;
        }

        // Leader gone -> hand off to the oldest remaining member (else the party would already be empty).
        if (party.Leader == sectorId)
            party.Leader = party.Members[0];

        EvaluateVote(party);
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;
        var gameMinutes = _time.DayActive ? dt * (60f / SWSenateTimeSystem.RealSecondsPerGameHour) : 0f;

        for (var i = _parties.Count - 1; i >= 0; i--)
        {
            var party = _parties[i];

            // Drop members who no longer hold a sector (resigned / left office).
            for (var m = party.Members.Count - 1; m >= 0; m--)
            {
                if (!_senate.IsClaimed(party.Members[m]))
                    RemoveMember(party, party.Members[m]);
            }

            if (!_parties.Contains(party))
                continue;

            party.Pending.RemoveAll(s => !_senate.IsClaimed(s) || MemberOf(s) != null);

            if (party.Members.Count >= MinMembers)
            {
                party.Formed = true;
                party.GraceLeft = 0f;
            }
            else if (party.Formed)
            {
                // Was a real bloc, slipped below the threshold: count down the recruitment grace.
                if (party.GraceLeft <= 0f)
                    party.GraceLeft = GraceGameMinutes;
                else
                {
                    party.GraceLeft -= gameMinutes;
                    if (party.GraceLeft <= 0f)
                    {
                        party.Formed = false;
                        party.GraceLeft = 0f;
                    }
                }
            }
        }
    }

    // Bloc tag for the vote console — only formed parties read as voting blocs.
    public (string Name, Color Color)? GetBlocTag(string sectorId)
    {
        var party = MemberOf(sectorId);
        return party is { Formed: true } ? (party.Name, party.Color) : null;
    }

    public List<SWPartyEntry> BuildParties(string? viewerSector)
    {
        var viewerParty = viewerSector != null ? MemberOf(viewerSector) : null;
        var viewerPending = viewerSector != null ? PendingOf(viewerSector) : null;

        var list = new List<SWPartyEntry>();
        foreach (var party in _parties)
        {
            var isMine = viewerParty == party;
            var isLeader = isMine && party.Leader == viewerSector;

            var entry = new SWPartyEntry
            {
                Id = party.Id,
                Name = party.Name,
                Description = party.Description,
                Color = party.Color,
                Formed = party.Formed,
                StatusLabel = StatusLabel(party),
                GraceWarning = GraceWarning(party),
                LeaderName = HolderName(party.Leader),
                MemberCount = party.Members.Count,
                IsMine = isMine,
                ViewerIsLeader = isLeader,
                CanRequestJoin = viewerSector != null && viewerParty == null && viewerPending == null,
                JoinPending = viewerPending == party,
                ViewerCanLeave = isMine,
                ViewerCanDisband = isLeader,
                ViewerCanInitiateVote = isMine && !isLeader && party.VoteTarget == null,
                CanManageMembers = isLeader && party.VoteTarget == null,
            };

            foreach (var sectorId in party.Members)
            {
                entry.Members.Add(new SWPartyMemberEntry
                {
                    SectorId = sectorId,
                    SectorName = SectorName(sectorId),
                    Holder = HolderName(sectorId),
                    Color = SectorColor(sectorId),
                    IsLeader = sectorId == party.Leader,
                });
            }

            if (isLeader)
            {
                foreach (var sectorId in party.Pending)
                {
                    entry.Pending.Add(new SWPartyPendingEntry
                    {
                        SectorId = sectorId,
                        SectorName = SectorName(sectorId),
                        Holder = HolderName(sectorId),
                    });
                }
            }

            if (party.VoteTarget != null)
            {
                entry.VoteActive = true;
                entry.VoteIsEvict = party.VoteKind == SWPartyVoteKind.Evict;
                entry.VoteTargetName = HolderName(party.VoteTarget);
                entry.VoteNeeded = party.Members.Count / 2 + 1;
                entry.VoteYes = party.Ballots.Count(b => party.Members.Contains(b.Key) && b.Value);
                entry.VoteNo = party.Ballots.Count(b => party.Members.Contains(b.Key) && !b.Value);
                entry.ViewerVoted = isMine && viewerSector != null && party.Ballots.ContainsKey(viewerSector);
                entry.ViewerCanCastVote = isMine && !entry.ViewerVoted;
            }

            list.Add(entry);
        }

        return list;
    }

    private string StatusLabel(SWSenateParty party)
    {
        if (party.Formed)
            return party.GraceLeft > 0f ? "Сформирована (под угрозой)" : "Сформирована";

        var need = MinMembers - party.Members.Count;
        return $"На формировании (нужно ещё {need})";
    }

    private string GraceWarning(SWSenateParty party)
    {
        if (!party.Formed || party.GraceLeft <= 0f)
            return string.Empty;

        var hours = party.GraceLeft / 60f;
        return $"Меньше {MinMembers} членов — найдите ещё одного примерно за {hours:0.0} ч, иначе партия вернётся в статус формирования.";
    }

    private string HolderName(string sectorId) => _senate.GetSectorHolder(sectorId) ?? SectorName(sectorId);

    private string SectorName(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Name : sectorId;

    private Color SectorColor(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Color : Color.White;

    private static string Clamp(string value, int limit) => value.Length > limit ? value[..limit] : value;
}

public sealed class SWSenateParty
{
    public int Id;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public Color Color;
    public string Leader = string.Empty;
    public readonly List<string> Members = new();
    public readonly List<string> Pending = new();
    public bool Formed;
    public float GraceLeft;
    public string? VoteTarget;
    public SWPartyVoteKind VoteKind;
    public readonly Dictionary<string, bool> Ballots = new();
}

public enum SWPartyVoteKind
{
    Leader,
    Evict,
}
