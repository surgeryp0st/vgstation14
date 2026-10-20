using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Life;

public sealed class VgLifeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgLifeComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<VgLifeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
        Dirty(ent);
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

            // The client only predicts its own mob; everything else waits for the server.
            if (_net.IsClient && _player.LocalEntity != uid)
                continue;

            life.NextTick += life.Interval;
            life.LifeTick++;
            Dirty(uid, life);

            var ev = new VgLifeTickEvent(life.LifeTick);
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
