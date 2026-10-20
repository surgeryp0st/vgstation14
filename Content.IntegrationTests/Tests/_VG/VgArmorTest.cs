using System.Linq;
using Content.Shared._VG.Armor;
using Content.Shared._VG.Body;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
[TestOf(typeof(VgArmorSystem))]
public sealed class VgArmorTest
{
    [Test]
    public async Task ArmorTableMatchesEntities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var armor in protoMan.EnumeratePrototypes<VgArmorPrototype>())
                {
                    Assert.That(protoMan.HasIndex<EntityPrototype>(armor.ID), $"vgArmor {armor.ID} names no entity");
                }

                // Every SS14 clothing item with real armor has vg13 armor, itself or through a parent.
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.TryGetComponent<ArmorComponent>(out var armor, factory) ||
                        armor.Modifiers.Coefficients.Count == 0 || !proto.TryGetComponent<Content.Shared.Clothing.Components.ClothingComponent>(out _, factory))
                    {
                        continue;
                    }

                    var hasVg = protoMan.EnumerateParents<EntityPrototype>(proto.ID, includeSelf: true)
                        .Any(p => protoMan.HasIndex<VgArmorPrototype>(p.ID));
                    Assert.That(hasVg, $"{proto.ID} has SS14 armor but no vgArmor");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VestProtectsTheChestOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var vest = entMan.SpawnEntity("ClothingOuterArmorBasic", map.GridCoords);
            Assert.That(entMan.System<InventorySystem>().TryEquip(human, vest, "outerClothing", silent: true, force: true));

            var damageable = entMan.System<DamageableSystem>();
            var blunt = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), 20);
            var body = entMan.GetComponent<VgBodyComponent>(human);

            // Security armor: melee 50 on the torso.
            damageable.TryChangeDamage(human, blunt.WithVgAttack(VgArmorType.Melee, VgLimbs.Chest));
            damageable.TryChangeDamage(human, blunt.WithVgAttack(VgArmorType.Melee, VgLimbs.LeftLeg));
            // No category: armor doesn't apply.
            damageable.TryChangeDamage(human, blunt.WithVgZone(VgLimbs.RightArm));

            Assert.Multiple(() =>
            {
                Assert.That(body.Limbs[VgLimbs.Chest].BruteDamage, Is.EqualTo(10).Within(0.01));
                Assert.That(body.Limbs[VgLimbs.LeftLeg].BruteDamage, Is.EqualTo(20).Within(0.01));
                Assert.That(body.Limbs[VgLimbs.RightArm].BruteDamage, Is.EqualTo(20).Within(0.01));
            });
        });

        await pair.CleanReturnAsync();
    }
}
