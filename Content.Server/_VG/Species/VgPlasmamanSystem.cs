using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Species;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._VG.Species;

/// <summary>
///     vg13 plasmaman handle_environment: with 1% or more oxygen around it and no containment suit and helmet, the body
///     ignites. Fully contained, the suit puts out any fire and holds body temperature.
/// </summary>
public sealed class VgPlasmamanSystem : EntitySystem
{
    /// <summary>
    ///     vg13 OXYCONCEN_PLASMEN_IGNITION.
    /// </summary>
    public const float IgnitionConcentration = 0.01f;

    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly VgNoMouthSystem _noMouth = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgSpeciesSystem _species = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VgSpeciesComponent, VgLifeTickEvent>(OnLifeTick);
    }

    private void OnLifeTick(Entity<VgSpeciesComponent> ent, ref VgLifeTickEvent args)
    {
        _noMouth.LifeTick(ent);

        if (_species.Get(ent) is not { IgnitesInOxygen: true })
            return;

        TryComp<FlammableComponent>(ent, out var flammable);

        if (IsContained(ent, out var suit))
        {
            if (flammable is { OnFire: true } && suit.Comp.NextExtinguish <= _timing.CurTime)
            {
                suit.Comp.NextExtinguish = _timing.CurTime + suit.Comp.ExtinguishCooldown;
                _popup.Self(ent, Loc.GetString("vg-plasmaman-suit-extinguish"), null, PopupType.SmallCaution);
                _flammable.Extinguish(ent, flammable);
            }

            // vg13 regulate_temp_of_wearer: 5 K a tick towards 37 C
            if (TryComp<TemperatureComponent>(ent, out var temperature))
            {
                const float target = Atmospherics.T0C + 37;
                temperature.CurrentTemperature = temperature.CurrentTemperature < target
                    ? MathF.Min(temperature.CurrentTemperature + 5, target)
                    : MathF.Max(temperature.CurrentTemperature - 5, target);
            }

            return;
        }

        if (flammable == null || _atmosphere.GetContainingMixture(ent.Owner) is not { TotalMoles: > 0 } air)
            return;

        if (air.GetMoles(Gas.Oxygen) / air.TotalMoles < IgnitionConcentration)
            return;

        if (!flammable.OnFire)
            _popup.Self(ent, Loc.GetString("vg-plasmaman-ignite"), null, PopupType.MediumCaution);

        // vg13 ignite() has no fuel to run out: keep at least a stack on the body while the air is there
        if (flammable.FireStacks < 1)
            _flammable.AdjustFireStacks(ent, 1 - flammable.FireStacks, flammable);

        _flammable.Ignite(ent, ent, flammable);
    }

    /// <summary>
    ///     Whether a containment suit and a containment helmet are both worn.
    /// </summary>
    public bool IsContained(EntityUid ent, out Entity<VgPlasmaContainmentComponent> suit)
    {
        suit = default;
        if (!_inventory.TryGetSlotEntity(ent, "outerClothing", out var outer)
            || !TryComp<VgPlasmaContainmentComponent>(outer, out var suitComp)
            || !_inventory.TryGetSlotEntity(ent, "head", out var head)
            || !HasComp<VgPlasmaContainmentComponent>(head))
            return false;

        suit = (outer.Value, suitComp);
        return true;
    }
}
