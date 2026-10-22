using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Fire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VG.Fire;

/// <summary>
///     vg13 fire (code/modules/ZAS/Fire.dm) on top of SS14's atmos. A burning tile has a fire entity that burns the gas
///     on it (plasma and volatile fuel), everything flammable on it (<see cref="VgBurnableComponent"/>) and the people
///     standing in it, makes the air hotter, and spreads. Replaces SS14's hotspots.
/// </summary>
public sealed class VgFireSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly VgBurnableSystem _burnable = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private const string FirePrototype = "VgFire";
    private const string AshPrototype = "Ash";

    // vg13 ZAS settings (NewSettings.dm).
    private const float HeatMultiplier = 1.45f;
    private const float MassConsumption = 0.25f;
    private const float OxygenConsumption = 0.025f;
    private const float SpreadChance = 3;
    private const float ConsumptionRate = 0.75f;
    private const float FirelevelMultiplier = 25;
    private const float FuelEnergyRelease = 2000000;

    // vg13 defines.
    private const float MinOxygenToBurn = 0.14f; // MINOXY2BURN
    private const float PlasmaMinimumOxygenNeeded = 2;
    private const float PlasmaMinimumBurnTemperature = Atmospherics.T0C + 100;
    private const float BaseFuelRequired = 0.1f; // BASE_ZAS_FUEL_REQ
    private const float MolesPlasmaVisible = 0.7f;
    private const float FlameTemperaturePlastic = 1773.15f;
    private const float AutoignitionTrigger = 500;

    private static readonly TimeSpan FireTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IgnitionScanTick = TimeSpan.FromSeconds(2);

    private TimeSpan _nextFire;
    private TimeSpan _nextScan;

    private readonly HashSet<EntityUid> _tileEntities = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgHotspotExposeEvent>(OnHotspotExpose);
    }

    #region Gas rules

    /// <summary>
    ///     vg13 molar_density(): moles per litre.
    /// </summary>
    private static float Density(GasMixture air, Gas gas)
    {
        return air.Volume <= 0 ? 0 : air.GetMoles(gas) / air.Volume;
    }

    private static float OxygenRatio(GasMixture air)
    {
        var total = air.TotalMoles;
        return total <= 0 ? 0 : air.GetMoles(Gas.Oxygen) / total;
    }

    /// <summary>
    ///     vg13 check_combustability() for the gases: 2 when there's enough plasma or volatile fuel to burn with
    ///     oxygen, otherwise 0.
    /// </summary>
    public static int GasCombustability(GasMixture air)
    {
        if (air.GetMoles(Gas.Oxygen) > 0 && (air.GetMoles(Gas.Plasma) > 0 || air.GetMoles(Gas.VolatileFuel) > 0))
        {
            if (Density(air, Gas.Plasma) * ConsumptionRate >= MolesPlasmaVisible / Atmospherics.CellVolume)
                return 2;

            if (Density(air, Gas.VolatileFuel) * ConsumptionRate >= BaseFuelRequired / Atmospherics.CellVolume)
                return 2;
        }

        return 0;
    }

    /// <summary>
    ///     vg13 calculate_firelevel(): how intense the fire is according to the gas reactants only.
    /// </summary>
    public static float CalculateFirelevel(GasMixture air, bool recombustible)
    {
        if (!recombustible)
            return 0;

        var fuel = air.GetMoles(Gas.Plasma) + air.GetMoles(Gas.VolatileFuel);
        var oxygen = air.GetMoles(Gas.Oxygen);
        var combustibles = fuel + oxygen;
        if (fuel <= 0 || oxygen <= 0)
            return 0;

        // Slows the burning down when the reactants are thin.
        var dampening = combustibles / (combustibles + air.GetMoles(Gas.Nitrogen) + air.GetMoles(Gas.CarbonDioxide));
        // How close the mixture of the reactants is to the optimum.
        var mix = 1 / (1 + 5 * MathF.Pow(oxygen / combustibles, 2));
        return MathF.Max(0, FirelevelMultiplier * mix * dampening);
    }

    /// <summary>
    ///     What a burning thing gives to the fire in one tick (vg13 burnSolidFuel()'s return value).
    /// </summary>
    public record struct BurnProducts(float HeatOut, float OxygenUsed, float CarbonDioxideProduced, float MaxTemperature);

    /// <summary>
    ///     vg13 zburn(): burns the gas in <paramref name="air"/> along with what the burning things give off, changes the
    ///     gases and works out the new temperature. Returns how much reacted.
    /// </summary>
    public float ZBurn(GasMixture air, bool recombustible, IReadOnlyList<BurnProducts> burning, bool forceBurn)
    {
        if (!recombustible)
            return 0;

        var startingEnergy = air.Temperature * _atmos.GetHeatCapacity(air, true);
        var totalFuel = 0f;
        var totalOxygen = 0f;
        var usedFuelRatio = 0f;
        var totalReactants = 0f;
        var usedReactantsRatio = 0f;

        if (air.Temperature > PlasmaMinimumBurnTemperature || forceBurn)
        {
            totalFuel = air.GetMoles(Gas.Plasma) + air.GetMoles(Gas.VolatileFuel);
            if (totalFuel > 0)
            {
                var firelevel = CalculateFirelevel(air, true);
                totalOxygen = MathF.Min(air.GetMoles(Gas.Oxygen), PlasmaMinimumOxygenNeeded * totalFuel);
                usedFuelRatio = MathF.Min(air.GetMoles(Gas.Oxygen) / PlasmaMinimumOxygenNeeded, totalFuel) / totalFuel;
                totalFuel *= usedFuelRatio;
                totalReactants = totalFuel + totalOxygen;
                usedReactantsRatio = Math.Clamp(firelevel / FirelevelMultiplier, Math.Clamp(0.2f / totalReactants, 0, 1), 1);
            }
        }

        var combustionEnergy = 0f;
        var combustionOxygen = 0f;
        var combustionCarbon = 0f;
        var maxTemperature = 0f;
        foreach (var products in burning)
        {
            combustionEnergy += products.HeatOut;
            combustionOxygen += products.OxygenUsed;
            combustionCarbon += products.CarbonDioxideProduced;
            maxTemperature = MathF.Max(maxTemperature, products.MaxTemperature);
        }

        combustionOxygen = Math.Clamp(combustionOxygen, 0, air.GetMoles(Gas.Oxygen));
        if (maxTemperature <= 0)
            maxTemperature = FlameTemperaturePlastic;

        var oxygen = air.GetMoles(Gas.Oxygen);
        var plasma = air.GetMoles(Gas.Plasma);
        var volatileFuel = air.GetMoles(Gas.VolatileFuel);

        air.AdjustMoles(Gas.Oxygen, -MathF.Min(oxygen, totalOxygen * usedReactantsRatio + combustionOxygen * OxygenConsumption));
        air.AdjustMoles(Gas.Plasma, -MathF.Min(plasma, plasma * usedFuelRatio * usedReactantsRatio * 3));
        air.AdjustMoles(Gas.CarbonDioxide, MathF.Max(2 * totalFuel * usedReactantsRatio + combustionCarbon * OxygenConsumption, 0));
        // Fuel burns five times as quickly.
        air.AdjustMoles(Gas.VolatileFuel, -MathF.Min(volatileFuel, volatileFuel * usedFuelRatio * usedReactantsRatio * 5));

        var efficiency = MathF.Max(1 - air.Temperature / maxTemperature, 0);
        var capacity = _atmos.GetHeatCapacity(air, true);
        if (capacity > 0)
        {
            air.Temperature = (startingEnergy
                               + MathF.Pow(combustionEnergy, HeatMultiplier) * efficiency
                               + FuelEnergyRelease * totalFuel * usedReactantsRatio) / capacity;
        }

        return totalReactants * usedReactantsRatio;
    }

    #endregion

    #region Burning things

    /// <summary>
    ///     vg13 burnSolidFuel(): a burning thing uses up some of its mass and gives heat to the fire. Null when it went
    ///     out or burnt away.
    /// </summary>
    private BurnProducts? BurnSolidFuel(Entity<VgBurnableComponent> ent, GasMixture air, EntityUid grid, Vector2i tile)
    {
        var material = _proto.Index(ent.Comp.Material);
        var oxygenRatio = OxygenRatio(air);

        ent.Comp.BurnTime += FireTick;

        // Heat and oxygen are two sides of the fire triangle; burnrate is about 1 in standard air.
        var burnRate = oxygenRatio / (MinOxygenToBurn + _random.Next(-2, 3) * 0.01f) * (air.Temperature / Atmospherics.T20C);
        if (burnRate < 0.1f || air.GetMoles(Gas.Oxygen) * Atmospherics.CellVolume < air.Volume)
        {
            _burnable.Extinguish(ent);
            return null;
        }

        var deltaMass = 0.20f * burnRate * MassConsumption;
        ent.Comp.ThermalMass -= deltaMass;

        var heatOut = material.HeatingValue * deltaMass;
        var oxygenUsed = deltaMass / material.MolecularWeight / material.FuelOxRatio;

        // Ash it once all its mass has burnt, but not in an instant.
        if (ent.Comp.ThermalMass <= 0.05f)
        {
            if (ent.Comp.BurnTime < VgBurnableSystem.MinBurnTime)
            {
                ent.Comp.ThermalMass = 0.1f;
            }
            else
            {
                ent.Comp.ThermalMass = 0;
                Ashify(ent);
                return new BurnProducts(heatOut * 1000, oxygenUsed, oxygenUsed, material.FlameTemperature);
            }
        }

        return new BurnProducts(heatOut * 1000, oxygenUsed, oxygenUsed, material.FlameTemperature);
    }

    /// <summary>
    ///     vg13 ashify(): it burns into a pile of dust.
    /// </summary>
    private void Ashify(Entity<VgBurnableComponent> ent)
    {
        if (!ent.Comp.OnFire)
            return;

        if (_proto.HasIndex<EntityPrototype>(AshPrototype))
            Spawn(AshPrototype, Transform(ent).Coordinates);

        QueueDel(ent);
    }

    /// <summary>
    ///     vg13 ignite() for a flammable thing lying on the ground. Worn and held things don't ignite.
    /// </summary>
    public bool Ignite(Entity<VgBurnableComponent> ent)
    {
        if (_burnable.IsFireProtected(ent.Comp))
            return false;

        var xform = Transform(ent);
        if (xform.GridUid == null || xform.ParentUid != xform.GridUid || ent.Comp.ThermalMass <= 0)
            return false;

        if (!ent.Comp.OnFire)
        {
            ent.Comp.OnFire = true;
            Dirty(ent);
        }

        EnsureFire(xform.GridUid.Value, _map.TileIndicesFor(xform.GridUid.Value, Comp<MapGridComponent>(xform.GridUid.Value), xform.Coordinates));
        return true;
    }

    #endregion

    #region Liquid fuel

    /// <summary>
    ///     vg13 has_liquid_fuel(): is anything flammable in a container or puddle here?
    /// </summary>
    private bool HasLiquidFuel(EntityUid uid)
    {
        if (!TryComp<SolutionContainerManagerComponent>(uid, out var manager) || HasComp<MobStateComponent>(uid))
            return false;

        foreach (var (_, solution) in _solutions.EnumerateSolutions((uid, manager)))
        {
            foreach (var reagent in solution.Comp.Solution.Contents)
            {
                if (_proto.HasIndex<VgFuelPrototype>(reagent.Reagent.Prototype))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     vg13 burnLiquidFuel(): burns the fuel in a container or puddle, giving heat to the fire. Reagents that aren't
    ///     fuel evaporate one unit at a time while it burns.
    /// </summary>
    private BurnProducts? BurnLiquidFuel(EntityUid uid)
    {
        if (!TryComp<SolutionContainerManagerComponent>(uid, out var manager) || HasComp<MobStateComponent>(uid))
            return null;

        var heat = 0f;
        var oxygen = 0f;
        var carbon = 0f;
        var maxTemperature = 0f;
        var hasFuel = false;
        var any = false;

        foreach (var (_, solution) in _solutions.EnumerateSolutions((uid, manager)).ToArray())
        {
            if (solution.Comp.Solution.Volume <= 0)
                continue;

            any = true;
            var solutionFuel = false;
            foreach (var reagent in solution.Comp.Solution.Contents.ToArray())
            {
                if (!_proto.TryIndex<VgFuelPrototype>(reagent.Reagent.Prototype, out var fuel))
                    continue;

                solutionFuel = hasFuel = true;
                maxTemperature = MathF.Max(maxTemperature, fuel.MaxTemperature);
                heat += fuel.ThermalEnergyTransfer;
                oxygen += fuel.OxygenConsumption;
                carbon += -fuel.CarbonDioxideConsumption;
                _solutions.RemoveReagent(solution, reagent.Reagent, FixedPoint2.New(fuel.ConsumptionRate));
            }

            if (!solutionFuel)
            {
                foreach (var reagent in solution.Comp.Solution.Contents.ToArray())
                {
                    _solutions.RemoveReagent(solution, reagent.Reagent, FixedPoint2.New(1));
                }
            }
        }

        if (!any || !hasFuel)
            return null;

        return new BurnProducts(heat, oxygen, carbon, maxTemperature);
    }

    #endregion

    #region Fire on a tile

    private GasMixture? Mixture(EntityUid grid, Vector2i tile, bool excite = false)
    {
        return _atmos.GetTileMixture(new Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>(grid, null, null), null, tile, excite);
    }

    private void GetTileBurnables(EntityUid grid, Vector2i tile, List<Entity<VgBurnableComponent>> into, List<EntityUid>? liquids = null)
    {
        _tileEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _tileEntities, 0f);
        foreach (var uid in _tileEntities)
        {
            if (TryComp<VgBurnableComponent>(uid, out var burnable) && Transform(uid).ParentUid == grid)
                into.Add((uid, burnable));

            if (liquids != null && HasLiquidFuel(uid))
                liquids.Add(uid);
        }
    }

    /// <summary>
    ///     vg13 check_recombustability(): 2 while there's gas fuel, 1 while something on the tile can still burn, else 0.
    /// </summary>
    private int Recombustability(GasMixture air, List<Entity<VgBurnableComponent>> burnables, List<EntityUid>? liquids = null)
    {
        if (GasCombustability(air) == 2)
            return 2;

        if (liquids is { Count: > 0 })
            return 1;

        foreach (var burnable in burnables)
        {
            if (!_burnable.IsFireProtected(burnable.Comp) && burnable.Comp.ThermalMass > 0)
                return 1;
        }

        return 0;
    }

    /// <summary>
    ///     vg13 check_combustability(): can anything here burn?
    /// </summary>
    private int Combustability(GasMixture air, List<Entity<VgBurnableComponent>> burnables, List<EntityUid>? liquids = null)
    {
        if (GasCombustability(air) == 2)
            return 2;

        if (liquids is { Count: > 0 })
            return 1;

        foreach (var burnable in burnables)
        {
            if (burnable.Comp.ThermalMass > 0)
                return 1;
        }

        return 0;
    }

    private EntityUid? FireAt(EntityUid grid, Vector2i tile)
    {
        _tileEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _tileEntities, 0f);
        foreach (var uid in _tileEntities)
        {
            if (HasComp<VgFireComponent>(uid))
                return uid;
        }

        return null;
    }

    private EntityUid EnsureFire(EntityUid grid, Vector2i tile)
    {
        if (FireAt(grid, tile) is { } existing)
            return existing;

        var fire = Spawn(FirePrototype, _map.ToCenterCoordinates(grid, tile));
        return fire;
    }

    /// <summary>
    ///     vg13 hotspot_expose(): something hot touches the tile, starting a fire if there's anything there that burns.
    /// </summary>
    private void OnHotspotExpose(ref VgHotspotExposeEvent args)
    {
        if (FireAt(args.Grid, args.Tile) != null)
            return;

        var air = Mixture(args.Grid, args.Tile, true);
        if (air == null)
            return;

        var burnables = new List<Entity<VgBurnableComponent>>();
        var liquids = new List<EntityUid>();
        GetTileBurnables(args.Grid, args.Tile, burnables, liquids);

        var combustability = Combustability(air, burnables, liquids);
        if (combustability == 0)
            return;

        var chance = args.Volume * 100 / Atmospherics.CellVolume;
        var igniting = false;

        if (combustability == 2 && _random.Prob(chance / 100))
            igniting = true;

        // Spilt fuel always catches.
        if (liquids.Count > 0)
            igniting = true;

        foreach (var burnable in burnables)
        {
            if (_random.Prob(chance / 100) && !burnable.Comp.OnFire && args.Temperature >= _burnable.GetAutoignitionTemperature(burnable.Comp) && Ignite(burnable))
                igniting = true;
        }

        if (igniting)
            EnsureFire(args.Grid, args.Tile);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextScan)
        {
            _nextScan = now + IgnitionScanTick;
            ScanForAutoignition();
        }

        if (now < _nextFire)
            return;

        _nextFire = now + FireTick;

        var fires = new List<(EntityUid Uid, VgFireComponent Comp)>();
        var query = EntityQueryEnumerator<VgFireComponent>();
        while (query.MoveNext(out var uid, out var fire))
        {
            fires.Add((uid, fire));
        }

        foreach (var (uid, fire) in fires)
        {
            if (!Deleted(uid))
                ProcessFire(uid, fire);
        }
    }

    /// <summary>
    ///     vg13 checkburn(): flammable things in air that is hot enough, with enough oxygen, catch fire by themselves.
    /// </summary>
    private void ScanForAutoignition()
    {
        var query = EntityQueryEnumerator<VgBurnableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var burnable, out var xform))
        {
            if (burnable.OnFire || xform.GridUid == null || xform.ParentUid != xform.GridUid || _burnable.IsFireProtected(burnable))
                continue;

            var grid = xform.GridUid.Value;
            var tile = _map.TileIndicesFor(grid, Comp<MapGridComponent>(grid), xform.Coordinates);
            var air = Mixture(grid, tile);
            if (air == null || air.Temperature < AutoignitionTrigger || air.Temperature < _burnable.GetAutoignitionTemperature(burnable))
                continue;

            if (OxygenRatio(air) < MinOxygenToBurn || air.GetMoles(Gas.Oxygen) * Atmospherics.CellVolume < air.Volume)
                continue;

            if (burnable.ThermalMass <= 0)
            {
                QueueDel(uid);
                continue;
            }

            if (_random.Prob(0.5f))
                Ignite(new Entity<VgBurnableComponent>(uid, burnable));
        }
    }

    /// <summary>
    ///     vg13 /obj/effect/fire/process().
    /// </summary>
    private void ProcessFire(EntityUid uid, VgFireComponent fire)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            QueueDel(uid);
            return;
        }

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var air = Mixture(grid, tile, true);
        if (air == null)
        {
            Extinguish(uid, grid, tile);
            return;
        }

        var burnables = new List<Entity<VgBurnableComponent>>();
        var liquids = new List<EntityUid>();
        GetTileBurnables(grid, tile, burnables, liquids);

        // Is there anything left to burn?
        var recombustability = Recombustability(air, burnables, liquids);
        if (recombustability == 0)
        {
            Extinguish(uid, grid, tile);
            return;
        }

        if (recombustability == 1 && (OxygenRatio(air) < MinOxygenToBurn + _random.Next(-2, 3) * 0.01f || air.GetMoles(Gas.Oxygen) < 1))
        {
            Extinguish(uid, grid, tile);
            return;
        }

        // Clean up residue too small for the rest of the rules to notice.
        TidyResidue(air, Gas.Oxygen);
        TidyResidue(air, Gas.Plasma);
        TidyResidue(air, Gas.VolatileFuel);

        var firelevel = CalculateFirelevel(air, recombustability != 0);
        fire.FireLevel = firelevel;
        SetSize(uid, firelevel);

        BurnThingsOnTile(grid, tile, air, firelevel);
        Spread((uid, fire), grid, gridComp, tile, firelevel);

        // Burn a part of the air at a time, so one pass can't burn all of it.
        var burning = new List<BurnProducts>();
        foreach (var burnable in burnables)
        {
            if (!burnable.Comp.OnFire || Deleted(burnable))
                continue;

            if (BurnSolidFuel(burnable, air, grid, tile) is { } products)
                burning.Add(products);
        }

        foreach (var liquid in liquids)
        {
            if (BurnLiquidFuel(liquid) is { } liquidProducts)
                burning.Add(liquidProducts);
        }

        var flow = air.RemoveVolume(ConsumptionRate * Atmospherics.CellVolume);
        ZBurn(flow, Recombustability(flow, burnables, liquids) != 0, burning, true);
        _atmos.Merge(air, flow);

        fire.BurnDuration++;
    }

    /// <summary>
    ///     vg13 setfirelight(): bigger flames and a brighter light as the firelevel goes up.
    /// </summary>
    private void SetSize(EntityUid uid, float firelevel)
    {
        var (size, radius, energy) = firelevel > 6 ? (3, 7f, 3f) : firelevel > 2.5f ? (2, 5f, 2f) : (1, 3f, 1f);
        _appearance.SetData(uid, VgFireVisuals.Size, size);
        _light.SetRadius(uid, radius);
        _light.SetEnergy(uid, energy);
    }

    private static void TidyResidue(GasMixture air, Gas gas)
    {
        var ratio = air.TotalMoles <= 0 ? 0 : air.GetMoles(gas) / air.TotalMoles;
        if (ratio < 0.1f / Atmospherics.CellVolume)
            air.SetMoles(gas, 0);
    }

    /// <summary>
    ///     Mobs in the fire catch fire and burn, and flammable things on the tile catch fire (vg13 fire_act()).
    /// </summary>
    private void BurnThingsOnTile(EntityUid grid, Vector2i tile, GasMixture air, float firelevel)
    {
        _tileEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _tileEntities, 0f);
        foreach (var other in _tileEntities.ToArray())
        {
            if (TryComp<VgBurnableComponent>(other, out var burnable) && !burnable.OnFire)
                Ignite(new Entity<VgBurnableComponent>(other, burnable));

            if (HasComp<FlammableComponent>(other))
            {
                _flammable.AdjustFireStacks(other, 0.5f, ignite: true);

                // vg13 FireBurn(): burn damage scaled by the fire level and the pressure.
                var multiplier = 5 * MathF.Max(firelevel, 1.5f) / FirelevelMultiplier * MathF.Min(air.Pressure / Atmospherics.OneAtmosphere, 1);
                var damage = new DamageSpecifier();
                damage.DamageDict["Burn"] = 2.5f * multiplier;
                _damageable.TryChangeDamage(other, damage, origin: null);
            }
        }
    }

    /// <summary>
    ///     vg13 fire spreading to open neighbouring tiles that have something to burn.
    /// </summary>
    private void Spread(Entity<VgFireComponent> fire, EntityUid grid, MapGridComponent gridComp, Vector2i tile, float firelevel)
    {
        foreach (var direction in new[] { AtmosDirection.North, AtmosDirection.East, AtmosDirection.South, AtmosDirection.West })
        {
            var other = tile.Offset(direction);
            if (_atmos.IsTileAirBlocked(grid, tile, direction, gridComp) || _atmos.IsTileAirBlocked(grid, other, direction.GetOpposite(), gridComp))
                continue;

            var neighbourAir = Mixture(grid, other);
            if (neighbourAir == null || FireAt(grid, other) != null)
                continue;

            var burnables = new List<Entity<VgBurnableComponent>>();
            var liquids = new List<EntityUid>();
            GetTileBurnables(grid, other, burnables, liquids);
            if (Combustability(neighbourAir, burnables, liquids) == 0)
                continue;

            var chance = SpreadChance * MathF.Round(fire.Comp.BurnDuration / 5f) * MathF.Sqrt(neighbourAir.Temperature / Atmospherics.T20C)
                         + 25 * firelevel + (liquids.Count > 0 ? 50 : 0);
            if (_random.Prob(Math.Clamp(chance, 0, 100) / 100))
                EnsureFire(grid, other);
        }
    }

    /// <summary>
    ///     vg13 Extinguish(): everything on the tile is put out and the fire goes.
    /// </summary>
    private void Extinguish(EntityUid fire, EntityUid grid, Vector2i tile)
    {
        var burnables = new List<Entity<VgBurnableComponent>>();
        GetTileBurnables(grid, tile, burnables);
        foreach (var burnable in burnables)
        {
            _burnable.Extinguish(burnable);
        }

        QueueDel(fire);
    }

    /// <summary>
    ///     Whether the tile has a fire. SS14's IsHotspotActive asks this (a // VG hook), so its extinguishing works.
    /// </summary>
    public bool IsBurning(EntityUid grid, Vector2i tile)
    {
        return FireAt(grid, tile) != null;
    }

    /// <summary>
    ///     vg13 qdel(hotspot): the tile's fire goes, but what's burning on it keeps burning. SS14's HotspotExtinguish.
    /// </summary>
    public bool RemoveFire(EntityUid grid, Vector2i tile)
    {
        if (FireAt(grid, tile) is not { } fire)
            return false;

        QueueDel(fire);
        return true;
    }

    /// <summary>
    ///     vg13 water reaction_turf() and reaction_obj(): everything on the tile is put out (and won't catch for a
    ///     while), and a fire there goes, cooling the air a little but not below 20 C.
    /// </summary>
    public void Douse(EntityUid grid, Vector2i tile)
    {
        var burnables = new List<Entity<VgBurnableComponent>>();
        GetTileBurnables(grid, tile, burnables);
        foreach (var burnable in burnables)
        {
            _burnable.Extinguish(burnable);
        }

        if (!RemoveFire(grid, tile) || Mixture(grid, tile) is not { } air)
            return;

        air.Temperature = MathF.Max(air.Temperature - 0.1f * _random.Next(1, 6), Atmospherics.T20C);
    }

    #endregion
}

/// <summary>
///     Raised when something hot touches a tile (what SS14 calls a hotspot expose; vg13 hotspot_expose()).
/// </summary>
[ByRefEvent]
public record struct VgHotspotExposeEvent(EntityUid Grid, Vector2i Tile, float Temperature, float Volume);
