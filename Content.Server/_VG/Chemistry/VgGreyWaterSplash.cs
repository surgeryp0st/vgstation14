using Content.Server.Chat.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.Damage;
using Content.Shared._VG.Popups;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VG.Chemistry;

/// <summary>
///     vg13's water on a Grey (reagents_basic.dm, ACID4WATER): a splash burns them unless something covers their face,
///     and a big one (30 units or more) can scald the head. vg13 also disfigures the face, which isn't ported.
/// </summary>
public sealed partial class VgGreyWaterSplash : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs { Method: Content.Shared.Chemistry.ReactionMethod.Touch } reagentArgs)
            return;

        var entMan = args.EntityManager;
        var target = args.TargetEntity;
        if (!entMan.HasComponent<VgBodyComponent>(target))
            return;

        var volume = (float) reagentArgs.Quantity;
        var damageable = entMan.System<DamageableSystem>();
        var inventory = entMan.System<InventorySystem>();
        var random = IoCManager.Resolve<IRobustRandom>();

        // vg13 check_body_part_coverage(EYES|MOUTH); anything on the face counts.
        if (inventory.TryGetSlotEntity(target, "eyes", out _) || inventory.TryGetSlotEntity(target, "mask", out _)
            || inventory.TryGetSlotEntity(target, "head", out _))
        {
            entMan.System<VgPopupSystem>().Self(target, Loc.GetString("vg-grey-water-protected"), null);
            return;
        }

        if (random.Prob(0.15f) && volume >= 30)
        {
            var scald = new DamageSpecifier();
            scald.DamageDict["Burn"] = 25;
            damageable.TryChangeDamage(target, scald.WithVgZone("head"), true);
            entMan.System<ChatSystem>().TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true);
        }
        else
        {
            var burn = new DamageSpecifier();
            burn.DamageDict["Burn"] = MathF.Min(15, volume * 2);
            damageable.TryChangeDamage(target, burn, true);
        }
    }
}
