using System.Collections.Generic;
using Content.Server._VG.Fire;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Fire;
using Content.Shared.Atmos;
using Content.Server.Atmos.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.GameObjects;
using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgFireTest
{
    private static GasMixture Air(float oxygen, float nitrogen, float plasma = 0, float temperature = Atmospherics.T20C)
    {
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        air.AdjustMoles(Gas.Oxygen, oxygen);
        air.AdjustMoles(Gas.Nitrogen, nitrogen);
        air.AdjustMoles(Gas.Plasma, plasma);
        return air;
    }

    [Test]
    public async Task GasFireRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var fire = server.System<VgFireSystem>();

            // Plain air doesn't burn; air with plasma in it does.
            Assert.That(VgFireSystem.GasCombustability(Air(20, 80)), Is.EqualTo(0));
            Assert.That(VgFireSystem.GasCombustability(Air(20, 80, 5)), Is.EqualTo(2));

            // The more even the mixture of plasma and oxygen, the more intense the fire.
            Assert.That(VgFireSystem.CalculateFirelevel(Air(20, 0, 10), true), Is.GreaterThan(VgFireSystem.CalculateFirelevel(Air(20, 80, 10), true)));
            Assert.That(VgFireSystem.CalculateFirelevel(Air(20, 80), true), Is.EqualTo(0));

            // Burning plasma uses up oxygen and plasma, makes carbon dioxide and heats the air up.
            var air = Air(20, 80, 10, 400);
            var before = air.Temperature;
            fire.ZBurn(air, true, new List<VgFireSystem.BurnProducts>(), true);
            Assert.Multiple(() =>
            {
                Assert.That(air.GetMoles(Gas.Plasma), Is.LessThan(10));
                Assert.That(air.GetMoles(Gas.Oxygen), Is.LessThan(20));
                Assert.That(air.GetMoles(Gas.CarbonDioxide), Is.GreaterThan(0));
                Assert.That(air.Temperature, Is.GreaterThan(before));
            });

            // Volatile fuel burns faster than plasma (five times the rate against three).
            var plasma = Air(20, 80, 10, 400);
            fire.ZBurn(plasma, true, new List<VgFireSystem.BurnProducts>(), true);
            var fuel = Air(20, 80, 0, 400);
            fuel.AdjustMoles(Gas.VolatileFuel, 10);
            fire.ZBurn(fuel, true, new List<VgFireSystem.BurnProducts>(), true);
            Assert.That(10 - fuel.GetMoles(Gas.VolatileFuel), Is.GreaterThan(10 - plasma.GetMoles(Gas.Plasma)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThermalMaterialsLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var paper = server.EntMan.SpawnEntity("Paper", map.GridCoords);
            var burnable = server.EntMan.GetComponent<VgBurnableComponent>(paper);
            var system = server.System<VgBurnableSystem>();
            Assert.That(system.GetAutoignitionTemperature(burnable), Is.EqualTo(573.15f).Within(0.01));
            Assert.That(system.IsFireProtected(burnable), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotAirIgnitesPaperAndItBurns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid grid = default;
        EntityUid paper = default;
        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            Assert.That(mapLoader.TryLoad(mapId, "Maps/Test/Breathing/3by3-20oxy-80nit.yml", out var roots));
            grid = roots.First(x => entMan.HasComponent<MapGridComponent>(x));
            paper = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var atmos = entMan.System<AtmosphereSystem>();
            var gridAtmos = entMan.GetComponent<GridAtmosphereComponent>(grid);
            var tile = gridAtmos.Tiles[new Vector2i(0, 0)];
            var tileAir = tile.Air!;
            tileAir.Temperature = 800;

            var ev = new VgHotspotExposeEvent(grid, new Vector2i(0, 0), 800, Atmospherics.CellVolume);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<VgBurnableComponent>(paper).OnFire, Is.True, "the paper caught fire");
                Assert.That(entMan.EntityQuery<VgFireComponent>().Count(), Is.GreaterThan(0), "there is a fire on the tile");
            });
        });

        // The fire burns the paper up over the next few seconds.
        await server.WaitRunTicks(60 * 12);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(paper), Is.True, "the paper burnt away");
        });

        await pair.CleanReturnAsync();
    }
}
