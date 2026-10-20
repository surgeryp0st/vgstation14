using Content.Server.Atmos.Components;
using Content.Shared._VG.Medical;

namespace Content.Server._VG.Clothing;

/// <summary>
///     Tags pressure-protected clothing as <see cref="VgSpaceproofComponent"/>, since pressure protection is server-only
///     and the client needs to know for prediction.
/// </summary>
public sealed class VgSpaceproofSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PressureProtectionComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<PressureProtectionComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<VgSpaceproofComponent>(ent);
    }
}
