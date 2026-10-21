using Content.Server.Atmos.EntitySystems;
using Content.Shared._VG.Radiation;
using Content.Shared.Atmos;
using Content.Shared.Mobs.Systems;

namespace Content.Server._VG.Radiation;

/// <summary>
///     vg13 radon_effects(): standing in air with radon in it irradiates you, 1.5 per mole of radon in a standard
///     tile's worth of air, every Life() tick.
/// </summary>
public sealed class VgRadonSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private const float RadsPerMole = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgRadiationComponent, VgAmbientRadiationEvent>(OnAmbient);
    }

    private void OnAmbient(Entity<VgRadiationComponent> ent, ref VgAmbientRadiationEvent args)
    {
        if (_mobState.IsDead(ent))
            return;

        var air = _atmosphere.GetContainingMixture(ent.Owner);
        if (air == null || air.Volume <= 0)
            return;

        var radon = air.GetMoles(Gas.Radon) / air.Volume * Atmospherics.CellVolume;
        args.Rads += radon * RadsPerMole;
    }
}
