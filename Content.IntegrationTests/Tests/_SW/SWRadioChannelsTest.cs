#nullable enable
using System.Linq;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWRadioChannelsTest
{
    /// <summary>
    /// Jammers and the examine text of a radio work by frequency, so two channels must not share one.
    /// </summary>
    [Test]
    public async Task ChannelFrequenciesAreUnique()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        var duplicates = protoMan.EnumeratePrototypes<RadioChannelPrototype>()
            .Where(c => c.ID.StartsWith("SW"))
            .GroupBy(c => c.Frequency)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.ID))}")
            .ToList();

        Assert.That(duplicates, Is.Empty);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A key's default channel must be one the key gives access to, or speaking on the default channel fails.
    /// </summary>
    [Test]
    public async Task KeyDefaultChannelsAreOnTheKey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var factory = pair.Server.ResolveDependency<IComponentFactory>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.ID.StartsWith("SW"))
                        continue;

                    if (!proto.TryGetComponent<EncryptionKeyComponent>(out var key, factory)
                        || key.DefaultChannel == null)
                        continue;

                    Assert.That(key.Channels, Does.Contain(key.DefaultChannel.Value), proto.ID);
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
