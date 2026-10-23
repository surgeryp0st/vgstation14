#nullable enable
using System.Linq;
using Content.Server._VG.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Slip;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Slippery;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 slipping: wet floors, lube, ice and things you step on.</summary>
[TestFixture]
public sealed class VgSlipTest
{
    private static EntityUid Puddle(IEntityManager entMan, EntityCoordinates at, string reagent)
    {
        var puddle = entMan.SpawnEntity("Puddle", at);
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var comp = entMan.GetComponent<PuddleComponent>(puddle);
        Assert.That(solutions.EnsureSolutionEntity(puddle, comp.SolutionName, out var soln, FixedPoint2Max));
        solutions.TryAddSolution(soln!.Value, new Solution(reagent, 20));
        return puddle;
    }

    private static readonly Content.Shared.FixedPoint.FixedPoint2 FixedPoint2Max = 1000;

    private static EntityUid Human(IEntityManager entMan, EntityCoordinates at, bool walking = false, string? shoes = null)
    {
        var human = entMan.SpawnEntity("MobHuman", at);
        if (walking)
            entMan.System<SharedMoverController>().SetSprinting((human, entMan.GetComponent<InputMoverComponent>(human)), 0, true);
        if (shoes != null)
        {
            var inventory = entMan.System<InventorySystem>();
            inventory.TryUnequip(human, "shoes", out _, force: true);
            Assert.That(inventory.SpawnItemInSlot(human, "shoes", shoes, true, true));
        }

        return human;
    }

    private static void Slip(IEntityManager entMan, EntityUid source, EntityUid victim)
    {
        entMan.System<SlipperySystem>().TrySlip(source, entMan.GetComponent<SlipperyComponent>(source), victim);
    }

    private static bool Stunned(IEntityManager entMan, EntityUid uid) => entMan.HasComponent<StunnedComponent>(uid);

    [Test]
    public async Task WetFloorsAndLube()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var water = Puddle(entMan, map.GridCoords, "Water");
            var lube = Puddle(entMan, map.GridCoords, "SpaceLube");

            var runner = Human(entMan, map.GridCoords);
            var walker = Human(entMan, map.GridCoords, walking: true);
            var galoshes = Human(entMan, map.GridCoords, shoes: "ClothingShoesGaloshes");
            var lubedWalker = Human(entMan, map.GridCoords, walking: true);
            var lubedGaloshes = Human(entMan, map.GridCoords, shoes: "ClothingShoesGaloshes");

            Slip(entMan, water, runner);
            Slip(entMan, water, walker);
            Slip(entMan, water, galoshes);
            var brute = entMan.GetComponent<DamageableComponent>(lubedWalker).TotalDamage;
            Slip(entMan, lube, lubedWalker);
            Slip(entMan, lube, lubedGaloshes);

            Assert.Multiple(() =>
            {
                Assert.That(Stunned(entMan, runner), "water slips someone running");
                Assert.That(entMan.HasComponent<KnockedDownComponent>(runner));
                Assert.That(Stunned(entMan, walker), Is.False, "but not someone walking");
                Assert.That(Stunned(entMan, galoshes), Is.False, "nor someone in galoshes");
                Assert.That(Stunned(entMan, lubedWalker), "lube slips walkers too");
                Assert.That(entMan.GetComponent<DamageableComponent>(lubedWalker).TotalDamage - brute, Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.New(2)), "and bruises");
                Assert.That(Stunned(entMan, lubedGaloshes), "galoshes don't help on lube");

                // vg13 water: stunned for 5 Life() ticks (10 s).
                Assert.That(entMan.System<StatusEffectsSystem>().TryGetTime(runner, "Stun", out var time));
                Assert.That((time!.Value.Item2 - server.Timing.CurTime).TotalSeconds, Is.EqualTo(10).Within(0.5));
            });

            // Soap: 3 ticks.
            var soap = entMan.SpawnEntity("Soap", map.GridCoords);
            var soaped = Human(entMan, map.GridCoords, walking: true);
            Slip(entMan, soap, soaped);
            Assert.That(entMan.System<StatusEffectsSystem>().TryGetTime(soaped, "Stun", out var soapTime), "soap slips walkers");
            Assert.That((soapTime!.Value.Item2 - server.Timing.CurTime).TotalSeconds, Is.EqualTo(6).Within(0.5));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IceFormsMeltsAndSlipsNowAndThen()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = entMan.System<AtmosphereSystem>();

        await server.WaitPost(() =>
        {
            for (var x = 0; x <= 3; x++)
            {
                var air = atmos.GetTileMixture(grid, null, new Vector2i(x, 0), true)!;
                air.AdjustMoles(Gas.Cryotheum, 2);
                air.Temperature = 250;
            }
        });

        // VgIceSystem checks every 2 s.
        await server.WaitRunTicks(90);

        EntityUid ice = default;
        await server.WaitAssertion(() =>
        {
            var ices = entMan.EntityQuery<VgIceComponent>().Select(c => c.Owner).ToList();
            Assert.That(ices, Is.Not.Empty, "cryotheum in freezing air ices the floor");
            ice = ices[0];

            // vg13: below 0 C it thickens; well above, it melts.
            var system = entMan.System<VgIceSystem>();
            var comp = new VgIceComponent { Thickness = 5 };
            Assert.That(system.Thaw(comp, 250), Is.False);
            Assert.That(comp.Thickness, Is.EqualTo(6));
            var melted = false;
            for (var i = 0; i < 20 && !melted; i++)
                melted = system.Thaw(comp, 400);
            Assert.That(melted, "warm air melts it");
        });

        // A 15% chance to slip each step, so over 40 tries some, but not all.
        var slipped = 0;
        for (var i = 0; i < 40; i++)
        {
            await server.WaitAssertion(() =>
            {
                var human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 0.5f, 0.5f));
                Slip(entMan, ice, human);
                if (Stunned(entMan, human))
                    slipped++;
                entMan.DeleteEntity(human);
            });
            await server.WaitRunTicks(1);
        }

        Assert.That(slipped, Is.InRange(1, 20), "ice slips now and then");

        await pair.CleanReturnAsync();
    }
}
