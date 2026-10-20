using System.Linq;
using Content.Shared._VG.Body;
using Content.Shared._VG.Clothing;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Damage;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Random;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Armor;

/// <summary>
///     vg13 armor: every worn item covering the hit limb adds its armor for the attack's category, and the hit is
///     reduced by that percentage (vg13 run_armor_check() and apply_damage(blocked)). Replaces SS14 armor and worn
///     explosion resistance on vg13 mobs.
/// </summary>
public sealed class VgArmorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly VgClothingSystem _clothing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly VgCombatSystem _combat = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;

    /// <summary>
    ///     vg13 checkarmor() looks at these slots.
    /// </summary>
    private static readonly string[] ArmorSlots = { "head", "mask", "outerClothing", "jumpsuit", "gloves", "shoes" };

    /// <summary>
    ///     vg13 projectiles: -30 to the miss chance before distance and aim come in.
    /// </summary>
    private const float ProjectileMissModifier = -30;

    private const int SaltProjectile = 30;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<VgBodyComponent, GetExplosionResistanceEvent>(OnGetExplosionResistance);
    }

    /// <summary>
    ///     vg13 checkarmor() / checkarmorabsorb(): the summed armor of worn items covering a body part.
    /// </summary>
    public (float Armor, float Absorb) GetArmor(EntityUid mob, VgBodyParts part, VgArmorType type)
    {
        var armor = 0f;
        var absorb = 0f;
        foreach (var slot in ArmorSlots)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot, out var item) || _clothing.Get(item.Value) is not { } itemArmor)
                continue;

            if ((itemArmor.CoverageFlags & part) == 0)
                continue;

            armor += itemArmor.Get(type);
            absorb += itemArmor.GetAbsorb(type);
        }

        return (armor, absorb);
    }

    /// <summary>
    ///     vg13 getarmor(null, type): with no zone, the average over all limbs.
    /// </summary>
    public (float Armor, float Absorb) GetAverageArmor(Entity<VgBodyComponent> mob, VgArmorType type)
    {
        if (mob.Comp.Limbs.Count == 0)
            return (0, 0);

        var armor = 0f;
        var absorb = 0f;
        foreach (var limb in mob.Comp.Limbs.Values)
        {
            var (a, b) = GetArmor(mob, limb.BodyPart, type);
            armor += a;
            absorb += b;
        }

        return (armor / mob.Comp.Limbs.Count, absorb / mob.Comp.Limbs.Count);
    }

    private void OnDamageModify(Entity<VgBodyComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Damage.VgArmor is not { } type)
            return;

        VgLimb? limb = null;
        var (armor, absorb) = args.Damage.VgZone is { } zone && _body.TryGetLimbForZone((ent, ent.Comp), zone, out limb)
            ? GetArmor(ent, limb.BodyPart, type)
            : GetAverageArmor(ent, type);

        armor = MathF.Max(0, armor - args.Damage.VgArmorPenetration);
        if (armor <= 0 && (absorb <= 0 || !args.Damage.VgArmorAbsorb))
            return;

        var damage = new DamageSpecifier(args.Damage);

        // vg13 run_armor_absorb(): small hits are soaked up by absorbing armor.
        if (args.Damage.VgArmorAbsorb && absorb > 0)
        {
            foreach (var (key, value) in damage.DamageDict.ToArray())
            {
                var multiplier = value.Float() / absorb;
                if (value > 0 && multiplier < 1)
                    damage.DamageDict[key] = value * multiplier;
            }
        }

        // vg13 apply_damage(blocked): armor takes its percentage off.
        damage *= MathF.Max(0, 100 - armor) / 100;
        args.Damage = damage;

        if (armor >= 50 && limb != null && _mobState.IsAlive(ent))
        {
            var key = armor >= 100 ? "vg-armor-protects" : "vg-armor-softens";
            _popup.Self(ent, Loc.GetString(key, ("limb", Loc.GetString(limb.Name))), args.Origin, PopupType.MediumCaution);
        }
    }

    /// <summary>
    ///     vg13 ex_act(): bomb armor, averaged over the body, reduces explosion damage.
    /// </summary>
    private void OnGetExplosionResistance(Entity<VgBodyComponent> ent, ref GetExplosionResistanceEvent args)
    {
        var (bomb, _) = GetAverageArmor(ent, VgArmorType.Bomb);
        args.DamageCoefficient *= Math.Clamp(100 - bomb, 0, 100) / 100;
    }

    /// <summary>
    ///     vg13 bullet_act(): a projectile or hitscan shot hits the shooter's selected zone, with vg13's miss chance.
    ///     Returns null if it narrowly missed. The category is laser for burn damage and bullet otherwise.
    /// </summary>
    public DamageSpecifier? AimShot(EntityUid shot, EntityUid target, EntityUid? shooter, DamageSpecifier damage)
    {
        if (!TryComp<VgBodyComponent>(target, out var body))
            return damage;

        var dict = damage.DamageDict;
        var burn = dict.GetValueOrDefault(VgDamageTranslation.Burn).Float();
        var brute = dict.GetValueOrDefault(VgDamageTranslation.Brute).Float();
        var type = burn > brute ? VgArmorType.Laser : VgArmorType.Bullet;

        var zone = shooter != null ? _combat.GetZone(shooter.Value) : VgLimbs.Chest;
        var random = VgPredictedRandom.Create(_timing.CurTick, GetNetEntity(shot).Id, GetNetEntity(target).Id, SaltProjectile);
        var hit = _combat.ZoneWithMissChance(zone, target, random, ProjectileMissModifier);

        if (hit != null && body.Limbs.TryGetValue(hit, out var limb) && !limb.IsExisting)
            hit = null;

        if (hit == null)
        {
            var message = Loc.GetString("vg-armor-projectile-miss", ("projectile", shot), ("target", Identity.Entity(target, EntityManager)));
            _popup.Visible(target, message, message, null, PopupType.Small);
            return null;
        }

        return damage.WithVgAttack(type, hit, absorb: true);
    }
}
