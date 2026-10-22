using Content.Server.Power.Components;
using Content.Shared._VG.Power;
using Robust.Shared.Prototypes;

namespace Content.Server._VG.Power;

/// <summary>
///     Gives machines vg13's idle power use (<see cref="VgPowerUsePrototype"/>) in place of SS14's power load. Only the idle
///     draw is modelled: vg13's active use and per-use costs (a vend, a door opening) aren't, except where an SS14 system
///     already changes the load itself.
/// </summary>
public sealed class VgPowerUseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ApcPowerReceiverComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<ApcPowerReceiverComponent> ent, ref ComponentStartup args)
    {
        var id = MetaData(ent).EntityPrototype?.ID;
        if (id == null)
            return;

        foreach (var parent in _proto.EnumerateParents<EntityPrototype>(id, includeSelf: true))
        {
            if (!_proto.TryIndex<VgPowerUsePrototype>(parent.ID, out var use))
                continue;

            ent.Comp.Load = use.Idle;
            ent.Comp.VgSavedLoad = use.Idle;
            return;
        }
    }
}
