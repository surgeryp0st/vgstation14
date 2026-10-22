using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Atmos;
using Content.Shared._VG.Body;
using Content.Shared._VG.CCVar;
using Content.Shared._VG.Clothing;
using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Species;
using Content.Shared._VG.Status;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VG.Atmos;

/// <summary>
///     vg13 plasma_effects() (code/modules/ZAS/Plasma.dm): visible plasma in the air gets into unsealed clothes and
///     burns uncovered eyes; contaminated things poison whoever has them on.
///     Not ported, as they're off in vg13's config: skin burns, genetic corruption, hallucinations and PLASMAGUARD_ONLY.
/// </summary>
public sealed class VgPlasmaContaminationSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly VgClothingSystem _clothing = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    /// <summary>
    ///     vg13 MOLES_PLASMA_VISIBLE: plasma only does anything once there's enough to see.
    /// </summary>
    private const float MolesPlasmaVisible = 0.7f;

    /// <summary>
    ///     vg13 HIDEJUMPSUIT: a suit covering this much keeps plasma off the clothes under it.
    /// </summary>
    private const VgBodyParts HideJumpsuit = VgBodyParts.Arms | VgBodyParts.Legs | VgBodyParts.FullTorso;

    private static readonly ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype> Toxin = "Toxin";

    public override void Initialize()
    {
        base.Initialize();

        // On the inventory: VgBodyComponent's Life() tick is SharedVgBodySystem's, and this is about what's worn.
        SubscribeLocalEvent<InventoryComponent, VgLifeTickEvent>(OnLifeTick);
    }

    private void OnLifeTick(Entity<InventoryComponent> inv, ref VgLifeTickEvent args)
    {
        if (!TryComp<VgBodyComponent>(inv, out var body))
            return;

        var ent = new Entity<VgBodyComponent>(inv, body);
        var immune = _species.Get(ent)?.PlasmaImmune == true;

        // vg13 handle_chemicals_in_body(): what's contaminated on them poisons them.
        if (!immune)
        {
            var contaminated = 0;
            foreach (var item in Carried(ent))
            {
                if (HasComp<VgContaminatedComponent>(item))
                    contaminated++;
            }

            if (contaminated > 0)
            {
                var damage = new DamageSpecifier();
                damage.DamageDict[Toxin] = contaminated * _cfg.GetCVar(VgCCVars.PlasmaContaminationLoss);
                _damageable.TryChangeDamage(ent, damage, ignoreResistances: true, interruptsDoAfters: false);
            }
        }

        // vg13 handle_environment(): only visible plasma does anything.
        if (_atmos.GetContainingMixture(ent.Owner) is not { } air || air.Volume <= 0
            || air.GetMoles(Gas.Plasma) / air.Volume * Atmospherics.CellVolume <= MolesPlasmaVisible)
        {
            return;
        }

        PlasmaEffects(ent, immune);
    }

    /// <summary>
    ///     vg13 plasma_effects().
    /// </summary>
    private void PlasmaEffects(Entity<VgBodyComponent> ent, bool immune)
    {
        if (_cfg.GetCVar(VgCCVars.PlasmaClothContamination))
            Contaminate(ent);

        if (_mobState.IsDead(ent) || immune)
            return;

        if (_cfg.GetCVar(VgCCVars.PlasmaEyeBurns) && !EyesCovered(ent))
            BurnEyes(ent);
    }

    /// <summary>
    ///     vg13 human contaminate(): an unsealed suit lets plasma into the clothes under it, and an open helmet now and
    ///     then too.
    /// </summary>
    private void Contaminate(EntityUid uid)
    {
        if (!SuitProtected(uid))
            SuitContamination(uid);

        if (!HeadProtected(uid) && _random.Prob(0.01f))
            SuitContamination(uid);
    }

    /// <summary>
    ///     vg13 suit_contamination(): the jumpsuit, shoes and gloves.
    /// </summary>
    private void SuitContamination(EntityUid uid)
    {
        foreach (var slot in new[] { "jumpsuit", "shoes", "gloves" })
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out var item))
                EnsureComp<VgContaminatedComponent>(item.Value);
        }
    }

    /// <summary>
    ///     vg13 pl_suit_protected(): a suit that covers the arms, legs and torso.
    /// </summary>
    private bool SuitProtected(EntityUid uid)
    {
        return _inventory.TryGetSlotEntity(uid, "outerClothing", out var suit)
               && _clothing.Get(suit.Value) is { } vg
               && (vg.CoverageFlags & HideJumpsuit) == HideJumpsuit;
    }

    /// <summary>
    ///     vg13 pl_head_protected(): something on the head, with the eyes covered.
    /// </summary>
    private bool HeadProtected(EntityUid uid)
    {
        return _inventory.TryGetSlotEntity(uid, "head", out _) && EyesCovered(uid);
    }

    /// <summary>
    ///     vg13 check_body_part_coverage(EYES): glasses, or a helmet or mask over the eyes. Clothing without vg13
    ///     coverage counts by SS14's identity blocking.
    /// </summary>
    private bool EyesCovered(EntityUid uid)
    {
        if (_inventory.TryGetSlotEntity(uid, "eyes", out _))
            return true;

        foreach (var slot in new[] { "head", "mask" })
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var item))
                continue;

            if (_clothing.Get(item.Value) is { } vg && (vg.CoverageFlags & VgBodyParts.Eyes) != 0)
                return true;

            if (TryComp<IdentityBlockerComponent>(item, out var blocker) && blocker.Enabled
                && (blocker.Coverage & IdentityBlockerCoverage.EYES) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     vg13 burn_eyes(): the eyes take damage up to just short of breaking, and may go blind for a moment.
    /// </summary>
    private void BurnEyes(Entity<VgBodyComponent> ent)
    {
        if (!_body.TryGetOrgan((ent, ent.Comp), VgOrgans.Eyes, out var eyes))
            return;

        if (_random.Prob(0.2f))
            _popup.Self(ent, Loc.GetString("vg-plasma-eyes-burn"), null);

        if (eyes.Damage + 5 < eyes.MinBrokenDamage)
            _body.TakeOrganDamage(ent, eyes, 1, silent: true);

        // TODO VG: eye_blurry 1.5 (max 50), once vg13 eyes are in.

        if (_random.Prob((MathF.Max(0, eyes.Damage - 15) + 1) / 100) && !_statusEffects.HasStatusEffect(ent, "TemporaryBlindness"))
        {
            _popup.Self(ent, Loc.GetString("vg-plasma-blinded"), null);
            _status.Blind(ent, 5);
        }
    }

    /// <summary>
    ///     vg13 <c>for(var/obj/item/I in src)</c>: what the mob wears and holds.
    /// </summary>
    private IEnumerable<EntityUid> Carried(EntityUid uid)
    {
        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item)
                yield return item;
        }

        foreach (var held in _hands.EnumerateHeld(uid))
        {
            yield return held;
        }
    }
}
