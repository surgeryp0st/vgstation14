#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Atmos;
using Content.Shared._VG.Body;
using Content.Shared._VG.Life;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>vg13 plasma_effects(): contamination and eye burns (code/modules/ZAS/Plasma.dm).</summary>
[TestFixture]
public sealed class VgPlasmaContaminationTest
{
    private static readonly string[] Clothes = { "jumpsuit", "shoes", "gloves" };

    private static EntityUid Dress(IEntityManager entMan, EntityUid grid, string mob, params (string Slot, string Item)[] extra)
    {
        var uid = entMan.SpawnEntity(mob, new EntityCoordinates(grid, 1.5f, 0.5f));
        // Breathing plasma would poison them too; only contamination is being looked at.
        entMan.RemoveComponent<Content.Shared._VG.Breathing.VgBreathingComponent>(uid);
        var inventory = entMan.System<InventorySystem>();
        foreach (var (slot, item) in new[] { ("jumpsuit", "ClothingUniformJumpsuitColorGrey"), ("shoes", "ClothingShoesColorBlack"), ("gloves", "ClothingHandsGlovesColorBlack") })
        {
            inventory.TryUnequip(uid, slot, out _, force: true);
            Assert.That(inventory.SpawnItemInSlot(uid, slot, item, true, true), $"{slot} on");
        }

        foreach (var (slot, item) in extra)
        {
            inventory.TryUnequip(uid, slot, out _, force: true);
            Assert.That(inventory.SpawnItemInSlot(uid, slot, item, true, true), $"{slot} on");
        }

        return uid;
    }

    private static void FillPlasma(IEntityManager entMan, EntityUid grid, float moles)
    {
        var atmos = entMan.System<AtmosphereSystem>();
        for (var x = 0; x <= 3; x++)
        {
            atmos.GetTileMixture(grid, null, new Vector2i(x, 0), true)!.AdjustMoles(Gas.Plasma, moles);
        }
    }

    private static void Tick(IEntityManager entMan, EntityUid mob)
    {
        var ev = new VgLifeTickEvent(1);
        entMan.EventBus.RaiseLocalEvent(mob, ref ev);
    }

    private static bool Contaminated(IEntityManager entMan, EntityUid mob, string slot)
    {
        return entMan.System<InventorySystem>().TryGetSlotEntity(mob, slot, out var item) && entMan.HasComponent<VgContaminatedComponent>(item);
    }

    private static float Toxin(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Toxin").Float();
    }

    private static float EyeDamage(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<VgBodyComponent>(mob).Organs[VgOrgans.Eyes].Damage;
    }

    [Test]
    public async Task UnsealedClothesTakeInPlasma()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var bare = Dress(entMan, grid, "MobHuman");
            var sealedIn = Dress(entMan, grid, "MobHuman",
                ("outerClothing", "ClothingOuterHardsuitEVA"), ("head", "ClothingHeadHelmetEVA"));

            // A wisp of plasma, too little to see, does nothing.
            FillPlasma(entMan, grid, 0.5f);
            Tick(entMan, bare);
            Assert.That(Clothes.Any(slot => Contaminated(entMan, bare, slot)), Is.False, "thin plasma");

            FillPlasma(entMan, grid, 5);
            var eyesBefore = EyeDamage(entMan, bare);
            Tick(entMan, bare);
            Tick(entMan, sealedIn);

            Assert.Multiple(() =>
            {
                foreach (var slot in Clothes)
                {
                    Assert.That(Contaminated(entMan, bare, slot), $"{slot} contaminated");
                    Assert.That(Contaminated(entMan, sealedIn, slot), Is.False, $"{slot} under a sealed suit stays clean");
                }

                Assert.That(EyeDamage(entMan, bare), Is.GreaterThan(eyesBefore), "uncovered eyes burn");
                Assert.That(EyeDamage(entMan, sealedIn), Is.Zero, "a helmet covers the eyes");
            });

            // Three contaminated things poison them each tick.
            var toxin = Toxin(entMan, bare);
            Tick(entMan, bare);
            Assert.That(Toxin(entMan, bare) - toxin, Is.EqualTo(3 * 0.02f).Within(0.005f), "contamination loss");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GlassesAndPlasmamen()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            FillPlasma(entMan, grid, 5);
            var glasses = Dress(entMan, grid, "MobHuman", ("eyes", "ClothingEyesGlassesSunglasses"));
            var plasmaman = Dress(entMan, grid, "MobPlasmaman");

            Tick(entMan, glasses);
            Tick(entMan, plasmaman);
            Tick(entMan, plasmaman);

            Assert.Multiple(() =>
            {
                Assert.That(EyeDamage(entMan, glasses), Is.Zero, "glasses keep plasma out of the eyes");
                Assert.That(Contaminated(entMan, plasmaman, "jumpsuit"), "a plasmaman's clothes still take in plasma");
                Assert.That(EyeDamage(entMan, plasmaman), Is.Zero, "but it doesn't hurt them");
                Assert.That(Toxin(entMan, plasmaman), Is.Zero, "nor do their contaminated clothes");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BleachCleansThingsOnTheFloor()
    {
        var (pair, grid) = await VgTestRooms.AirRoom();
        await using var _ = pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var shoes = entMan.SpawnEntity("ClothingShoesColorBlack", new EntityCoordinates(grid, 2.5f, 0.5f));
            entMan.AddComponent<VgContaminatedComponent>(shoes);

            var tile = server.System<SharedMapSystem>().GetTileRef(grid, entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(grid), new Vector2i(2, 0));
            server.ProtoMan.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Water").ReactionTile(tile, 10, entMan, null);
            Assert.That(entMan.HasComponent<VgContaminatedComponent>(shoes), "water doesn't wash it out");

            server.ProtoMan.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Bleach").ReactionTile(tile, 10, entMan, null);
            Assert.That(entMan.HasComponent<VgContaminatedComponent>(shoes), Is.False, "bleach does");
        });

        await pair.CleanReturnAsync();
    }
}
