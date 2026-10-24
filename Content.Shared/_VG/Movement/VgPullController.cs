using System.Numerics;
using Content.Shared.Friction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Movement;

/// <summary>
///     vg13's pull for vg mobs (living Move()): what you pull follows into the tile you just left, staying a tile behind
///     at any speed and following your path round corners, instead of SS14's rope joint. Pulled further than vg13's
///     adjacency allows (stuck on something), the pull breaks.
/// </summary>
public sealed class VgPullController : VirtualController
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    ///     How far behind the puller the pulled thing rides: one tile.
    /// </summary>
    public const float TrailDistance = 1f;

    /// <summary>
    ///     Further than this and the pull breaks.
    /// </summary>
    private const float BreakDistance = 3f;

    private const float MinTrailStep = 0.05f;

    /// <summary>
    ///     Catching up after being left behind, at most this fast (tiles a second).
    /// </summary>
    private const float MaxSpeed = 15f;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(TileFrictionController));
    }

    public override void UpdateBeforeMapSolve(bool prediction, Robust.Shared.Physics.Dynamics.PhysicsMapComponent mapComponent, float frameTime)
    {
        base.UpdateBeforeMapSolve(prediction, mapComponent, frameTime);

        var query = EntityQueryEnumerator<VgMovementComponent, PullerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var movement, out var puller, out var xform))
        {
            if (xform.MapUid != mapComponent.Owner)
                continue;

            if (puller.Pulling is not { } pulled)
            {
                movement.PullTrail.Clear();
                movement.PullDriving = false;
                continue;
            }

            if (!TryComp<PhysicsComponent>(pulled, out var physics) || !TryComp<PullableComponent>(pulled, out var pullable))
                continue;

            var pullerPos = _transform.GetWorldPosition(xform);
            var pulledPos = _transform.GetWorldPosition(pulled);
            var trail = movement.PullTrail;

            if (trail.Count == 0 || (trail[^1] - pullerPos).Length() >= MinTrailStep)
                trail.Add(pullerPos);

            var distance = (pulledPos - pullerPos).Length();
            if (distance > BreakDistance)
            {
                if (!_timing.ApplyingState)
                    _pulling.TryStopPull(pulled, pullable);
                continue;
            }

            // vg13 only moves what you pull once it's no longer next to you.
            if (distance <= TrailDistance + 0.01f)
            {
                Trim(trail, TrailDistance * 2);
                if (movement.PullDriving)
                {
                    PhysicsSystem.SetLinearVelocity(pulled, Vector2.Zero, body: physics);
                    movement.PullDriving = false;
                }
                continue;
            }

            var target = TrailPoint(trail, pullerPos, pulledPos);
            Trim(trail, TrailDistance * 2);

            var velocity = (target - pulledPos) / frameTime;
            if (velocity.Length() > MaxSpeed)
                velocity = velocity / velocity.Length() * MaxSpeed;

            movement.PullDriving = true;
            PhysicsSystem.SetLinearVelocity(pulled, velocity, body: physics);
            PhysicsSystem.WakeBody(pulled, body: physics);
        }
    }

    /// <summary>
    ///     The point a tile back along the puller's path, or a tile from the puller towards the pulled thing if the path
    ///     is shorter than that.
    /// </summary>
    private static Vector2 TrailPoint(List<Vector2> trail, Vector2 pullerPos, Vector2 pulledPos)
    {
        var remaining = TrailDistance;
        var from = pullerPos;
        for (var i = trail.Count - 1; i >= 0; i--)
        {
            var step = trail[i] - from;
            var length = step.Length();
            if (length >= remaining)
                return from + step / length * remaining;

            remaining -= length;
            from = trail[i];
        }

        var away = pulledPos - pullerPos;
        return pullerPos + away / away.Length() * TrailDistance;
    }

    /// <summary>
    ///     Keeps only as much path as we could need.
    /// </summary>
    private static void Trim(List<Vector2> trail, float keep)
    {
        var length = 0f;
        for (var i = trail.Count - 1; i > 0; i--)
        {
            length += (trail[i] - trail[i - 1]).Length();
            if (length > keep)
            {
                trail.RemoveRange(0, i - 1);
                return;
            }
        }
    }
}
