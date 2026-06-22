using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Dilemmas;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate;

public sealed class SWSenateDilemmaSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    public const string RepublicOwner = "republic";

    private const int MorningDeadline = 14;
    private const int MorningApply = 15;
    private const int AfternoonDeadline = 20;
    private const int AfternoonApply = 20;
    private const float UpdateInterval = 1f;

    private readonly List<SWActiveDilemma> _dilemmas = new();
    private readonly HashSet<string> _generated = new();
    private int _nextId = 1;
    private int _day = -1;
    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        if (_time.Day != _day)
        {
            _day = _time.Day;
            _dilemmas.Clear();
            _generated.Clear();
            _nextId = 1;
        }

        ApplyDue();
        EnsureGeneration();
    }

    private SWDilemmaWindow? CurrentGenWindow()
    {
        if (!_time.DayActive)
            return null;

        var hour = _time.CurrentHour;
        if (hour >= SWSenateTimeSystem.StartHour && hour <= 13)
            return SWDilemmaWindow.Morning;
        if (hour is >= 15 and <= 19)
            return SWDilemmaWindow.Afternoon;

        return null;
    }

    private void EnsureGeneration()
    {
        var window = CurrentGenWindow();
        if (window == null)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            GenerateFor(sectorId, SWDilemmaScope.Sectoral, window.Value, sectorId);

        if (_senate.Chancellor != null)
            GenerateFor(RepublicOwner, SWDilemmaScope.Republic, window.Value, null);
    }

    private void GenerateFor(string owner, SWDilemmaScope scope, SWDilemmaWindow window, string? sectorId)
    {
        var key = $"{owner}|{window}";
        if (!_generated.Add(key))
            return;

        var pool = _proto.EnumeratePrototypes<SWDilemmaPrototype>()
            .Where(d => d.Scope == scope && string.IsNullOrEmpty(d.Trigger))
            .ToList();

        if (pool.Count == 0)
            return;

        var count = Math.Min(_random.Next(1, 3), pool.Count);
        _random.Shuffle(pool);

        var deadline = window == SWDilemmaWindow.Morning ? MorningDeadline : AfternoonDeadline;
        var apply = window == SWDilemmaWindow.Morning ? MorningApply : AfternoonApply;

        for (var i = 0; i < count; i++)
        {
            _dilemmas.Add(new SWActiveDilemma
            {
                Id = _nextId++,
                DilemmaId = pool[i].ID,
                Scope = scope,
                Owner = owner,
                SectorId = sectorId,
                Window = window,
                DeadlineHour = deadline,
                ApplyHour = apply,
                Status = SWDilemmaStatus.Pending,
            });
        }
    }

    private void ApplyDue()
    {
        foreach (var dilemma in _dilemmas)
        {
            if (dilemma.Status is not (SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen))
                continue;

            var due = !_time.DayActive || _time.CurrentHour >= dilemma.ApplyHour;
            if (due)
                ApplyDilemma(dilemma);
        }
    }

    private void ApplyDilemma(SWActiveDilemma dilemma)
    {
        if (!_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto))
        {
            dilemma.Status = SWDilemmaStatus.Applied;
            return;
        }

        if (dilemma.Status == SWDilemmaStatus.Chosen
            && dilemma.ChosenOption >= 0
            && dilemma.ChosenOption < proto.Options.Count)
        {
            var option = proto.Options[dilemma.ChosenOption];
            Apply(dilemma, option.PolicyEffects, option.SectorEffects);
            dilemma.Status = SWDilemmaStatus.Applied;
        }
        else
        {
            Apply(dilemma, proto.FailPolicyEffects, proto.FailSectorEffects);
            dilemma.Status = SWDilemmaStatus.Failed;
        }
    }

    private void Apply(
        SWActiveDilemma dilemma,
        Dictionary<ProtoId<SWStatPrototype>, float> policy,
        Dictionary<ProtoId<SWGlobalStatPrototype>, float> sector)
    {
        if (dilemma.Scope == SWDilemmaScope.Republic)
        {
            foreach (var (sub, delta) in policy)
                _senate.AddPolicy(sub, delta);
        }
        else if (dilemma.SectorId != null)
        {
            foreach (var (global, delta) in sector)
                _senate.AddSectorBias(dilemma.SectorId, global, delta);
        }
    }

    public bool Resolve(int id, int option, string owner)
    {
        var dilemma = _dilemmas.FirstOrDefault(d => d.Id == id);
        if (dilemma == null || dilemma.Owner != owner || dilemma.Status != SWDilemmaStatus.Pending)
            return false;

        if (!_time.DayActive || _time.CurrentHour >= dilemma.DeadlineHour)
            return false;

        if (!_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto)
            || option < 0
            || option >= proto.Options.Count)
            return false;

        dilemma.ChosenOption = option;
        dilemma.Status = SWDilemmaStatus.Chosen;
        return true;
    }

    public List<SWDilemmaEntry> BuildOwnerDilemmas(string? owner)
    {
        var list = new List<SWDilemmaEntry>();
        if (owner == null)
            return list;

        foreach (var dilemma in _dilemmas)
        {
            if (dilemma.Owner != owner)
                continue;
            if (dilemma.Status is not (SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen))
                continue;

            list.Add(BuildEntry(dilemma, owner));
        }

        return list;
    }

    public List<SWDilemmaEntry> BuildAllDilemmas()
    {
        var list = new List<SWDilemmaEntry>();
        foreach (var dilemma in _dilemmas)
            list.Add(BuildEntry(dilemma, null));

        list.Sort((a, b) =>
        {
            if (a.IsRepublic != b.IsRepublic)
                return a.IsRepublic ? -1 : 1;
            var owner = string.Compare(a.OwnerLabel, b.OwnerLabel, StringComparison.Ordinal);
            return owner != 0 ? owner : a.Id.CompareTo(b.Id);
        });

        return list;
    }

    private SWDilemmaEntry BuildEntry(SWActiveDilemma dilemma, string? viewer)
    {
        _proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto);

        var entry = new SWDilemmaEntry
        {
            Id = dilemma.Id,
            Name = proto?.Name ?? dilemma.DilemmaId,
            Description = proto?.Description ?? string.Empty,
            IsRepublic = dilemma.Scope == SWDilemmaScope.Republic,
            OwnerLabel = dilemma.Scope == SWDilemmaScope.Republic ? "Канцлер" : SectorName(dilemma.SectorId),
            FailText = proto?.FailText ?? string.Empty,
            Status = StatusLabel(dilemma.Status),
            ChosenOption = dilemma.ChosenOption,
            CanResolve = viewer != null
                && dilemma.Owner == viewer
                && dilemma.Status == SWDilemmaStatus.Pending
                && _time.DayActive
                && _time.CurrentHour < dilemma.DeadlineHour,
            TimeLabel = dilemma.Status is SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen
                ? $"Решить до {dilemma.DeadlineHour:00}:00"
                : StatusLabel(dilemma.Status),
        };

        if (proto != null)
        {
            foreach (var option in proto.Options)
            {
                entry.Options.Add(new SWDilemmaOptionEntry
                {
                    Label = option.Label,
                    Description = option.Description,
                    Effects = dilemma.Scope == SWDilemmaScope.Republic
                        ? PolicyEffectEntries(option.PolicyEffects)
                        : SectorEffectEntries(option.SectorEffects),
                });
            }

            entry.FailEffects = dilemma.Scope == SWDilemmaScope.Republic
                ? PolicyEffectEntries(proto.FailPolicyEffects)
                : SectorEffectEntries(proto.FailSectorEffects);
        }

        return entry;
    }

    private string SectorName(string? sectorId)
    {
        if (sectorId != null && _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
            return proto.Name;
        return sectorId ?? "—";
    }

    private static string StatusLabel(SWDilemmaStatus status) => status switch
    {
        SWDilemmaStatus.Pending => "Ожидает решения",
        SWDilemmaStatus.Chosen => "Решение принято",
        SWDilemmaStatus.Applied => "Применено",
        SWDilemmaStatus.Failed => "Провалено",
        _ => "—",
    };

    private List<SWBillEffectEntry> SectorEffectEntries(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta });
        }

        return list;
    }

    private List<SWBillEffectEntry> PolicyEffectEntries(Dictionary<ProtoId<SWStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!effects.TryGetValue(sub.ID, out var delta))
                continue;

            var color = _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;
            list.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = color, Delta = delta });
        }

        return list;
    }
}

public sealed class SWActiveDilemma
{
    public int Id;
    public string DilemmaId = string.Empty;
    public SWDilemmaScope Scope;
    public string Owner = string.Empty;
    public string? SectorId;
    public SWDilemmaWindow Window;
    public int DeadlineHour;
    public int ApplyHour;
    public SWDilemmaStatus Status;
    public int ChosenOption = -1;
}
