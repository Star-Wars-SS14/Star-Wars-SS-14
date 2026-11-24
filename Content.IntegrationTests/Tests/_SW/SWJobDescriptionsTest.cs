#nullable enable
using System.Linq;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWJobDescriptionsTest
{
    /// <summary>
    /// The lobby shows the description as the job's tooltip; without one the player gets no hint what the job does.
    /// </summary>
    [Test]
    public async Task AllJobsHaveDescriptions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();

        // Jobs defined inside tests belong to no department.
        var inDepartment = protoMan.EnumeratePrototypes<DepartmentPrototype>().SelectMany(d => d.Roles).ToHashSet();

        Assert.Multiple(() =>
        {
            foreach (var job in protoMan.EnumeratePrototypes<JobPrototype>())
            {
                if (!job.ID.StartsWith("SW") || !inDepartment.Contains(job.ID))
                    continue;

                Assert.That(job.Description, Is.Not.Null, $"{job.ID} has no description.");
                Assert.That(loc.HasString(job.Description!), $"{job.ID}: no text for {job.Description}.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
