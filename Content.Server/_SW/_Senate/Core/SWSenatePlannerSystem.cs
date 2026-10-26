using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenatePlannerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly SWSenateCommitteeSystem _committee = default!;

    private const int MaxMeetingsPerHost = 8;
    private const int MaxBlocksPerOwner = 10;
    private const int MaxInvitees = 8;

    private readonly List<Meeting> _meetings = new();
    private readonly List<Block> _blocks = new();
    private readonly Dictionary<string, List<string>> _notices = new();
    private int _nextMeetingId = 1;
    private int _nextBlockId = 1;
    private int _day = -1;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _meetings.Clear();
        _blocks.Clear();
        _notices.Clear();
        _nextMeetingId = 1;
        _nextBlockId = 1;
        _day = -1;
    }

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        if (_time.Day == _day)
            return;

        _day = _time.Day;
        _meetings.RemoveAll(m => m.Day < _day);
        _blocks.RemoveAll(b => b.Day < _day);
    }

    private void AddNotice(string person, string text)
    {
        if (!_notices.TryGetValue(person, out var list))
        {
            list = new List<string>();
            _notices[person] = list;
        }

        list.Insert(0, $"[{_time.CurrentHour:00}:{_time.CurrentMinute:00}] {text}");
        if (list.Count > 6)
            list.RemoveRange(6, list.Count - 6);
    }

    public List<string> GetNotices(string person) =>
        _notices.TryGetValue(person, out var list) ? new List<string>(list) : new List<string>();

    private static bool Confirmed(Meeting m) =>
        m.Invites.Count == 0 || m.Invites.Values.Any(s => s == SWInviteStatus.Accepted);

    private bool ValidWindow(int day, int slot, int duration, int maxDuration)
    {
        if (day != _time.Day && day != _time.Day + 1)
            return false;
        if (duration < 1 || duration > maxDuration)
            return false;
        return slot >= 0 && slot + duration <= SWPlannerSlots.PerDay;
    }

    // --- Meetings ---

    public bool CreateMeeting(string host, int day, int slot, int duration, string title, string description,
        string location, bool priv, List<string> invitees)
    {
        title = title.Trim();
        if (title.Length == 0 || !ValidWindow(day, slot, duration, 4))
            return false;
        if (_meetings.Count(m => m.Host == host) >= MaxMeetingsPerHost)
            return false;

        var meeting = new Meeting
        {
            Id = _nextMeetingId++,
            Day = day,
            StartSlot = slot,
            Duration = duration,
            Title = title,
            Description = description.Trim(),
            Location = location.Trim(),
            Host = host,
            Private = priv,
        };

        foreach (var name in invitees.Distinct().Take(MaxInvitees))
        {
            if (name == host || string.IsNullOrWhiteSpace(name))
                continue;
            meeting.Invites[name] = SWInviteStatus.Pending;
            AddNotice(name, $"{host} приглашает: «{title}», {DayLabel(day)} {SWPlannerSlots.Range(slot, duration)}.");
        }

        _meetings.Add(meeting);
        return true;
    }

    public bool Respond(string person, int meetingId, bool accept, string reason)
    {
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId);
        if (meeting == null || !meeting.Invites.ContainsKey(person))
            return false;

        meeting.Invites[person] = accept ? SWInviteStatus.Accepted : SWInviteStatus.Declined;
        if (accept)
        {
            meeting.Reasons.Remove(person);
            AddNotice(meeting.Host, $"{person} принял приглашение «{meeting.Title}».");
        }
        else
        {
            reason = reason.Trim();
            meeting.Reasons[person] = reason;
            var tail = reason.Length > 0 ? $": {reason}" : ".";
            AddNotice(meeting.Host, $"{person} отклонил «{meeting.Title}»{tail}");

            if (meeting.Invites.Count > 0 && meeting.Invites.Values.All(s => s == SWInviteStatus.Declined))
            {
                _meetings.Remove(meeting);
                AddNotice(meeting.Host, $"Встреча «{meeting.Title}» распалась: все приглашённые отказались.");
            }
        }

        return true;
    }

    public bool Cancel(string host, int meetingId)
    {
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId && m.Host == host);
        if (meeting == null)
            return false;

        _meetings.Remove(meeting);
        foreach (var (name, _) in meeting.Invites)
            AddNotice(name, $"{host} отменил встречу «{meeting.Title}».");
        return true;
    }

    public bool Reschedule(string host, int meetingId, int day, int slot, int duration)
    {
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId && m.Host == host);
        if (meeting == null || !ValidWindow(day, slot, duration, 4))
            return false;

        meeting.Day = day;
        meeting.StartSlot = slot;
        meeting.Duration = duration;
        meeting.Reasons.Clear();
        foreach (var name in meeting.Invites.Keys.ToList())
        {
            meeting.Invites[name] = SWInviteStatus.Pending;
            AddNotice(name, $"{host} перенёс «{meeting.Title}» на {DayLabel(day)} {SWPlannerSlots.Range(slot, duration)} - подтвердите заново.");
        }

        return true;
    }

    // --- Blocks (personal schedule) ---

    public bool AddBlock(string owner, int day, int slot, int duration, string label)
    {
        label = label.Trim();
        if (label.Length == 0 || !ValidWindow(day, slot, duration, 16))
            return false;
        if (_blocks.Count(b => b.Owner == owner) >= MaxBlocksPerOwner)
            return false;

        _blocks.Add(new Block { Id = _nextBlockId++, Day = day, StartSlot = slot, Duration = duration, Owner = owner, Label = label });
        return true;
    }

    public bool RemoveBlock(string owner, int blockId) =>
        _blocks.RemoveAll(b => b.Id == blockId && b.Owner == owner) > 0;

    // --- Views ---

    private bool IsPolitical(string person) =>
        person == _senate.Chancellor || person == _senate.Speaker || _senate.SectorHolders.Values.Contains(person);

    // The Chancellor's administration: Chancellor, Speaker and sitting ministry heads (not departments/bureaus).
    private bool IsCabinet(string person)
    {
        if (person == _senate.Chancellor || person == _senate.Speaker)
            return true;

        foreach (var (ministryId, holder) in _ministry.Holders)
        {
            if (holder == person
                && _proto.TryIndex<SWMinistryPrototype>(ministryId, out var proto)
                && proto.Kind == SWMinistryKind.Ministry)
                return true;
        }

        return false;
    }

    private static readonly (int Slot, int Duration, string Label)[] Presets =
    {
        (0, 8, "Личное время"),
        (28, 4, "Обед"),
        (52, 4, "Личное время"),
    };

    public ulong BusyMask(string person, int day)
    {
        ulong mask = 0;
        foreach (var (slot, duration, _) in Presets)
            mask |= SWPlannerSlots.Mask(slot, duration);

        foreach (var block in _blocks)
        {
            if (block.Owner == person && block.Day == day)
                mask |= SWPlannerSlots.Mask(block.StartSlot, block.Duration);
        }

        foreach (var meeting in _meetings)
        {
            if (meeting.Day != day)
                continue;
            var attends = meeting.Host == person
                || (meeting.Invites.TryGetValue(person, out var status) && status == SWInviteStatus.Accepted);
            if (attends)
                mask |= SWPlannerSlots.Mask(meeting.StartSlot, meeting.Duration);
        }

        if (day == _time.Day && IsPolitical(person))
        {
            foreach (var session in _senate.Sessions)
            {
                if (session.Status != SWSessionStatus.Confirmed)
                    continue;
                mask |= SWPlannerSlots.Mask(session.StartSlot, session.SlotDuration);
            }
        }

        if (JudicialRole(person) is { } judicial)
        {
            mask |= SWPlannerSlots.Mask((SWSenateCourtSystem.MorningStart - SWSenateTimeSystem.StartHour) * 4,
                (SWSenateCourtSystem.MorningEnd - SWSenateCourtSystem.MorningStart) * 4);
            mask |= SWPlannerSlots.Mask((SWSenateCourtSystem.EveningStart - SWSenateTimeSystem.StartHour) * 4,
                (SWSenateCourtSystem.EveningEnd - SWSenateCourtSystem.EveningStart) * 4);
            if (judicial)
            {
                mask |= SWPlannerSlots.Mask((SWSenateCourtSystem.MorningEnd - SWSenateTimeSystem.StartHour) * 4, 4);
                mask |= SWPlannerSlots.Mask((SWSenateCourtSystem.EveningEnd - SWSenateTimeSystem.StartHour) * 4, 4);
            }
        }

        return mask;
    }

    private bool? JudicialRole(string person)
    {
        if (_court.IsJudge(person))
            return true;
        if (_court.Prosecutor == person || _court.Defender == person)
            return false;
        return null;
    }

    public List<SWPlannerItemEntry> BuildItems(string viewer, string person, int day)
    {
        var items = new List<SWPlannerItemEntry>();

        var cabinet = IsCabinet(person);
        foreach (var (slot, duration, label) in Presets)
        {
            if (slot == 0 && cabinet)
            {
                items.Add(new SWPlannerItemEntry { StartSlot = 0, Duration = 4, Kind = 0, Label = "Личное время" });
                items.Add(new SWPlannerItemEntry { StartSlot = 4, Duration = 4, Kind = 1, Label = "Собрание Кабинета" });
                continue;
            }

            if (slot == 0 && _committee.IsAnyMember(person))
            {
                items.Add(new SWPlannerItemEntry { StartSlot = 0, Duration = 4, Kind = 0, Label = "Личное время" });
                items.Add(new SWPlannerItemEntry { StartSlot = 4, Duration = 4, Kind = 1, Label = "Комитетский час" });
                continue;
            }

            items.Add(new SWPlannerItemEntry { StartSlot = slot, Duration = duration, Kind = 0, Label = label });
        }

        if (day == _time.Day && IsPolitical(person))
        {
            foreach (var session in _senate.Sessions)
            {
                if (session.Status != SWSessionStatus.Confirmed)
                    continue;

                items.Add(new SWPlannerItemEntry
                {
                    StartSlot = session.StartSlot,
                    Duration = session.SlotDuration,
                    Kind = 1,
                    Label = $"Заседание Сената: {_senate.GetSessionName(session)}",
                });
            }
        }

        if (JudicialRole(person) is { } judicialRole)
        {
            items.Add(new SWPlannerItemEntry
            {
                StartSlot = (SWSenateCourtSystem.MorningStart - SWSenateTimeSystem.StartHour) * 4,
                Duration = (SWSenateCourtSystem.MorningEnd - SWSenateCourtSystem.MorningStart) * 4,
                Kind = 1,
                Label = "Утреннее слушание суда",
            });
            items.Add(new SWPlannerItemEntry
            {
                StartSlot = (SWSenateCourtSystem.EveningStart - SWSenateTimeSystem.StartHour) * 4,
                Duration = (SWSenateCourtSystem.EveningEnd - SWSenateCourtSystem.EveningStart) * 4,
                Kind = 1,
                Label = "Вечернее слушание суда",
            });
            if (judicialRole)
            {
                items.Add(new SWPlannerItemEntry
                {
                    StartSlot = (SWSenateCourtSystem.MorningEnd - SWSenateTimeSystem.StartHour) * 4,
                    Duration = 4,
                    Kind = 1,
                    Label = "Совещание судей",
                });
                items.Add(new SWPlannerItemEntry
                {
                    StartSlot = (SWSenateCourtSystem.EveningEnd - SWSenateTimeSystem.StartHour) * 4,
                    Duration = 4,
                    Kind = 1,
                    Label = "Совещание судей",
                });
            }
        }

        foreach (var block in _blocks)
        {
            if (block.Owner != person || block.Day != day)
                continue;

            items.Add(new SWPlannerItemEntry
            {
                StartSlot = block.StartSlot,
                Duration = block.Duration,
                Kind = 3,
                Label = block.Label,
                BlockId = viewer == person ? block.Id : -1,
            });
        }

        foreach (var meeting in _meetings)
        {
            if (meeting.Day != day)
                continue;
            var attends = meeting.Host == person
                || (meeting.Invites.TryGetValue(person, out var status) && status == SWInviteStatus.Accepted);
            if (!attends)
                continue;

            var involved = viewer == person || meeting.Host == viewer || meeting.Invites.ContainsKey(viewer);
            if (meeting.Private && !involved)
            {
                items.Add(new SWPlannerItemEntry
                {
                    StartSlot = meeting.StartSlot,
                    Duration = meeting.Duration,
                    Kind = 4,
                    Label = "Занято",
                });
                continue;
            }

            items.Add(new SWPlannerItemEntry
            {
                StartSlot = meeting.StartSlot,
                Duration = meeting.Duration,
                Kind = 2,
                Label = $"Встреча: {meeting.Title}",
                Sub = BuildMeetingSub(meeting),
                MeetingId = meeting.Id,
                Confirmed = Confirmed(meeting),
            });
        }

        items.Sort((a, b) => a.StartSlot != b.StartSlot ? a.StartSlot.CompareTo(b.StartSlot) : a.Kind.CompareTo(b.Kind));
        return items;
    }

    private string BuildMeetingSub(Meeting meeting)
    {
        var parts = new List<string> { $"хост: {meeting.Host}" };
        if (meeting.Location.Length > 0)
            parts.Add($"место: {meeting.Location}");
        parts.Add(Confirmed(meeting) ? "подтверждена" : "ждёт ответов");
        return string.Join(", ", parts);
    }

    public List<SWPlannerMeetingEntry> BuildMeetingsFor(string person)
    {
        var list = new List<SWPlannerMeetingEntry>();

        foreach (var meeting in _meetings)
        {
            var isHost = meeting.Host == person;
            var invited = meeting.Invites.ContainsKey(person);
            if (!isHost && !invited)
                continue;

            var entry = new SWPlannerMeetingEntry
            {
                Id = meeting.Id,
                Day = meeting.Day,
                StartSlot = meeting.StartSlot,
                Duration = meeting.Duration,
                Title = meeting.Title,
                Description = meeting.Description,
                Location = meeting.Location,
                Host = meeting.Host,
                Private = meeting.Private,
                Confirmed = Confirmed(meeting),
                IsHost = isHost,
                Invited = invited,
                MyStatus = invited ? meeting.Invites[person] : SWInviteStatus.Accepted,
            };

            foreach (var (name, status) in meeting.Invites)
            {
                var line = status switch
                {
                    SWInviteStatus.Accepted => $"{name} - принял",
                    SWInviteStatus.Declined => meeting.Reasons.TryGetValue(name, out var r) && r.Length > 0
                        ? $"{name} - отказ: {r}"
                        : $"{name} - отказался",
                    _ => $"{name} - не ответил",
                };
                entry.ParticipantLines.Add(line);
            }

            if (meeting.Day == _time.Day && IsPolitical(person))
            {
                foreach (var session in _senate.Sessions)
                {
                    if (session.Status != SWSessionStatus.Confirmed)
                        continue;

                    var sessionMask = SWPlannerSlots.Mask(session.StartSlot, session.SlotDuration);
                    if ((sessionMask & SWPlannerSlots.Mask(meeting.StartSlot, meeting.Duration)) != 0)
                    {
                        entry.Warning = $"Пересекается с заседанием Сената ({SWSenateDuration.FormatClock(session.StartHour)})";
                        break;
                    }
                }
            }

            list.Add(entry);
        }

        list.Sort((a, b) => a.Day != b.Day ? a.Day.CompareTo(b.Day) : a.StartSlot.CompareTo(b.StartSlot));
        return list;
    }

    public List<SWPlannerPersonEntry> BuildRoster()
    {
        var roles = new Dictionary<string, string>();

        foreach (var (sectorId, holder) in _senate.SectorHolders)
            roles[holder] = _proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? $"Сенатор {sp.Name}" : "Сенатор";
        foreach (var (ministryId, holder) in _ministry.Holders)
        {
            if (_proto.TryIndex<SWMinistryPrototype>(ministryId, out var mp))
                roles.TryAdd(holder, mp.Name);
        }
        if (_senate.Speaker != null)
            roles[_senate.Speaker] = "Спикер Сената";
        if (_senate.Chancellor != null)
            roles[_senate.Chancellor] = "Верховный Канцлер";
        foreach (var judge in _court.BuildJudges())
            roles[judge.Name] = judge.IsChief ? "Верховный Судья" : "Республиканский Судья";
        if (_court.Prosecutor != null)
            roles.TryAdd(_court.Prosecutor, "Обвинитель");
        if (_court.Defender != null)
            roles.TryAdd(_court.Defender, "Защитник");

        foreach (var meeting in _meetings)
        {
            roles.TryAdd(meeting.Host, "Участник");
            foreach (var name in meeting.Invites.Keys)
                roles.TryAdd(name, "Участник");
        }

        foreach (var block in _blocks)
            roles.TryAdd(block.Owner, "Участник");

        var list = new List<SWPlannerPersonEntry>();
        foreach (var (name, role) in roles)
        {
            list.Add(new SWPlannerPersonEntry
            {
                Name = name,
                Role = role,
                BusyToday = BusyMask(name, _time.Day),
                BusyTomorrow = BusyMask(name, _time.Day + 1),
            });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    public string DayLabel(int day) => day == _time.Day ? "сегодня" : day == _time.Day + 1 ? "завтра" : $"день {day}";

    public int NowSlot()
    {
        if (!_time.DayActive)
            return -1;
        return Math.Clamp((_time.CurrentHour - SWSenateTimeSystem.StartHour) * 4 + _time.CurrentMinute / 15, 0, SWPlannerSlots.PerDay - 1);
    }

    private sealed class Meeting
    {
        public int Id;
        public int Day;
        public int StartSlot;
        public int Duration;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string Location = string.Empty;
        public string Host = string.Empty;
        public bool Private;
        public readonly Dictionary<string, SWInviteStatus> Invites = new();
        public readonly Dictionary<string, string> Reasons = new();
    }

    private sealed class Block
    {
        public int Id;
        public int Day;
        public int StartSlot;
        public int Duration;
        public string Owner = string.Empty;
        public string Label = string.Empty;
    }
}
