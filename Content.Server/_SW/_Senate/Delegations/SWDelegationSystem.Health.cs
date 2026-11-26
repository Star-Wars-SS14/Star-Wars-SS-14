using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Delegations;

public sealed partial class SWDelegationSystem
{
    private sealed class HealthObligation
    {
        public required string World;
        public required int Episode;
        public required double Deadline;
        public readonly HashSet<string> Orders = new();
        public bool Finished;
        public bool Succeeded;
        public bool EpidemicEnded;
    }

    private readonly List<HealthObligation> _healthObligations = new();

    private void AddHealthObligation(Visit visit)
    {
        _healthObligations.RemoveAll(x => x.Finished);
        var obligation = new HealthObligation
        {
            World = visit.Good,
            Episode = visit.Episode,
            Deadline = visit.Proposal.Deadline,
        };
        visit.HealthObligation = obligation;
        _healthObligations.Add(obligation);
    }

    private void OnAidOrdered(SWHealthAidOrderedEvent ev)
    {
        if (!_health.PathogenActive || _health.PathogenEpisode != ev.Episode)
            return;
        foreach (var obligation in _healthObligations)
            if (!obligation.Finished && _elapsed < obligation.Deadline
                && obligation.Episode == ev.Episode && obligation.World == ev.WorldId)
                obligation.Orders.Add(ev.OrderId);
    }

    private void OnAidApplied(SWHealthAidAppliedEvent ev)
    {
        if (ev.ReserveIncrease <= 0 || !_health.PathogenActive || _health.PathogenEpisode != ev.Episode)
            return;
        foreach (var obligation in _healthObligations)
            if (!obligation.Finished && _elapsed < obligation.Deadline
                && obligation.Episode == ev.Episode && obligation.World == ev.WorldId
                && obligation.Orders.Remove(ev.OrderId))
            {
                obligation.Finished = true;
                obligation.Succeeded = true;
                Changed();
            }
    }

    private void TickHealthObligations()
    {
        foreach (var obligation in _healthObligations)
        {
            if (obligation.Finished)
                continue;
            if (!_health.PathogenActive || _health.PathogenEpisode != obligation.Episode)
            {
                obligation.Finished = true;
                obligation.EpidemicEnded = true;
                Changed();
            }
            else if (_elapsed >= obligation.Deadline)
            {
                obligation.Finished = true;
                Changed();
            }
        }
    }
}
