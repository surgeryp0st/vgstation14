using Content.Server.Nutrition.Components;
using Content.Shared._VG.Life;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Nutrition;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgMushroomTest
{
    [Test]
    public async Task MushroomPeopleCannotEatButDissolveFoodTheyStandOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var mushroom = entMan.SpawnEntity("MobMushroom", map.GridCoords);
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var mouthless = new IngestionAttemptEvent();
            entMan.EventBus.RaiseLocalEvent(mushroom, mouthless);
            var mouthful = new IngestionAttemptEvent();
            entMan.EventBus.RaiseLocalEvent(human, mouthful);
            Assert.That(mouthless.Cancelled, Is.True, "no mouth");
            Assert.That(mouthful.Cancelled, Is.False);

            var food = entMan.SpawnEntity("FoodBreadPlain", map.GridCoords);
            var foodComp = entMan.GetComponent<FoodComponent>(food);
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(food, foodComp.Solution, out _, out var before));
            var start = before!.Volume;

            var absorb = entMan.System<Content.Server._VG.Species.VgNoMouthSystem>();
            absorb.Absorb(mushroom, lying: false);

            // standing, it takes a tenth of a bite
            Assert.That(solutions.TryGetSolution(food, foodComp.Solution, out _, out var after));
            Assert.That((start - after!.Volume).Float(), Is.EqualTo(0.5f).Within(0.01f));

            // lying down, it takes the whole bite
            absorb.Absorb(mushroom, lying: true);
            Assert.That(solutions.TryGetSolution(food, foodComp.Solution, out _, out var lying));
            Assert.That((start - lying!.Volume).Float(), Is.EqualTo(5.5f).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }
}
