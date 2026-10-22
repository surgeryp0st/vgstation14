using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared.Examine;
using Content.Shared.Movement.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._VG.Power;

/// <summary>
///     vg13's treadmill: walking into its facing edge from its own tile makes power. Each bump (one per tile of walking
///     speed) burns nutrition, and a runner too hungry to burn any is told to stop. Not ported: vg13's sportiness reagents
///     and hulk bonus, the per-species treadmill speed, and the heat and injuries of an emagged one.
/// </summary>
public sealed class VgTreadmillSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VgTreadmillComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<VgTreadmillComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("vg-treadmill-examine", ("watts", (int) ent.Comp.TickPower)));
    }

    /// <summary>
    ///     One bump by a runner: vg13 <c>powerwalk</c>. False if the runner is too hungry to burn the calories.
    /// </summary>
    public bool TryBump(Entity<VgTreadmillComponent> treadmill, EntityUid runner)
    {
        var comp = treadmill.Comp;
        var nutrition = _nutrition.GetNutrition(runner);
        if (nutrition - comp.BumpNutrition <= 0)
        {
            _popup.Self(runner, Loc.GetString("vg-treadmill-exhausted"), null);
            return false;
        }

        _nutrition.AddNutrition(runner, -comp.BumpNutrition);
        comp.CountPower += comp.BumpEnergy * comp.Efficiency;
        return true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VgTreadmillComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var treadmill, out var xform))
        {
            if (!xform.Anchored)
                continue;

            if (_timing.CurTime >= treadmill.NextCycle)
            {
                treadmill.TickPower = treadmill.CountPower;
                treadmill.CountPower = 0;
                treadmill.NextCycle = _timing.CurTime + TimeSpan.FromSeconds(treadmill.CycleTime);
                if (TryComp<PowerSupplierComponent>(uid, out var supplier))
                    supplier.MaxSupply = treadmill.TickPower;
            }

            Walk((uid, treadmill), xform, frameTime);
        }
    }

    /// <summary>
    ///     Mobs on the treadmill's tile pushing towards its facing edge walk against it; each tile's worth of walking is a bump.
    /// </summary>
    private void Walk(Entity<VgTreadmillComponent> treadmill, TransformComponent xform, float frameTime)
    {
        var facing = xform.LocalRotation.GetDir().ToVec();
        var tile = new EntityCoordinates(xform.ParentUid, xform.LocalPosition);
        var onTile = new HashSet<EntityUid>();

        foreach (var runner in _lookup.GetEntitiesInRange<InputMoverComponent>(tile, 0.5f))
        {
            var mover = runner.Comp;
            var push = mover.Sprinting ? mover.CurTickSprintMovement : mover.CurTickWalkMovement;
            if (push == Vector2.Zero || !HasComp<VgNutritionComponent>(runner))
                continue;

            if (Vector2.Dot(Vector2.Normalize(push), facing) < 0.7f)
                continue;

            if (!TryComp<MovementSpeedModifierComponent>(runner, out var speed))
                continue;

            onTile.Add(runner);
            treadmill.Comp.Progress.TryGetValue(runner, out var progress);
            progress += frameTime * (mover.Sprinting ? speed.CurrentSprintSpeed : speed.CurrentWalkSpeed);
            while (progress >= 1)
            {
                progress -= 1;
                if (!TryBump(treadmill, runner))
                {
                    progress = 0;
                    break;
                }
            }

            treadmill.Comp.Progress[runner] = progress;
        }

        foreach (var gone in treadmill.Comp.Progress.Keys.Where(k => !onTile.Contains(k)).ToList())
            treadmill.Comp.Progress.Remove(gone);
    }
}
