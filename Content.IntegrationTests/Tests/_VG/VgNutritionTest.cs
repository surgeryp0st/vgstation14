using Content.Server.Body.Components;
using Content.Shared._VG.Breathing;
using Content.Shared._VG.Life;
using Content.Shared._VG.Nutrition;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgNutritionTest
{
    private static void LifeTick(IEntityManager entMan, EntityUid uid)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    [Test]
    public async Task NutritionBurnsAndFoodFeeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.RemoveComponent<VgBreathingComponent>(human);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<HungerComponent>(human), Is.False);
                Assert.That(entMan.HasComponent<ThirstComponent>(human), Is.False);
            });

            var nutrition = entMan.GetComponent<VgNutritionComponent>(human);
            Assert.That(nutrition.Nutrition, Is.EqualTo(400));

            LifeTick(entMan, human);
            Assert.That(nutrition.Nutrition, Is.EqualTo(400 - 0.15f).Within(0.001));

            // 10 units of nutriment: 3 nutrition on the first tick, 0.2 units metabolised.
            entMan.System<Content.Server.Body.Systems.BloodstreamSystem>()
                .TryAddToChemicals(human, new Content.Shared.Chemistry.Components.Solution("Nutriment", FixedPoint2.New(10)));
            var before = nutrition.Nutrition;
            LifeTick(entMan, human);
            Assert.That(nutrition.Nutrition, Is.EqualTo(before - 0.15f + 3).Within(0.001));
        });

        await pair.CleanReturnAsync();
    }
}
