using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Dilemmas;

namespace Content.Server._SW._Senate.Economy;

public sealed partial class SWSenateDilemmaSystem
{
    private sealed class GovernorRequest
    {
        public string Key = string.Empty;
        public string Sector = string.Empty;
        public SWActiveDilemma Dilemma = default!;
        public SWDilemmaPrototype Snapshot = default!;
        public int Credits;
        public double Expires;
    }

    private readonly Dictionary<string, GovernorRequest> _governorRequests = new();
    private readonly Dictionary<string, double> _governorCooldown = new();
    private const string GovernorPrefix = "governor:";

    private bool TryDilemma(string id, [NotNullWhen(true)] out SWDilemmaPrototype? prototype)
    {
        if (_governorRequests.TryGetValue(id, out var governor))
        {
            prototype = governor.Snapshot;
            return true;
        }
        return _proto.TryIndex<SWDilemmaPrototype>(id, out prototype);
    }

    public bool HasGovernorRequest(string problemKey) => GovernorPending(GovernorPrefix + problemKey);

    private bool GovernorPending(string id) => _governorRequests.TryGetValue(id, out var governor) &&
        governor.Dilemma.Status is SWDilemmaStatus.Pending or SWDilemmaStatus.Delegated;

    private bool GovernorSectorPending(string? sector) => sector != null &&
        _governorRequests.Values.Any(g => g.Sector == sector && GovernorPending(g.Dilemma.DilemmaId));

    /// <summary>Refresh evidence without extending the active-hour deadline or creating another payment.</summary>
    public bool UpsertGovernorRequest(string sectorId, string problemKey, string title, string description, int credits)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return false;
        ExpireGovernorRequests();
        if (credits <= 0 || credits > 10000 || string.IsNullOrWhiteSpace(problemKey) ||
            string.IsNullOrWhiteSpace(title) || !_senate.SectorHolders.ContainsKey(sectorId) || !_time.ModeActive)
            return false;
        var id = GovernorPrefix + problemKey;
        if (_governorRequests.TryGetValue(id, out var existing) && GovernorPending(id))
        {
            if (existing.Sector != sectorId)
                return false;
            existing.Snapshot.Name = title;
            existing.Snapshot.Description = description;
            existing.Credits = credits;
            existing.Snapshot.MinistryOptions[0].Cost = credits;
            return true;
        }
        if (GovernorSectorPending(sectorId) ||
            _governorCooldown.GetValueOrDefault(id) > _time.ActiveGameHours ||
            _governorCooldown.GetValueOrDefault("sector:" + sectorId) > _time.ActiveGameHours)
            return false;
        // Completed snapshots remain addressable until daily cleanup; never reuse their synthetic ID.
        if (_governorRequests.ContainsKey(id))
        {
            _dilemmas.RemoveAll(d => d.DilemmaId == id);
            _requests.RemoveAll(r => r.DilemmaId == id);
            _governorRequests.Remove(id);
        }
        var dilemma = new SWActiveDilemma
        {
            Id = _nextId++, DilemmaId = id, Scope = SWDilemmaScope.Sectoral,
            Owner = sectorId, SectorId = sectorId, Window = SWDilemmaWindow.Afternoon,
            DeadlineHour = SWSenateTimeSystem.EndHour, ApplyHour = SWSenateTimeSystem.EndHour,
            Status = SWDilemmaStatus.Pending,
        };
        var snapshot = new SWDilemmaPrototype
        {
            Name = title, Description = description, FailText = "Финансирование не выделено; проект не начнётся без реальных средств.",
            Options = new()
            {
                new() { Label = "Направить в Министерство экономики", Description = "Запросить перевод средств в бюджет округа.", Ministry = SWMinistryEconomySystem.EconomyMinistryId },
                new() { Label = "Отложить обращение", Description = "Вернуться к вопросу после шести активных часов." },
            },
            MinistryOptions = new()
            {
                new() { Label = "Выделить средства округу", Description = "Реальный перевод из бюджета министерства; проект выбирает губернатор.", Cost = credits },
            },
        };
        _governorRequests[id] = new GovernorRequest
        {
            Key = problemKey, Sector = sectorId, Dilemma = dilemma, Snapshot = snapshot,
            Credits = credits, Expires = _time.ActiveGameHours + 12,
        };
        _dilemmas.Add(dilemma);
        AddNotice(sectorId, $"Губернатор направил обращение «{title}».");
        return true;
    }

    public void CloseGovernorRequest(string problemKey)
    {
        if (_governorRequests.TryGetValue(GovernorPrefix + problemKey, out var governor) && GovernorPending(governor.Dilemma.DilemmaId))
            FinishGovernor(governor, false, "Обращение губернатора закрыто: потребность в финансировании устранена.");
    }

    private bool ResolveGovernorDilemma(SWActiveDilemma dilemma, int option)
    {
        if (!_time.ModeActive || !_time.DayActive || !GovernorPending(dilemma.DilemmaId))
            return false;
        var governor = _governorRequests[dilemma.DilemmaId];
        if (_time.ActiveGameHours >= governor.Expires)
        {
            FinishGovernor(governor, true, "Истёк срок обращения губернатора.", true);
            return false;
        }
        if (option == 0)
            return Delegate(dilemma, option, SWMinistryEconomySystem.EconomyMinistryId);
        if (option != 1)
            return false;
        dilemma.ChosenOption = option;
        FinishGovernor(governor, true, "Сенатор отложил обращение губернатора.");
        return true;
    }

    private bool FundGovernorRequest(SWMinistryRequest request, GovernorRequest governor, int option)
    {
        if (option != 0 || !_time.ModeActive || !_time.DayActive || !_budget.Approved || !GovernorPending(request.DilemmaId) ||
            request.MinistryId != SWMinistryEconomySystem.EconomyMinistryId || !_ministry.IsClaimed(request.MinistryId) ||
            !_senate.SectorHolders.ContainsKey(governor.Sector))
            return false;
        if (_time.ActiveGameHours >= governor.Expires)
        {
            FinishGovernor(governor, true, "Истёк срок обращения губернатора.", true);
            return false;
        }
        if (!SpendMinistry(request.MinistryId, governor.Credits))
            return false;
        _budget.Grant(governor.Sector, governor.Credits);
        request.MinisterOption = option;
        FinishGovernor(governor, false, $"Министерство экономики перечислило округу {governor.Credits} кр. на реальный проект.");
        return true;
    }

    private void FinishGovernor(GovernorRequest governor, bool declined, string notice, bool expired = false)
    {
        governor.Dilemma.Status = declined ? SWDilemmaStatus.Failed : SWDilemmaStatus.Applied;
        foreach (var request in _requests.Where(r => r.DilemmaId == governor.Dilemma.DilemmaId && r.Status == SWMinistryRequestStatus.Pending))
        {
            request.Status = expired ? SWMinistryRequestStatus.Expired : declined ? SWMinistryRequestStatus.Refused : SWMinistryRequestStatus.Resolved;
            request.AuditPending = false;
        }
        if (declined)
        {
            _governorCooldown[governor.Dilemma.DilemmaId] = _time.ActiveGameHours + 6;
            _governorCooldown["sector:" + governor.Sector] = _time.ActiveGameHours + 6;
        }
        AddNotice(governor.Sector, notice);
    }

    private void ExpireGovernorRequests()
    {
        foreach (var governor in _governorRequests.Values)
        {
            if (GovernorPending(governor.Dilemma.DilemmaId) && _time.ActiveGameHours >= governor.Expires)
                FinishGovernor(governor, true, "Истёк срок обращения губернатора; финансирование не выделено.", true);
        }
    }

    private string GovernorDeadline(GovernorRequest governor) =>
        $"До истечения: {Math.Max(0, governor.Expires - _time.ActiveGameHours):0.0} активных ч";
}
