#nullable enable
using System;
using System.Linq;
using Content.Server._VG.Atmos;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 ZAS airflow in place of SS14 space wind.</summary>
[TestFixture]
public sealed class VgAirflowTest
{
    [Test]
    public async Task BreachSucksThingsOut()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = entMan.System<SharedTransformSystem>();

        EntityUid human = default, paper = default, crowbar = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 2.5f, 0.5f));
            paper = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, 1.5f, 0.5f));
            crowbar = entMan.SpawnEntity("Crowbar", new EntityCoordinates(grid, 1.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        // Knock the east wall out: the room opens to space.
        await server.WaitPost(() =>
        {
            var mapSys = entMan.System<SharedMapSystem>();
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            foreach (var wall in mapSys.GetAnchoredEntities(grid, gridComp, new Vector2i(4, 0)).ToList())
                entMan.DeleteEntity(wall);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"human felt {entMan.GetComponentOrNull<VgAirflowComponent>(human)?.MaxDifferential} kPa");
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<VgAirflowComponent>(human).LastStun, Is.GreaterThan(TimeSpan.Zero), "the rush of air knocks them over");
                Assert.That(entMan.HasComponent<VgAirflowComponent>(human), "airflow got to them");
                Assert.That(xforms.GetWorldPosition(human).X, Is.GreaterThan(2.6f), "and pulled them toward the breach");
                Assert.That(xforms.GetWorldPosition(paper).X, Is.GreaterThan(1.6f), "paper is blown toward the breach");
                Assert.That(entMan.HasComponent<VgAirflowComponent>(crowbar), "the crowbar felt it too");
            });
        });

        await pair.CleanReturnAsync();
    }
}
