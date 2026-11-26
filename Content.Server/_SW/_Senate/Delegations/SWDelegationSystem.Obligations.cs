using System.Linq;

namespace Content.Server._SW._Senate.Delegations;

public sealed partial class SWDelegationSystem
{
    private const int MaxPendingObligations = 8;
    private sealed class TaxObligation
    {
        public required string Sector;
        public required string Good;
        public required int OriginalEffective;
        public required int TargetSurcharge;
        public required double Deadline;
        public bool Finished;
        public bool Succeeded;
    }

    private readonly List<TaxObligation> _taxObligations = new();

    private int PendingCount => _taxObligations.Count(x => !x.Finished) + _healthObligations.Count(x => !x.Finished);

    private bool CanAccept(Visit visit)
    {
        if (PendingCount >= MaxPendingObligations)
            return false;
        if (visit.Health)
            return _health.PathogenActive && _health.PathogenEpisode == visit.Episode && _health.PlanetInfection(visit.Good) > 1f && _health.CanOrderAid(visit.Good);
        return _finance.TryGetCommodityTax(visit.Good, out var surcharge, out var effective)
            && surcharge == visit.OriginalSurcharge && effective == visit.OriginalEffective;
    }

    private void AddTaxObligation(Visit visit)
    {
        _taxObligations.RemoveAll(x => x.Finished);
        var obligation = new TaxObligation
        {
            Sector = visit.Sector,
            Good = visit.Good,
            OriginalEffective = visit.OriginalEffective,
            TargetSurcharge = visit.Proposal.TargetSurcharge,
            Deadline = visit.Proposal.Deadline,
        };
        visit.TaxObligation = obligation;
        _taxObligations.Add(obligation);
    }

    private void TickTaxObligations()
    {
        foreach (var obligation in _taxObligations)
        {
            if (obligation.Finished)
                continue;
            if (_elapsed >= obligation.Deadline)
            {
                obligation.Finished = true;
                Changed();
            }
            else if (_finance.TryGetCommodityTax(obligation.Good, out var surcharge, out var effective)
                && surcharge <= obligation.TargetSurcharge && effective < obligation.OriginalEffective)
            {
                obligation.Finished = true;
                obligation.Succeeded = true;
                Changed();
            }
        }
    }
}
