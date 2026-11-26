using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

public enum SWNavyProjectKind : byte { Supply, Repair, Construction }

/// <summary>
/// Finite physical kits for aggregate combat formations. Supply covers four active hours;
/// the Defense receiver decides when the next kit is needed and applies a completed kit once.
/// These are additional naval services, separate from planetary garrisons and civilian freighters.
/// </summary>
public static class SWRepublicNavyLogistics
{
    public const double SupplyHours = 4;
    public const int OpeningDepotKits = 4;
    public static double OpeningDepotReserve(string good) => good switch
    {
        "military_supplies" => .15 * OpeningDepotKits,
        "ship_fuel" => .1 * OpeningDepotKits,
        _ => 0,
    };
    // A commissioned hull used to be a zero-slot automatic asset. Naval commissioning now
    // uses the project carrier and route ledger, including eight slots for the hull transfer.
    public const double HullCargoSlots = 8;
    // Commodity units are aggregate shipyard lots, not a count of individual vessels.
    // One formation takes a quarter lot; Kuat starts with one completed formation lot.
    public const double HullsPerFormation = .25;

    public static EconomyProjectRequest CreateProject(string id, string world, SWNavyProjectKind kind, double scale = 1,
        int? recipientFleetId = null)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > 100)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var (supplies, fuel, parts, circuits, hours) = kind switch
        {
            SWNavyProjectKind.Supply => (.15, .1, 0d, 0d, .25),
            SWNavyProjectKind.Repair => (.25, .1, .5, .25, 2d),
            // Hull production already includes eight hours of shipyard assembly. The
            // receiver's work is crew assignment, loading and commissioning of that lot.
            SWNavyProjectKind.Construction => (.5, .2, .5, .25, 2d),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var materials = new List<EconomyAmount>
        {
            new("military_supplies", supplies * scale), new("ship_fuel", fuel * scale),
        };
        // Routine replenishment is fuel and ammunition. Replacement components belong to
        // the repair/commissioning receiver, so a minute circuit relay cannot prevent fuel loading.
        if (parts > 0) materials.Add(new EconomyAmount("parts", parts * scale));
        if (circuits > 0) materials.Add(new EconomyAmount("circuits", circuits * scale));
        if (kind == SWNavyProjectKind.Construction)
            materials.Add(new EconomyAmount("military_ships", HullsPerFormation * scale));
        return new EconomyProjectRequest(id, world, materials.ToArray(), hours, NavySupply: kind == SWNavyProjectKind.Supply,
            NavyKind: (EconomyNavyProjectKind) kind, RecipientFleetId: recipientFleetId);
    }
}
