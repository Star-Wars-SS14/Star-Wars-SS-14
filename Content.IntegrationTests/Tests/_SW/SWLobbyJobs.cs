#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// The jobs a player picks in the lobby editor. Campaign departments are hidden from it and their kits are fixed,
/// and jobs defined inside tests belong to no department.
/// </summary>
public static class SWLobbyJobs
{
    public static IEnumerable<JobPrototype> Enumerate(IPrototypeManager protos)
    {
        return protos.EnumeratePrototypes<DepartmentPrototype>()
            .Where(d => !d.EditorHidden)
            .SelectMany(d => d.Roles)
            .Distinct()
            .Select(role => protos.Index(role))
            .Where(j => j.ID.StartsWith("SW"));
    }
}
