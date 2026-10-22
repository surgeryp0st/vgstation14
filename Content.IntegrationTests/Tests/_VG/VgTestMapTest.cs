#nullable enable
using System.Collections.Generic;
using Content.Shared.Gravity;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Checks the VgTest map made by Tools/_VG/make_test_map.py.</summary>
[TestFixture]
public sealed class VgTestMapTest
{
    [Test]
    public async Task TestMapHasGravity()
    {
        // It has no gravity generator, so the grid's own gravity has to survive the startup refresh.
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
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityComponent>(grids![0]).Enabled, Is.True);
            Assert.That(entMan.System<SharedGravitySystem>().IsWeightless(human), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
