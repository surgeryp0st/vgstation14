using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._VG.Atmos;

/// <summary>
///     vg13's gas reactions (code/modules/ZAS/XGM_reactions.dm). Their conditions are in the <c>gasReaction</c>
///     prototypes; these do the reacting. vg13 scales competing reactions against each other's reactant use, which
///     the engine doesn't, so each runs on its own.
/// </summary>
public static class VgGasReactions
{
    /// <summary>
    ///     vg13 add_thermal_energy(): changes the temperature by this much energy, but never takes it below the floor.
    /// </summary>
    public static void AddThermalEnergy(AtmosphereSystem atmos, GasMixture mixture, float energy, float minTemperature)
    {
        var capacity = atmos.GetHeatCapacity(mixture, true);
        if (capacity <= 0)
            return;

        var temperature = mixture.Temperature + energy / capacity;
        mixture.Temperature = energy < 0 ? MathF.Max(temperature, minTemperature) : temperature;
    }
}

/// <summary>
///     vg13 N2O decomposition: above 300 C, N2O breaks into nitrogen and oxygen and gives off heat, faster the hotter and
///     the higher the pressure.
/// </summary>
[UsedImplicitly]
public sealed partial class VgN2ODecompositionReaction : IGasReactionEffect
{
    private const float MinTemperature = 300 + Atmospherics.T0C;
    private const float DecompositionEnergy = 82050; // 82.05 kJ/mol

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var n2o = mixture.GetMoles(Gas.NitrousOxide);
        if (mixture.Temperature < MinTemperature || n2o <= 0)
            return ReactionResult.NoReaction;

        var ratio = (mixture.Temperature - MinTemperature) / 900;
        ratio *= 1 - 1 / (mixture.Pressure / Atmospherics.OneAtmosphere + 1);
        ratio = MathF.Min(0.95f, MathF.Sqrt(MathF.Max(0, ratio)));

        var used = n2o * ratio;
        if (used <= 0)
            return ReactionResult.NoReaction;

        mixture.AdjustMoles(Gas.NitrousOxide, -used);
        mixture.AdjustMoles(Gas.Nitrogen, used);
        mixture.AdjustMoles(Gas.Oxygen, used * 0.5f);
        VgGasReactions.AddThermalEnergy(atmosphereSystem, mixture, used * DecompositionEnergy, 0);

        return ReactionResult.Reacting;
    }
}

/// <summary>
///     vg13: cryotheum catalyses oxygen into slowly evaporating, giving off a little cold down to 242.9 K.
/// </summary>
[UsedImplicitly]
public sealed partial class VgCryotheumOxygenReaction : IGasReactionEffect
{
    private const float EnergyPerMole = -170000;
    private const float MinTemperature = 242.8952f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var cryotheum = mixture.GetMoles(Gas.Cryotheum);
        var oxygen = mixture.GetMoles(Gas.Oxygen);
        if (cryotheum <= 0 || oxygen <= 0)
            return ReactionResult.NoReaction;

        // Full speed at 1 cryotheum to 50 oxygen, scaling down linearly below that.
        var catalyst = MathF.Min(1, cryotheum / oxygen * 50);
        var used = catalyst * oxygen * 0.0015f;

        mixture.AdjustMoles(Gas.Oxygen, -used);
        VgGasReactions.AddThermalEnergy(atmosphereSystem, mixture, used * EnergyPerMole, MinTemperature);

        return ReactionResult.Reacting;
    }
}

/// <summary>
///     vg13: cryotheum burns itself up in the presence of plasma, drastically cooling the mixture (down to 0.1 K).
/// </summary>
[UsedImplicitly]
public sealed partial class VgCryotheumPlasmaReaction : IGasReactionEffect
{
    private const float MinTemperature = 0.1f;
    private const float EnergyPerMole = -240000;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var cryotheum = mixture.GetMoles(Gas.Cryotheum);
        var plasma = mixture.GetMoles(Gas.Plasma);
        if (cryotheum <= 0 || plasma <= 0)
            return ReactionResult.NoReaction;

        // Full speed at 1 cryotheum to 10 plasma.
        var catalyst = MathF.Min(1, plasma / cryotheum * 10);
        var used = catalyst * cryotheum * 0.2f;

        mixture.AdjustMoles(Gas.Cryotheum, -used);

        // The closer to 0.1 K, the more it cools.
        var distance = MathF.Max(0.00001f, mixture.Temperature - MinTemperature);
        var cooling = MathF.Min(1, 80 / distance) + MathF.Min(5, 50 / distance);
        VgGasReactions.AddThermalEnergy(atmosphereSystem, mixture, cooling * used * EnergyPerMole, MinTemperature);

        return ReactionResult.Reacting;
    }
}

/// <summary>
///     vg13 react(): gas in pipes, tanks and canisters burns by itself once it's past the plasma burn temperature and
///     has fuel and oxygen (zburn() without a tile). Gas on tiles only burns where a vg13 fire is (VgFireSystem).
/// </summary>
[UsedImplicitly]
public sealed partial class VgContainedBurnReaction : IGasReactionEffect
{
    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (holder is TileAtmosphere)
            return ReactionResult.NoReaction;

        // Not cached: each test server has its own entity manager.
        var fire = IoCManager.Resolve<IEntityManager>().System<Content.Server._VG.Fire.VgFireSystem>();
        var recombustible = Content.Server._VG.Fire.VgFireSystem.GasCombustability(mixture) == 2;
        return fire.ZBurn(mixture, recombustible, Array.Empty<Content.Server._VG.Fire.VgFireSystem.BurnProducts>(), false) > 0
            ? ReactionResult.Reacting
            : ReactionResult.NoReaction;
    }
}
