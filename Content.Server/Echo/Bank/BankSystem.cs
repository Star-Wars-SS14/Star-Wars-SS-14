using System.Threading;
using Content.Server.Database;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared.Bank.Components;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Echo.Bank;

public sealed partial class BankSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _station = default!;

    private readonly ISawmill _log = default!;

    public bool TryWithdraw(EntityUid uid, int amount)
    {
        var station = _station.GetOwningStation(uid);
        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        if (!TryComp<CapturableStationComponent>(station, out var capturableStationComponent))
            return false;

        while (query.MoveNext(out _, out var rule))
        {
            switch (capturableStationComponent.Fraction)
            {
                case SectorFractions.Alliance:
                {
                    if (rule.AllianceCredits < amount)
                    {
                        return false;
                    }

                    rule.AllianceCredits -= amount;
                    break;
                }
                case SectorFractions.Resistance:
                {
                    if (rule.ResistanceCredits < amount)
                    {
                        return false;
                    }

                    rule.ResistanceCredits -= amount;
                    break;
                }
                default:
                    _log.Error("Unknown fraction!");
                    break;
            }
        }
        return true;
    }
}
