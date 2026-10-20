using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.Breathing;
using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Status;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VG.Breathing;

/// <summary>
///     vg13 breathing (breathe(), handle_breath() and the lungs' gas handlers), once per Life() tick. Replaces SS14's
///     respirator on these mobs. Server-side, like atmos.
/// </summary>
public sealed class VgBreathingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private static readonly ProtoId<DamageTypePrototype> Oxygen = "Asphyxiation";
    private static readonly ProtoId<DamageTypePrototype> Toxin = "Poison";
    private static readonly ProtoId<DamageTypePrototype> Burn = "Heat";

    // vg13 HUMAN_MAX_OXYLOSS, and HUMAN_CRIT_MAX_OXYLOSS for a 2 second Life() tick.
    private const float MaxOxyLoss = 1;
    private const float CritMaxOxyLoss = 2f / 3;

    /// <summary>
    ///     vg13 GAS_CONSUME_TO_WASTE_DENOMINATOR.
    /// </summary>
    private const float ConsumeDenominator = 2;

    // vg13 MIN/MAX_PLASMA_DAMAGE.
    private const float MinToxicDamage = 1;
    private const float MaxToxicDamage = 10;

    /// <summary>
    ///     vg13 BREATH_MOLES: a normal breath at one atmosphere and 20 C.
    /// </summary>
    private static readonly float BreathMoles = Atmospherics.OneAtmosphere * Atmospherics.BreathVolume / (Atmospherics.T20C * Atmospherics.R);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBreathingComponent, VgLifeTickEvent>(OnLifeTick);
    }

    private void OnLifeTick(Entity<VgBreathingComponent> ent, ref VgLifeTickEvent args)
    {
        if (_mobState.IsDead(ent))
            return;

        TryComp<VgBodyComponent>(ent, out var body);
        VgOrgan? lungs = null;
        if (body != null)
            body.Organs.TryGetValue(VgOrgans.Lungs, out lungs);

        // In crit, or without lungs, you can't draw a breath.
        if (Health(ent) < 0 || lungs == null)
            _status.AddLoseBreath(ent, 1);

        var environment = _atmos.GetContainingMixture(ent.Owner, excite: true);
        GasMixture? breath = null;

        if (_status.GetLoseBreath(ent) > 0)
        {
            _status.AddLoseBreath(ent, -1);
            if (_random.Prob(0.1f))
                Emote(ent, "Gasp");
        }
        else
        {
            // Internals first (SS14 internals and cryo pods answer this), then the air around you.
            var inhale = new InhaleLocationEvent();
            RaiseLocalEvent(ent, ref inhale);
            var source = inhale.Gas ?? environment;
            breath = source?.RemoveVolume(Atmospherics.BreathVolume);

            // Breathing air that's far too thin or too thick strains the lungs.
            if (inhale.Gas == null && lungs != null && body != null &&
                (breath == null || breath.TotalMoles < BreathMoles / 5 || breath.TotalMoles > BreathMoles * 5) &&
                _random.Prob(0.2f))
            {
                _body.TakeOrganDamage((ent, body), lungs, 1, silent: true);
            }
        }

        HandleBreath(ent, breath, lungs != null);

        // Breathe out into the room.
        if (breath != null && environment != null)
            _atmos.Merge(environment, breath);
    }

    /// <summary>
    ///     vg13 handle_breath() and the lungs' gas handlers.
    /// </summary>
    private void HandleBreath(Entity<VgBreathingComponent> ent, GasMixture? breath, bool hasLungs)
    {
        // TODO VG: inaprovaline and preslomite stop suffocation damage, once vg13 chemistry is in.
        if (breath == null || breath.TotalMoles <= 0 || !hasLungs)
        {
            Damage(ent, Oxygen, Health(ent) >= 0 ? MaxOxyLoss : CritMaxOxyLoss);
            return;
        }

        var comp = ent.Comp;
        var total = breath.TotalMoles;
        // vg13 sets the breath to its 0.5 l volume, so partial pressures match the air it came from.
        var pressure = total * Atmospherics.R * breath.Temperature / Atmospherics.BreathVolume;
        float PartialPressure(Gas gas) => breath.GetMoles(gas) / total * pressure;

        // The gas you live on.
        var breathPressure = PartialPressure(comp.BreathGas);
        if (breathPressure < comp.MinPressure && _random.Prob(0.2f))
            Emote(ent, "Gasp");

        var ratio = breathPressure > 0 ? MathF.Min(1, breathPressure / comp.MinPressure) : 0;
        var moles = breath.GetMoles(comp.BreathGas);
        var used = 0f;
        if (ratio <= 0)
        {
            Damage(ent, Oxygen, MaxOxyLoss);
        }
        else if (ratio >= 1)
        {
            Damage(ent, Oxygen, -5);
            used = moles / ConsumeDenominator;
        }
        else
        {
            Damage(ent, Oxygen, MaxOxyLoss * (1 - ratio));
            used = moles * ratio / ConsumeDenominator;
        }

        breath.AdjustMoles(comp.BreathGas, -used);

        // Waste gas: too much and you pass out after a while.
        if (comp.WasteMaxPressure > 0 && PartialPressure(comp.WasteGas) > comp.WasteMaxPressure)
        {
            var now = _timing.CurTime;
            comp.WasteOverloadStart ??= now;
            var overloaded = now - comp.WasteOverloadStart.Value;
            if (overloaded > TimeSpan.FromSeconds(12))
            {
                _status.Paralyse(ent, 3);
                Damage(ent, Oxygen, overloaded > TimeSpan.FromSeconds(60) ? 4 : 1);
            }

            if (_random.Prob(0.2f))
                Emote(ent, "Cough");
        }
        else
        {
            comp.WasteOverloadStart = null;
        }

        breath.AdjustMoles(comp.WasteGas, used);

        // Toxic gases.
        foreach (var toxic in comp.ToxicGases)
        {
            var toxicPressure = PartialPressure(toxic.Gas);
            if (toxicPressure <= toxic.MaxPressure)
                continue;

            var toxicRatio = toxicPressure / toxic.MaxPressure * toxic.Multiplier;
            if (toxic.MaxPressureMask > 0 && WearingGasMask(ent))
                toxicRatio = toxicPressure > toxic.MaxPressureMask ? toxicPressure / toxic.MaxPressureMask * toxic.Multiplier : 0;

            if (toxicRatio > 0)
                Damage(ent, Toxin, Math.Clamp(toxicRatio, MinToxicDamage, MaxToxicDamage));
        }

        // Sleeping gas.
        var sleepPressure = PartialPressure(comp.SleepGas);
        if (sleepPressure > comp.ParalysePressure)
            _status.Paralyse(ent, 3);
        // TODO VG: vg13 sleeping builds up by 2 a breath to 10; until sleep is ported, a longer paralysis.
        if (sleepPressure > comp.SleepPressure)
            _status.Paralyse(ent, 5);
        if (sleepPressure > comp.GigglePressure && _random.Prob(0.2f))
            Emote(ent, "Laugh");
        breath.SetMoles(comp.SleepGas, 0);

        HandleBreathTemperature(ent, breath.Temperature);
    }

    /// <summary>
    ///     vg13 lungs handle_breath(): breathing very hot or cold air burns your face.
    /// </summary>
    private void HandleBreathTemperature(Entity<VgBreathingComponent> ent, float temperature)
    {
        var comp = ent.Comp;
        if (MathF.Abs(310.15f - temperature) <= 50)
            return;

        if (temperature < comp.ColdLevel1 && _random.Prob(0.2f))
            _popup.Self(ent, Loc.GetString("vg-breath-cold"), null, PopupType.MediumCaution);
        else if (temperature > comp.HeatLevel1 && _random.Prob(0.2f))
            _popup.Self(ent, Loc.GetString("vg-breath-hot"), null, PopupType.MediumCaution);

        var damage = temperature switch
        {
            _ when temperature <= comp.ColdLevel3 => 3f,
            _ when temperature <= comp.ColdLevel2 => 1.5f,
            _ when temperature <= comp.ColdLevel1 => 0.5f,
            _ when temperature >= comp.HeatLevel3 => 8f,
            _ when temperature >= comp.HeatLevel2 => 4f,
            _ when temperature >= comp.HeatLevel1 => 2f,
            _ => 0f,
        };

        if (damage > 0)
        {
            var burn = new DamageSpecifier(_proto.Index(Burn), damage).WithVgZone(VgLimbs.Head);
            _damageable.TryChangeDamage(ent, burn, true, interruptsDoAfters: false);
        }
    }

    private bool WearingGasMask(EntityUid uid)
    {
        return _inventory.TryGetSlotEntity(uid, "mask", out var mask) && HasComp<IdentityBlockerComponent>(mask);
    }

    private float Health(EntityUid uid)
    {
        return TryComp<DamageableComponent>(uid, out var damageable) ? 100 - damageable.TotalDamage.Float() : 100;
    }

    private void Damage(EntityUid uid, ProtoId<DamageTypePrototype> type, float amount)
    {
        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index(type), amount), true, interruptsDoAfters: false);
    }

    private void Emote(EntityUid uid, string emote)
    {
        _chat.TryEmoteWithChat(uid, emote, ChatTransmitRange.HideChat, ignoreActionBlocker: true);
    }
}
