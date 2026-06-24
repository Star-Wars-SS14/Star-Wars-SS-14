using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Programs;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Economy;

public sealed class SWSenateProgramSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private int _day = -1;
    private readonly HashSet<string> _applied = new();

    public override void Update(float frameTime)
    {
        if (_time.Day == _day)
            return;

        _day = _time.Day;
        _applied.Clear();
    }

    private static string Key(string sectorId, string programId) => $"{sectorId}|{programId}";

    public bool IsApplied(string sectorId, string programId) => _applied.Contains(Key(sectorId, programId));

    public bool Apply(string sectorId, string programId)
    {
        if (!_proto.TryIndex<SWProgramPrototype>(programId, out var proto))
            return false;

        if (_applied.Contains(Key(sectorId, programId)))
            return false;

        if (!_budget.Spend(sectorId, proto.Cost))
            return false;

        foreach (var (global, delta) in proto.SectorEffects)
            _senate.AddSectorBias(sectorId, global, delta);

        _applied.Add(Key(sectorId, programId));
        return true;
    }

    public List<SWProgramEntry> BuildPrograms(string sectorId)
    {
        var list = new List<SWProgramEntry>();
        var available = _budget.Available(sectorId);

        foreach (var proto in _proto.EnumeratePrototypes<SWProgramPrototype>().OrderBy(p => p.Order))
        {
            list.Add(new SWProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Category = proto.Category,
                Cost = proto.Cost,
                Applied = IsApplied(sectorId, proto.ID),
                Affordable = available >= proto.Cost,
                Effects = EffectEntries(proto.SectorEffects),
            });
        }

        return list;
    }

    private List<SWBillEffectEntry> EffectEntries(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta });
        }

        return list;
    }
}
