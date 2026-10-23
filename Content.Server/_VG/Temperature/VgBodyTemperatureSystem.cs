using Content.Server._VG.Speech;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Speech;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.Clothing;
using Content.Shared._VG.Life;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Species;
using Content.Shared._VG.Status;
using Content.Shared._VG.Temperature;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Bed.Sleep;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VG.Temperature;

/// <summary>
///     vg13 body temperature on the Life() tick: calories burnt and sweat, heat exchanged with the air through clothing,
///     burns past the species' limits, the temperature alert, and hypothermia. See <see cref="VgBodyTemperatureComponent"/>.
/// </summary>
public sealed class VgBodyTemperatureSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly VgClothingSystem _clothing = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private static readonly ProtoId<DamageTypePrototype> Burn = "Burn";
    private static readonly ProtoId<AlertPrototype> ColdAlert = "Cold";
    private static readonly ProtoId<AlertPrototype> HotAlert = "Hot";
    private static readonly ProtoId<AlertCategoryPrototype> TemperatureAlerts = "Temperature";

    // vg13 __DEFINES/setup.dm
    public const float BodyTemperatureDefault = 310.055f;
    private const float Optimal = 310.15f;
    private const float AutoRecoveryDivisor = 0.5f;
    private const float AutoRecoveryMaximum = 2;
    private const float ColdDivisor = 200;
    private const float HeatDivisor = 80;
    private const float HeatingMax = 10;
    public const float ColdDamageLimit = 220.15f;
    public const float HeatDamageLimit = 360.15f;
    private const float PressureFactorNoLinear = 1.5f;
    private const float ColdPressureFactorMax = PressureFactorNoLinear / (-1 / PressureFactorNoLinear + 1);
    private const float FireSuitProtection = 30000;
    private const float HungerFactor = 0.15f;

    // vg13 carbon_defines.dm
    private const float ColdDamage1 = 0.5f;
    private const float ColdDamage2 = 1.5f;
    private const float ColdDamage3 = 3;
    private const float HeatDamage1 = 2;
    private const float HeatDamage2 = 4;
    private const float HeatDamage3 = 8;

    /// <summary>
    ///     The slots vg13's get_cold_protection() and get_heat_protection_flags() look at, with their item types'
    ///     default coverage and heat_conductivity.
    /// </summary>
    private static readonly (string Slot, VgBodyParts Coverage, float Conductivity)[] Slots =
    {
        ("head", VgBodyParts.Head, 0.5f),
        ("outerClothing", VgBodyParts.FullTorso | VgBodyParts.Arms | VgBodyParts.Legs, 0.4f),
        ("jumpsuit", VgBodyParts.FullTorso | VgBodyParts.Arms | VgBodyParts.Legs, 0.4f),
        ("shoes", VgBodyParts.Feet, 0.4f),
        ("gloves", VgBodyParts.Hands, 0.5f),
        ("mask", VgBodyParts.Mouth, 0.4f),
    };

    /// <summary>
    ///     vg13 ethanol (every alcohol) and capsaicin widen the blood vessels.
    /// </summary>
    private static readonly string[] Vasodilators = { "Ethanol", "CapsaicinOil" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyTemperatureComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<VgBodyTemperatureComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<VgBodyTemperatureComponent, AccentGetEvent>(OnAccent);
    }

    private void OnMove(Entity<VgBodyTemperatureComponent> ent, ref MoveEvent args)
    {
        if (args.OldPosition.EntityId != args.NewPosition.EntityId)
            return;

        var oldTile = args.OldPosition.Position.Floored();
        var newTile = args.NewPosition.Position.Floored();
        if (oldTile == newTile)
            return;

        ent.Comp.Steps++;
        if (TryComp<InputMoverComponent>(ent, out var mover) && mover.Sprinting)
            ent.Comp.RunSteps++;
    }

    /// <summary>
    ///     vg13 treat_speech(): moderate hypothermia slurs and stutters a quarter of the time each.
    /// </summary>
    private void OnAccent(Entity<VgBodyTemperatureComponent> ent, ref AccentGetEvent args)
    {
        if (ent.Comp.Hypothermia != VgHypothermia.Moderate)
            return;

        if (_random.Prob(0.25f))
            args.Message = VgSpeech.Slur(args.Message, _random);
        if (_random.Prob(0.25f))
            args.Message = VgSpeech.Stutter(args.Message, _random);
    }

    private void OnLifeTick(Entity<VgBodyTemperatureComponent> ent, ref VgLifeTickEvent args)
    {
        if (!TryComp<TemperatureComponent>(ent, out var temperatureComp))
            return;

        var uid = ent.Owner;
        var comp = ent.Comp;
        var species = _species.Get(uid);
        var alive = !_mobState.IsDead(uid);
        var inCryo = HasComp<InsideCryoPodComponent>(uid);
        var temperature = temperatureComp.CurrentTemperature;
        var stage = Stage(temperature, species, inCryo);

        // vg13 carbon Move() and handle_chemicals_in_body(): burning calories warms you.
        if (alive)
        {
            var steps = comp.Steps + comp.RunSteps;
            if (steps > 0)
                temperature = BurnCalories(uid, temperature, HungerFactor / 20 * steps, false);

            var rate = CompOrNull<VgNutritionComponent>(uid)?.CalorieBurnRate ?? HungerFactor;
            if (HasComp<SleepingComponent>(uid))
                rate *= 0.75f;
            temperature = BurnCalories(uid, temperature, rate, true);
        }

        comp.Steps = 0;
        comp.RunSteps = 0;

        // vg13 handle_body_temperature(): hypothermia, and sweating when too warm.
        if (alive && MathF.Abs(Optimal - temperature) >= 0.5f)
        {
            if (stage != VgHypothermia.None)
                temperature = Hypothermia(uid, stage, temperature);

            if (temperature > Optimal)
            {
                var recovery = MathF.Min(MathF.Abs(Optimal - temperature) / AutoRecoveryDivisor, AutoRecoveryMaximum);
                temperature = Sweat(uid, temperature, recovery, species);
            }
        }

        // vg13 handle_environment(): on fire you only heat up; otherwise the air takes or gives heat through clothing.
        var environment = _atmos.GetContainingMixture(uid, excite: false);
        var onFire = TryComp<FlammableComponent>(uid, out var flammable) && flammable.OnFire;
        var thermalLoss = 0f;
        if (environment != null)
        {
            var locTemp = environment.Temperature;
            var skin = SkinTemperature(uid, temperature, stage);
            if (locTemp < skin)
            {
                thermalLoss = ThermalLoss(uid, comp, environment, skin);
                if (!onFire)
                    temperature -= thermalLoss;
            }
            else if (!onFire)
            {
                var protection = HeatProtection(uid, locTemp);
                if (protection < 1)
                    temperature += MathF.Min((1 - protection) * ((locTemp - skin) / HeatDivisor), HeatingMax);
            }
        }

        // vg13 handle_fire(): burning heats you up to 100 C unless you're in fire suit grade gear.
        if (onFire && 1 - HeatProtection(uid, FireSuitProtection) > 0.0001f && temperature < Atmospherics.T0C + 100)
            temperature = MathF.Min(temperature + HeatingMax, Atmospherics.T0C + 100);

        // Burns past the limits; cryo pods keep you safe.
        if ((temperature < ColdDamageLimit || temperature > HeatDamageLimit) && !inCryo)
        {
            var damage = TemperatureDamage(temperature, species);
            // vg13 apply_damage() with no zone: ran_zone() picks a limb.
            if (damage > 0)
            {
                var burn = new DamageSpecifier(_proto.Index(Burn), damage).WithVgZone(_body.RandomZone(uid, null));
                _damageable.TryChangeDamage(uid, burn, true, interruptsDoAfters: false);
            }
        }

        _temperature.ForceChangeTemperature(uid, temperature, temperatureComp);

        stage = Stage(temperature, species, inCryo);
        var tally = Tally(temperature, species, stage);
        if (stage != comp.Hypothermia || !MathHelper.CloseTo(tally, comp.Tally))
        {
            comp.Hypothermia = stage;
            comp.Tally = tally;
            Dirty(ent);
        }

        UpdateAlert(uid, temperature, thermalLoss, stage, species);
    }

    /// <summary>
    ///     vg13 undergoing_hypothermia().
    /// </summary>
    public static VgHypothermia Stage(float temperature, VgSpeciesPrototype? species, bool inCryo = false)
    {
        if (inCryo || species is { HypothermiaImmune: true })
            return VgHypothermia.None;

        var celsius = temperature - Atmospherics.T0C;
        return celsius switch
        {
            > 34 => VgHypothermia.None,
            >= 32 => VgHypothermia.Mild,
            >= 28 => VgHypothermia.Moderate,
            >= 24 => VgHypothermia.Severe,
            _ => VgHypothermia.Profound,
        };
    }

    /// <summary>
    ///     vg13 movement tally from the cold.
    /// </summary>
    private static float Tally(float temperature, VgSpeciesPrototype? species, VgHypothermia stage)
    {
        // vg13 slime people get sluggish below 183 K instead.
        if (species?.ID == "Slime")
            return temperature < 183.222f ? (283.222f - temperature) / 10 * 175 : 0;

        return 2 * (int) stage;
    }

    /// <summary>
    ///     vg13 get_body_temperature_damage(). vg13 checks heat level 1 first, so any heat burns at level 1.
    /// </summary>
    public static float TemperatureDamage(float temperature, VgSpeciesPrototype? species)
    {
        var cold1 = species?.ColdLevel1 ?? 220;
        var cold2 = species?.ColdLevel2 ?? 200;
        var cold3 = species?.ColdLevel3 ?? 120;
        var heat1 = species?.HeatLevel1 ?? 360;

        if (temperature < cold3)
            return ColdDamage3;
        if (temperature < cold2)
            return ColdDamage2;
        if (temperature < cold1)
            return ColdDamage1;
        if (temperature >= heat1)
            return HeatDamage1;
        return 0;
    }

    /// <summary>
    ///     vg13 burn_calories(): nutrition into heat, up to 50 C.
    /// </summary>
    private float BurnCalories(EntityUid uid, float temperature, float amount, bool force)
    {
        if (!TryComp<VgNutritionComponent>(uid, out var nutrition))
            return temperature;

        if (nutrition.Nutrition - amount <= 0 && !force)
            return temperature;

        var cap = MathF.Max(temperature, Atmospherics.T0C + 50);
        var heat = 0.7f * (100 / ColdDivisor) * (nutrition.Fat ? 2 : 1);

        // The Life() tick's own burn already comes off nutrition in VgNutritionSystem.
        if (!force)
            _nutrition.AddNutrition(uid, -amount, nutrition);

        return MathF.Min(temperature + amount * heat, cap);
    }

    /// <summary>
    ///     vg13 sweat().
    /// </summary>
    private float Sweat(EntityUid uid, float temperature, float amount, VgSpeciesPrototype? species)
    {
        if (species is not { Sweats: true })
            return temperature;

        var fat = TryComp<VgNutritionComponent>(uid, out var nutrition) && nutrition.Fat;
        _nutrition.AddNutrition(uid, -amount / 50, nutrition);
        return temperature - amount * (fat ? 2 : 1);
    }

    /// <summary>
    ///     vg13 handle_hypothermia().
    /// </summary>
    private float Hypothermia(EntityUid uid, VgHypothermia stage, float temperature)
    {
        switch (stage)
        {
            case VgHypothermia.Mild:
                if (_random.Prob(0.15f) && !VesselDilated(uid))
                    ColdMessage(uid, _random.Prob(0.75f));
                if (_random.Prob(0.25f))
                    _status.Jitter(uid, 15);
                break;
            case VgHypothermia.Moderate:
                if (_random.Prob(0.15f) && !VesselDilated(uid))
                    ColdMessage(uid, _random.Prob(0.25f));
                temperature = BurnCalories(uid, temperature, 0.2f, false);

                // vg13 handle_dizziness(): dizzy now and then.
                if (_status.GetDizziness(uid) <= 0 && _random.Prob(0.5f))
                    _status.Dizzy(uid, 120);

                if (_random.Prob(0.02f) && _hands.GetActiveItem(uid) is { } item && _hands.TryDrop(uid, checkActionBlocker: false))
                    _popup.Self(uid, Loc.GetString("vg-cold-drop", ("item", item)), null);
                if (_random.Prob(0.01f))
                {
                    _popup.Visible(uid, Loc.GetString("vg-cold-collapse-others", ("target", uid)), Loc.GetString("vg-cold-collapse-self"), null);
                    _status.Knockdown(uid, 10);
                    _status.Stun(uid, 10);
                }
                break;
            case VgHypothermia.Severe:
            case VgHypothermia.Profound:
                // Out cold; past 24 C there is no breath either (VgBreathingSystem).
                _status.Paralyse(uid, 1.5f);
                if (_random.Prob(0.25f))
                    _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<DamageTypePrototype>("Oxygen"), 5), true, interruptsDoAfters: false);
                break;
        }

        return temperature;
    }

    private void ColdMessage(EntityUid uid, bool light)
    {
        if (light)
            _popup.Self(uid, Loc.GetString($"vg-cold-light-{_random.Next(1, 7)}"), null, PopupType.Medium);
        else
            _popup.Self(uid, Loc.GetString($"vg-cold-hard-{_random.Next(1, 6)}"), null, PopupType.MediumCaution);
    }

    /// <summary>
    ///     vg13 is_vessel_dilated(): alcohol and capsaicin keep the blood in the skin. (vg13's own "moderate and below
    ///     29" check compares kelvin to celsius and is never true.)
    /// </summary>
    private bool VesselDilated(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream) ||
            !_solutions.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals))
        {
            return false;
        }

        foreach (var reagent in Vasodilators)
        {
            if (chemicals.ContainsReagent(reagent, null))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     vg13 get_skin_temperature(): 90% of the body's celsius, less as hypothermia draws blood from the skin.
    /// </summary>
    private float SkinTemperature(EntityUid uid, float temperature, VgHypothermia stage)
    {
        var modifier = 0.9f;
        if (stage != VgHypothermia.None && !VesselDilated(uid))
            modifier -= 0.2f * (int) stage;

        return (temperature - Atmospherics.T0C) * modifier + Atmospherics.T0C;
    }

    /// <summary>
    ///     vg13 get_thermal_loss(): exposed skin, times how dense the air is (if denser than the station's), times the
    ///     difference from the skin, over 200.
    /// </summary>
    private float ThermalLoss(EntityUid uid, VgBodyTemperatureComponent comp, GasMixture environment, float skin)
    {
        var loss = 1 - ColdProtection(uid);
        var density = environment.Volume > 0 ? environment.TotalMoles / environment.Volume : 0;
        var ratio = density / (Atmospherics.MolesCellStandard / Atmospherics.CellVolume);
        if (ratio > 1)
            loss *= ratio < PressureFactorNoLinear ? ratio : -ColdPressureFactorMax / ratio + ColdPressureFactorMax;

        loss *= skin - environment.Temperature;
        loss /= ColdDivisor;
        return loss * comp.ThermalLossMultiplier;
    }

    /// <summary>
    ///     vg13 get_cold_protection(): each worn item's coverage times (1 - heat conductivity), capped by the coverage of
    ///     everything worn.
    /// </summary>
    public float ColdProtection(EntityUid uid)
    {
        var protection = 0f;
        var covered = VgBodyParts.None;
        foreach (var (_, coverage, conductivity) in Worn(uid))
        {
            protection += CoverProtection(coverage) * (1 - conductivity);
            covered |= coverage;
        }

        return MathF.Min(protection, CoverProtection(covered));
    }

    /// <summary>
    ///     vg13 get_heat_protection(get_heat_protection_flags(temperature)): the coverage of items protecting against air
    ///     this hot.
    /// </summary>
    public float HeatProtection(EntityUid uid, float temperature)
    {
        var covered = VgBodyParts.None;
        foreach (var (item, coverage, _) in Worn(uid))
        {
            if (_clothing.Get(item)?.MaxHeatProtection is { } max && max >= temperature)
                covered |= coverage;
        }

        return CoverProtection(covered);
    }

    private IEnumerable<(EntityUid Item, VgBodyParts Coverage, float Conductivity)> Worn(EntityUid uid)
    {
        foreach (var (slot, defaultCoverage, defaultConductivity) in Slots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var item))
                continue;

            var proto = _clothing.Get(item.Value);
            var coverage = proto is { Coverage.Count: > 0 } ? proto.CoverageFlags : defaultCoverage;
            yield return (item.Value, coverage, proto?.HeatConductivity ?? defaultConductivity);
        }
    }

    /// <summary>
    ///     vg13 get_thermal_protection(): how much of the body these parts are (hands and feet 2.5% each, arms and legs
    ///     7.5%, each half of the torso 15%, each part of the head 7.5%).
    /// </summary>
    public static float CoverProtection(VgBodyParts parts)
    {
        var protection = 0f;
        foreach (var (part, value) in CoverValues)
        {
            if ((parts & part) != 0)
                protection += value;
        }

        return MathF.Min(1, protection);
    }

    private static readonly (VgBodyParts Part, float Value)[] CoverValues =
    {
        (VgBodyParts.Head, 0.075f), (VgBodyParts.Eyes, 0.075f), (VgBodyParts.Mouth, 0.075f), (VgBodyParts.Ears, 0.075f),
        (VgBodyParts.UpperTorso, 0.15f), (VgBodyParts.LowerTorso, 0.15f),
        (VgBodyParts.LegLeft, 0.075f), (VgBodyParts.LegRight, 0.075f),
        (VgBodyParts.FootLeft, 0.025f), (VgBodyParts.FootRight, 0.025f),
        (VgBodyParts.ArmLeft, 0.075f), (VgBodyParts.ArmRight, 0.075f),
        (VgBodyParts.HandLeft, 0.025f), (VgBodyParts.HandRight, 0.025f),
    };

    /// <summary>
    ///     vg13's temperature indicator (handle_regular_hud_updates()), as SS14's hot and cold alerts.
    /// </summary>
    private void UpdateAlert(EntityUid uid, float temperature, float thermalLoss, VgHypothermia stage, VgSpeciesPrototype? species)
    {
        // Severity: negative is cold, positive hot, 0 safe.
        int level;
        if (HasReagent(uid, "CapsaicinOil"))
            level = 3;
        else if (thermalLoss <= 0.1f || temperature > Atmospherics.T0C + 50 || stage != VgHypothermia.None ||
                 temperature > (species?.HeatLevel1 ?? 360))
        {
            level = temperature switch
            {
                >= 370 => 3,
                >= 360 => 2,
                >= 320 => 1,
                >= 309 => 0,
                >= 306 => -1,
                >= 290 => -2,
                _ => -3,
            };
        }
        else
        {
            level = thermalLoss switch
            {
                < 0.15f => 0,
                < 0.2f => -1,
                < 0.4f => -2,
                _ => -3,
            };
        }

        if (level == 0)
            _alerts.ClearAlertCategory(uid, TemperatureAlerts);
        else
            _alerts.ShowAlert(uid, level > 0 ? HotAlert : ColdAlert, (short) Math.Abs(level));
    }

    private bool HasReagent(EntityUid uid, string reagent)
    {
        return TryComp<BloodstreamComponent>(uid, out var bloodstream) &&
               _solutions.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals) &&
               chemicals.ContainsReagent(reagent, null);
    }
}
