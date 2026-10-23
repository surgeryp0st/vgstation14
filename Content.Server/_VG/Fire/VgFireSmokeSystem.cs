using System.Numerics;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._VG.Fire;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._VG.Fire;

/// <summary>
///     vg13 genSmoke(): burning things now and then give off bad smoke, on SS14's smoke.
/// </summary>
public sealed class VgFireSmokeSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SmokeSystem _smoke = default!;

    private const string SmokePrototype = "VgFireSmoke";

    /// <summary>
    ///     vg13 genSmoke()'s time_to_live, s.
    /// </summary>
    private const float SmokeLifetime = 10;

    private const float MinOxygenToBurn = 0.14f; // MINOXY2BURN

    /// <summary>
    ///     vg13 genSmoke(): a 4% chance at 20 C or colder, falling to none at 1000 C; thicker the less oxygen there is.
    /// </summary>
    public void TrySmoke(EntityCoordinates coordinates, float oxygenRatio, float temperature)
    {
        if (RollSmoke(temperature))
            Smoke(coordinates, oxygenRatio);
    }

    public bool RollSmoke(float temperature)
    {
        var noSmoke = Math.Clamp(96 + 4 * (temperature - Atmospherics.T20C) / (Atmospherics.T0C + 1000 - Atmospherics.T20C), 96, 100);
        return !_random.Prob(noSmoke / 100);
    }

    public EntityUid Smoke(EntityCoordinates coordinates, float oxygenRatio)
    {
        var density = oxygenRatio <= 0 ? 5 : Math.Clamp(5 * MathF.Pow(MinOxygenToBurn / oxygenRatio, 2), 1, 5);
        var smoke = Spawn(SmokePrototype, coordinates);
        _smoke.StartSmoke(smoke, new Solution(), SmokeLifetime, (int) MathF.Ceiling(density));
        return smoke;
    }

    /// <summary>
    ///     vg13 /obj/effect/smoke/bad/Cross(): a beam loses half its damage for each puff it passes through.
    /// </summary>
    public DamageSpecifier ThroughSmoke(MapCoordinates from, EntityUid hit, DamageSpecifier damage)
    {
        var to = _transform.GetMapCoordinates(hit);
        if (to.MapId != from.MapId)
            return damage;

        var crossed = new HashSet<EntityUid>();
        var delta = to.Position - from.Position;
        var steps = (int) MathF.Ceiling(delta.Length() / 0.5f);
        var found = new HashSet<Entity<VgBadSmokeComponent>>();
        for (var i = 0; i <= steps; i++)
        {
            var point = from.Position + delta * (steps == 0 ? 0 : (float) i / steps);
            found.Clear();
            _lookup.GetEntitiesIntersecting(from.MapId, Box2.CenteredAround(point, new Vector2(0.1f, 0.1f)), found);
            foreach (var smoke in found)
            {
                crossed.Add(smoke);
            }
        }

        return crossed.Count == 0 ? damage : damage * MathF.Pow(0.5f, crossed.Count);
    }
}
