#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Light.Components;
using Content.Server.Power.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Gravity;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Checks the VgTest map made by Tools/_VG/make_test_map.py.</summary>
[TestFixture]
public sealed class VgTestMapTest
{
    [Test]
    public async Task TestMapIsUsable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        IReadOnlyList<EntityUid>? grids = null;
        EntityUid human = default;
        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            Assert.That(mapLoader.TryLoad(mapId, "Maps/_VG/vg_test.yml", out grids));
            // On the corridor floor by the spawn points.
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grids![0], -4.5f, 2.5f));
        });
        // Long enough for the power networks to settle.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var grid = grids![0];
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);

            // It has no gravity generator, so the grid's own gravity has to survive the startup refresh.
            Assert.That(entMan.GetComponent<GravityComponent>(grid).Enabled, Is.True);
            Assert.That(entMan.System<SharedGravitySystem>().IsWeightless(human), Is.False);

            // Every light has an APC, the corridor's included.
            var unpowered = new List<string>();
            var lights = entMan.EntityQueryEnumerator<PoweredLightComponent, ApcPowerReceiverComponent, TransformComponent>();
            var lightCount = 0;
            while (lights.MoveNext(out _, out _, out var receiver, out var xform))
            {
                lightCount++;
                if (!receiver.Powered)
                    unpowered.Add(xform.Coordinates.Position.Floored().ToString());
            }

            // Nothing solid right inside or outside a door.
            var blocked = new List<string>();
            var doors = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (doors.MoveNext(out _, out _, out var doorXform))
            {
                var tile = mapSys.TileIndicesFor(grid, gridComp, doorXform.Coordinates);
                foreach (var side in new[] { new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    foreach (var ent in mapSys.GetAnchoredEntities(grid, gridComp, tile + side))
                    {
                        if (entMan.TryGetComponent<PhysicsComponent>(ent, out var physics) && physics.Hard && physics.CanCollide)
                            blocked.Add($"{entMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID} at {tile + side}");
                    }
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(lightCount, Is.GreaterThan(0));
                Assert.That(unpowered, Is.Empty, "unpowered lights");
                Assert.That(blocked, Is.Empty, "doors blocked by");
            });
        });

        await pair.CleanReturnAsync();
    }
}
