#nullable enable
using System;
using Content.Server.Cargo.Systems;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// A shop product must never sell for more than it costs, or a player can make money by carrying goods
/// from a buy pallet to a sell pallet.
/// </summary>
[TestFixture]
public sealed class SWBuySellLoopTest
{
    private static readonly ProtoId<SWCurrencyPrototype> SellInput = "SWVanillaCurrency";
    private static readonly ProtoId<SWCurrencyPrototype> SellOutput = "SWEmpireCreditCurrency";

    [Test]
    public async Task ProductsSellForLessThanTheyCost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var pricing = entMan.System<PricingSystem>();
        var bank = entMan.System<SWSharedBankSystem>();

        await server.WaitAssertion(() =>
        {
            var checkedProducts = 0;

            Assert.Multiple(() =>
            {
                foreach (var product in protoMan.EnumeratePrototypes<SWCargoProductPrototype>())
                {
                    if (product.Abstract || !TryGetCheapestCost(product, bank, protoMan, out var cost))
                        continue;

                    var uid = entMan.SpawnEntity(product.Product, testMap.GridCoords);
                    var anchored = entMan.GetComponent<TransformComponent>(uid).Anchored;
                    var price = pricing.GetPrice(uid);
                    entMan.DeleteEntity(uid);

                    // The sell console leaves anchored goods on the pallet.
                    if (anchored)
                        continue;

                    bank.TransferCurrency((float) price, SellInput, SellOutput, out float sold);
                    checkedProducts++;

                    Assert.That(sold, Is.LessThan(cost),
                        $"{product.ID} delivers {product.Product}: buys for {cost} IC, sells for {sold} IC.");
                }
            });

            Assert.That(checkedProducts, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The lowest price of the product in the sell console's output currency. Currencies with no exchange
    /// rate to it, such as supply points, are skipped: no console turns those into credits.
    /// </summary>
    private static bool TryGetCheapestCost(
        SWCargoProductPrototype product,
        SWSharedBankSystem bank,
        IPrototypeManager protoMan,
        out float cost)
    {
        cost = float.MaxValue;
        var found = false;

        foreach (var (currency, amount) in product.Cost)
        {
            if (!bank.TryTransferCurrency(amount, protoMan.Index(currency), protoMan.Index(SellOutput), out var converted))
                continue;

            cost = MathF.Min(cost, converted);
            found = true;
        }

        return found;
    }
}
