#nullable enable
using Content.Server._VG.Power;
using Content.Server.Power.Components;
using Content.Shared._VG.Nutrition;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgTreadmillTest
{
    [Test]
    public async Task TreadmillMakesPowerFromBumps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var treadmills = entMan.System<VgTreadmillSystem>();
        var nutrition = entMan.System<VgNutritionSystem>();

        EntityUid treadmill = default;
        EntityUid runner = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("CableMV", map.GridCoords);
            treadmill = entMan.SpawnEntity("VgTreadmill", map.GridCoords);
            runner = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        await pair.RunTicksSync(5); // the first cycle starts

        float before = 0;
        await server.WaitAssertion(() =>
        {
            before = nutrition.GetNutrition(runner);
            for (var i = 0; i < 3; i++)
                Assert.That(treadmills.TryBump((treadmill, entMan.GetComponent<VgTreadmillComponent>(treadmill)), runner));

            // vg13 burn_calories(HUNGER_FACTOR * 2) per bump
            Assert.That(nutrition.GetNutrition(runner), Is.EqualTo(before - 0.9f).Within(0.01f));
        });

        // Three bumps of 400 are what the next cycle supplies.
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<VgTreadmillComponent>(treadmill).TickPower, Is.EqualTo(1200));
            Assert.That(entMan.GetComponent<PowerSupplierComponent>(treadmill).MaxSupply, Is.EqualTo(1200));
            Assert.That(entMan.GetComponent<PowerSupplierComponent>(treadmill).Net, Is.Not.Null, "it is on the MV grid");
        });

        await pair.CleanReturnAsync();
    }
}
