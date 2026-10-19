using Robust.Shared.Timing;

namespace Content.Server._VG.Life;

public sealed class VgLifeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgLifeComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<VgLifeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<VgLifeComponent>();
        while (query.MoveNext(out var uid, out var life))
        {
            if (now < life.NextTick)
                continue;

            life.NextTick += life.Interval;
            life.LifeTick++;

            var ev = new VgLifeTickEvent(life.LifeTick);
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
