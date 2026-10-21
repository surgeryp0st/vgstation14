using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._VG.Fire;

public sealed class VgBurnableSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    ///     vg13 MIN_BURN_TIME: things can't burn up in an instant.
    /// </summary>
    public static readonly TimeSpan MinBurnTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     vg13 extinguish(): how long it can't catch fire again.
    /// </summary>
    public static readonly TimeSpan ExtinguishProtection = TimeSpan.FromSeconds(30);

    public float GetAutoignitionTemperature(VgBurnableComponent comp)
    {
        return comp.AutoignitionTemperature ?? _proto.Index(comp.Material).AutoignitionTemperature;
    }

    public bool IsFireProtected(VgBurnableComponent comp)
    {
        return comp.FireProtectionUntil >= _timing.CurTime;
    }

    /// <summary>
    ///     vg13 extinguish(): puts it out, and it can't catch fire again for a while.
    /// </summary>
    public void Extinguish(Entity<VgBurnableComponent> ent, TimeSpan? protection = null)
    {
        if (ent.Comp.OnFire)
        {
            ent.Comp.OnFire = false;
            Dirty(ent);
        }

        ent.Comp.FireProtectionUntil = _timing.CurTime + (protection ?? ExtinguishProtection);
    }
}
