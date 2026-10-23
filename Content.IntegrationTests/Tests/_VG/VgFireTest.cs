#nullable enable
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
            grid = roots!.First(x => entMan.HasComponent<MapGridComponent>(x));
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

    [Test]
    public async Task SpiltFuelBurns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid grid = default;
        EntityUid beaker = default;
        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            Assert.That(mapLoader.TryLoad(mapId, "Maps/Test/Breathing/3by3-20oxy-80nit.yml", out var roots));
            grid = roots!.First(x => entMan.HasComponent<MapGridComponent>(x));
            beaker = entMan.SpawnEntity("Beaker", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            var solutions = entMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln));
            solutions.TryAddSolution(soln!.Value, new Content.Shared.Chemistry.Components.Solution("WeldingFuel", 30));
        });

        await server.WaitAssertion(() =>
        {
            var ev = new VgHotspotExposeEvent(grid, new Vector2i(0, 0), 700, Atmospherics.CellVolume);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(entMan.EntityQuery<VgFireComponent>().Count(), Is.GreaterThan(0), "fuel caught fire");
        });

        await server.WaitRunTicks(60 * 4);

        await server.WaitAssertion(() =>
        {
            var solutions = entMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out _, out var solution));
            Assert.That(solution!.GetTotalPrototypeQuantity("WeldingFuel").Float(), Is.LessThan(30), "the fuel burnt");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientSeesMobAlreadyOnFire()
    {
        // A mob the client first sees while it is burning (joining mid-round next to a burning
        // plasmaman) must not spawn its fire light before the mob has finished initialising.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEntMan = server.EntMan;

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            sEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, Air(21.824779f, 82.10312f));
            human = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            sEntMan.System<FlammableSystem>().AdjustFireStacks(human, 5, ignite: true);
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var cEntMan = client.EntMan;
            var clientHuman = cEntMan.GetEntity(sEntMan.GetNetEntity(human));
            var visuals = cEntMan.GetComponent<Content.Client.Atmos.Components.FireVisualsComponent>(clientHuman);
            Assert.That(visuals.LightEntity, Is.Not.Null, "burning mob has its fire light");
        });

        await pair.CleanReturnAsync();
    }

    private static Task<(Content.IntegrationTests.Pair.TestPair Pair, EntityUid Grid)> AirRoom() => VgTestRooms.AirRoom();

    /// <summary>Sets the tile alight with a beaker of welding fuel, so it keeps burning.</summary>
    private static EntityUid FuelFire(IEntityManager entMan, EntityUid grid, Vector2i tile)
    {
        var beaker = entMan.SpawnEntity("Beaker", new EntityCoordinates(grid, tile + new Vector2(0.5f, 0.5f)));
        var solutions = entMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln));
        solutions.TryAddSolution(soln!.Value, new Content.Shared.Chemistry.Components.Solution("WeldingFuel", 30));
        var ev = new VgHotspotExposeEvent(grid, tile, 700, Atmospherics.CellVolume);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return beaker;
    }

    [Test]
    public async Task WaterPutsOutFire()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var fires = entMan.System<VgFireSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var tile = new Vector2i(2, 0);

        EntityUid paper = default;
        await server.WaitAssertion(() =>
        {
            FuelFire(entMan, grid, tile);
            paper = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, tile + new Vector2(0.5f, 0.5f)));
            Assert.That(fires.Ignite((paper, entMan.GetComponent<VgBurnableComponent>(paper))), Is.True);
            Assert.That(fires.IsBurning(grid, tile));
            // SS14's extinguishing asks atmos, which now sees the vg13 fire.
            Assert.That(atmos.IsHotspotActive(grid, tile), Is.True);

            var air = atmos.GetTileMixture(grid, null, tile, true)!;
            air.Temperature = 600;
            var water = server.ProtoMan.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Water");
            var tileRef = server.System<SharedMapSystem>().GetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), tile);
            water.ReactionTile(tileRef, 10, entMan, null);

            // vg13: the paper is out and stays out for a while, the air cools by at most half a degree.
            var burnable = entMan.GetComponent<VgBurnableComponent>(paper);
            Assert.That(burnable.OnFire, Is.False);
            Assert.That(entMan.System<VgBurnableSystem>().IsFireProtected(burnable), Is.True);
            Assert.That(air.Temperature, Is.InRange(599.5f, 599.9f));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(fires.IsBurning(grid, tile), Is.False, "fire is out"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExtinguisherPutsOutFire()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var fires = entMan.System<VgFireSystem>();
        var target = new Vector2i(2, 0);

        await server.WaitAssertion(() => FuelFire(entMan, grid, target));

        // Left alone, it keeps burning for at least as long as the spray takes.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(fires.IsBurning(grid, target), "still burning before the spray");

            // vg13 setfirelight(): the flames take the colour of the air's heat.
            var fire = entMan.EntityQuery<VgFireComponent>().Single().Owner;
            Assert.That(entMan.System<SharedAppearanceSystem>().TryGetData<Color>(fire, VgFireVisuals.Color, out var color), "fire has a heat colour");
            Assert.That(color.RByte, Is.EqualTo(255), "flames are red-orange");

            var user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 0.5f, 0.5f));
            var extinguisher = entMan.SpawnEntity("FireExtinguisher", new EntityCoordinates(grid, 0.5f, 0.5f));
            entMan.System<Content.Shared.Item.ItemToggle.ItemToggleSystem>().TryActivate(extinguisher);
            var spray = entMan.GetComponent<Content.Server.Fluids.Components.SprayComponent>(extinguisher);
            var targetCoords = entMan.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(grid, target + new Vector2(0.5f, 0.5f)));
            entMan.System<Content.Server.Fluids.EntitySystems.SpraySystem>().Spray((extinguisher, spray), user, targetCoords);
        });

        // The vapour takes a moment to fly over.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() => Assert.That(fires.IsBurning(grid, target), Is.False, "sprayed fire is out"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WoodenFloorsBurn()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var fires = entMan.System<VgFireSystem>();
        var mapSys = server.System<SharedMapSystem>();
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var tileDefs = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>();
        var wood = new Tile(tileDefs["FloorWood"].TileId);
        var plating = new Tile(tileDefs["Plating"].TileId);
        var woodTile = new Vector2i(2, 0);
        var platingTile = new Vector2i(0, 0);

        EntityUid? FloorFuel(Vector2i tile)
        {
            foreach (var uid in entMan.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(grid, tile, 0f))
            {
                if (entMan.HasComponent<VgFloorFuelComponent>(uid))
                    return uid;
            }
            return null;
        }

        void Expose(Vector2i tile)
        {
            var ev = new VgHotspotExposeEvent(grid, tile, 700, Atmospherics.CellVolume);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        }

        await server.WaitAssertion(() =>
        {
            mapSys.SetTile(grid, gridComp, woodTile, wood);

            Expose(platingTile);
            Assert.That(fires.IsBurning(grid, platingTile), Is.False, "plating doesn't burn");

            Expose(woodTile);
            Assert.That(fires.IsBurning(grid, woodTile), Is.True, "the wooden floor catches");
            var fuel = FloorFuel(woodTile);
            Assert.That(fuel, Is.Not.Null);
            var burnable = entMan.GetComponent<VgBurnableComponent>(fuel!.Value);
            Assert.That(burnable.OnFire, Is.True);
            Assert.That(burnable.Material.Id, Is.EqualTo("Wood"));
            // Most of it gone already, so it burns out quickly.
            burnable.ThermalMass = 0.3f;
        });

        await server.WaitRunTicks(30 * 20);

        await server.WaitAssertion(() =>
        {
            var fuel = FloorFuel(woodTile);
            Assert.That(fuel, Is.Not.Null, "a spent floor stays");
            var burnable = entMan.GetComponent<VgBurnableComponent>(fuel!.Value);
            Assert.That(burnable.ThermalMass, Is.Zero);
            Assert.That(burnable.OnFire, Is.False);
            Assert.That(fires.IsBurning(grid, woodTile), Is.False, "the fire went out with nothing left to burn");
        });

        // Spent, it can't catch again; a new floor can.
        await server.WaitRunTicks(30 * 31);
        await server.WaitAssertion(() =>
        {
            Expose(woodTile);
            Assert.That(fires.IsBurning(grid, woodTile), Is.False, "spent floor doesn't catch again");

            mapSys.SetTile(grid, gridComp, woodTile, plating);
            mapSys.SetTile(grid, gridComp, woodTile, wood);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(FloorFuel(woodTile), Is.Null, "relaid floor has no old burning state");
            Expose(woodTile);
            Assert.That(fires.IsBurning(grid, woodTile), Is.True, "relaid wooden floor catches");

            var carpet = server.ProtoMan.Index<Robust.Shared.Prototypes.EntityPrototype>("Carpet");
            Assert.That(carpet.Components.ContainsKey("VgBurnable"), "SS14 carpets burn like vg13's carpet floors");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlasmaWallsGoUp()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var plating = new Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId);

        // Two plasma walls in the corridor's north wall, two tiles apart, and one six tiles off.
        EntityUid near = default, chained = default, far = default;
        await server.WaitPost(() =>
        {
            EntityUid Replace(Vector2i tile)
            {
                foreach (var old in mapSys.GetAnchoredEntities(grid, gridComp, tile).ToList())
                {
                    entMan.DeleteEntity(old);
                }
                return entMan.SpawnEntity("WallPlasma", new EntityCoordinates(grid, tile.X + 0.5f, tile.Y + 0.5f));
            }

            near = Replace(new Vector2i(0, 1));
            chained = Replace(new Vector2i(2, 1));
            mapSys.SetTile(grid, gridComp, new Vector2i(0, -5), plating);
            far = Replace(new Vector2i(0, -5));
        });
        await server.WaitRunTicks(5);

        var plasmaBurn = entMan.System<Content.Server._VG.Fire.VgPlasmaBurnSystem>();
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<VgPlasmaBurnComponent>(near);
            Assert.That(plasmaBurn.Ignite((near, comp), 290), Is.False, "too cool to set it off");
            Assert.That(plasmaBurn.Ignite((near, comp), Content.Server._VG.Fire.VgPlasmaBurnSystem.WelderTemperature), Is.True);
        });
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(near), "the lit wall went up");
            Assert.That(entMan.Deleted(chained), "a plasma wall two tiles away went with it");
            Assert.That(entMan.Deleted(far), Is.False, "six tiles away is out of range");

            var girders = 0;
            foreach (var tile in new[] { new Vector2i(0, 1), new Vector2i(2, 1) })
            {
                girders += mapSys.GetAnchoredEntities(grid, gridComp, tile)
                    .Count(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "Girder");
            }
            Assert.That(girders, Is.EqualTo(2), "each left its girder");

            // Its plasma is in the room (or already burnt to CO2).
            var atmos = entMan.System<AtmosphereSystem>();
            var released = 0f;
            for (var x = -1; x <= 4; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (atmos.GetTileMixture(grid, null, new Vector2i(x, y), false) is { } air)
                    released += air.GetMoles(Gas.Plasma) + air.GetMoles(Gas.CarbonDioxide);
            }
            Assert.That(released, Is.GreaterThan(1), "plasma came out");

            // A beam sets one off by itself.
            var beam = new Content.Shared.Weapons.Ranged.Events.HitScanReflectAttemptEvent(null, far, Content.Shared.Weapons.Reflect.ReflectType.None, Vector2.UnitX, false);
            entMan.EventBus.RaiseLocalEvent(far, ref beam);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(far), "shot with a beam, it went up"));

        // Someone takes a lit welder to one.
        EntityUid wall = default;
        await server.WaitAssertion(() =>
        {
            wall = entMan.SpawnEntity("WallPlasma", new EntityCoordinates(grid, 0.5f, -4.5f));
            var user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 3.5f, 0.5f));
            var welder = entMan.SpawnEntity("Welder", new EntityCoordinates(grid, 3.5f, 0.5f));
            Assert.That(entMan.System<Content.Shared.Item.ItemToggle.ItemToggleSystem>().TryActivate(welder, user), "welder lights");
            entMan.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(user, welder, wall,
                entMan.GetComponent<TransformComponent>(wall).Coordinates, checkCanInteract: false, checkCanUse: false);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(wall), "a lit welder set it off"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotPlasmaBurnsInCanisters()
    {
        // vg13 react(): plasma and oxygen past 100 C burn by themselves in a canister, but on a floor only with a fire.
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = entMan.System<AtmosphereSystem>();
        var floor = new Vector2i(3, 0);

        Content.Server.Atmos.Piping.Unary.Components.GasCanisterComponent canister = default!;
        await server.WaitPost(() =>
        {
            var uid = entMan.SpawnEntity("StorageCanister", new EntityCoordinates(grid, 0.5f, 0.5f));
            canister = entMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasCanisterComponent>(uid);
            canister.Air.Clear();
            canister.Air.AdjustMoles(Gas.Plasma, 10);
            canister.Air.AdjustMoles(Gas.Oxygen, 20);
            canister.Air.Temperature = 400;

            var tileAir = atmos.GetTileMixture(grid, null, floor, true)!;
            tileAir.AdjustMoles(Gas.Plasma, 10);
            tileAir.Temperature = 400;
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(canister.Air.GetMoles(Gas.Plasma), Is.LessThan(10), "plasma burnt in the canister");
            Assert.That(canister.Air.GetMoles(Gas.CarbonDioxide), Is.GreaterThan(0), "into CO2");
            Assert.That(canister.Air.Temperature, Is.GreaterThan(400), "and heated it");

            var tileAir = atmos.GetTileMixture(grid, null, floor, false)!;
            Assert.That(tileAir.GetMoles(Gas.CarbonDioxide), Is.Zero, "hot plasma on a floor needs a fire to burn");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyWaterAndCornOilPutOutFires()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var fires = entMan.System<VgFireSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var tile = new Vector2i(2, 0);

        await server.WaitAssertion(() =>
        {
            FuelFire(entMan, grid, tile);
            Assert.That(fires.IsBurning(grid, tile));
            var tileRef = server.System<SharedMapSystem>().GetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), tile);
            Content.Shared.Chemistry.Reagent.ReagentPrototype Reagent(string id) => server.ProtoMan.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>(id);

            // vg13: drinks don't put fires out.
            Reagent("Cola").ReactionTile(tileRef, 10, entMan, null);
            Assert.That(atmos.IsHotspotActive(grid, tile), "cola doesn't");

            // Corn oil smothers it and takes the heat out of the air.
            var air = atmos.GetTileMixture(grid, null, tile, true)!;
            air.Temperature = 1000;
            Reagent("Cornoil").ReactionTile(tileRef, 10, entMan, null);
            // min(T - 2000, T / 2): 2000 K colder, floored at the cosmic background.
            Assert.That(air.Temperature, Is.EqualTo(Atmospherics.TCMB).Within(0.01), "the heat taken out");

            // Water still puts out a burning person it touches.
            Assert.That(Reagent("Water").ReactiveEffects?.ContainsKey("Extinguish"), Is.True);
            Assert.That(Reagent("Cola").ReactiveEffects?.ContainsKey("Extinguish") ?? false, Is.False);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(fires.IsBurning(grid, tile), Is.False, "corn oil put it out"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BurningThingsSmoke()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var smoke = entMan.System<Content.Server._VG.Fire.VgFireSmokeSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // vg13: 4% a burn at room temperature, none at 1000 C.
                var cool = Enumerable.Range(0, 4000).Count(_ => smoke.RollSmoke(Atmospherics.T20C));
                Assert.That(cool, Is.InRange(100, 230), "about 4%");
                Assert.That(Enumerable.Range(0, 1000).Count(_ => smoke.RollSmoke(Atmospherics.T0C + 1000)), Is.Zero);
            });

            var puff = smoke.Smoke(new EntityCoordinates(grid, 1.5f, 0.5f), 0.21f);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<Robust.Shared.GameObjects.OccluderComponent>(puff), "it blocks sight");
                // ceil(5 * (0.14 / 0.21)^2) = 3 puffs' worth of spreading.
                Assert.That(entMan.GetComponent<Content.Shared.Chemistry.Components.SmokeComponent>(puff).SpreadAmount, Is.EqualTo(3));
            });
        });

        await server.WaitAssertion(() =>
        {
            // A beam through two puffs keeps a quarter of its damage.
            foreach (var smokeEnt in entMan.EntityQuery<VgBadSmokeComponent>().Select(c => c.Owner).ToList())
                entMan.DeleteEntity(smokeEnt);
            entMan.SpawnEntity("VgFireSmoke", new EntityCoordinates(grid, 1.5f, 0.5f));
            entMan.SpawnEntity("VgFireSmoke", new EntityCoordinates(grid, 2.5f, 0.5f));
            var target = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 3.5f, 0.5f));
            var from = entMan.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(grid, 0.5f, 0.5f));
            var damage = new Content.Shared.Damage.DamageSpecifier();
            damage.DamageDict["Burn"] = 20;
            var through = smoke.ThroughSmoke(from, target, damage);
            Assert.That(through.DamageDict["Burn"].Float(), Is.EqualTo(5).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SS14ThingsBurnAsVg13s()
    {
        var (pair, grid) = await AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var fires = entMan.System<VgFireSystem>();
        var tile = new Vector2i(2, 0);

        await server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => entMan.SpawnEntity(id, new EntityCoordinates(grid, tile + new Vector2(0.5f, 0.5f)));
            VgBurnableComponent? Burnable(EntityUid uid) => entMan.GetComponentOrNull<VgBurnableComponent>(uid);

            var jumpsuit = Spawn("ClothingUniformJumpsuitColorGrey");
            Assert.Multiple(() =>
            {
                Assert.That(Burnable(jumpsuit)?.Material.Id, Is.EqualTo("Fabric"), "a jumpsuit is cloth");
                Assert.That(Burnable(jumpsuit)?.ThermalMass, Is.EqualTo(1), "of a normal size");
                Assert.That(Burnable(Spawn("FoodApple"))?.Material.Id, Is.EqualTo("Biological"));
                Assert.That(Burnable(Spawn("BookRandom"))?.Material.Id, Is.EqualTo("Wood"));
                Assert.That(Burnable(Spawn("TableWood"))?.ThermalMass, Is.EqualTo(5), "a wooden table, vg13's large");
                Assert.That(Burnable(Spawn("ClothingHeadHelmetBasic")), Is.Null, "helmets don't burn");
                Assert.That(Burnable(Spawn("ClothingEyesGlassesSunglasses")), Is.Null, "nor glasses");
                Assert.That(Burnable(Spawn("MobHuman")), Is.Null, "mobs burn their own way");
            });

            // On the floor of a burning tile, it catches.
            FuelFire(entMan, grid, tile);
            Assert.That(fires.IsBurning(grid, tile));
        });

        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            var burning = entMan.EntityQuery<VgBurnableComponent>().Any(b => b.OnFire
                && entMan.GetComponent<MetaDataComponent>(b.Owner).EntityPrototype?.ID == "ClothingUniformJumpsuitColorGrey");
            var ashed = entMan.EntityQuery<MetaDataComponent>().Any(m => m.EntityPrototype?.ID == "Ash");
            Assert.That(burning || ashed, "the jumpsuit caught fire");
        });

        await pair.CleanReturnAsync();
    }
}
