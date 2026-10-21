using Content.Server.Medical;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Status;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._VG.Status;

/// <summary>
///     vg13 vomit(): a warning, 15 seconds later a second warning, 10 seconds after that you are stunned and throw up
///     (costing 40 nutrition and healing 3 toxin damage), and you can't be made to vomit again for 35 seconds after.
/// </summary>
public sealed class VgVomitSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly VgNutritionSystem _nutrition = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;

    private static readonly TimeSpan SecondWarning = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Throw = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(35);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgNutritionComponent, VgVomitEvent>(OnVomit);
    }

    private void OnVomit(Entity<VgNutritionComponent> ent, ref VgVomitEvent args)
    {
        var vomit = EnsureComp<VgVomitingComponent>(ent);
        if (vomit.Stage != VgVomitStage.None)
            return;

        _popup.Self(ent, Loc.GetString("vg-vomit-nauseous"), null);
        vomit.Stage = VgVomitStage.Nauseous;
        vomit.Instant = args.Instant;
        vomit.Next = _timing.CurTime + (args.Instant ? TimeSpan.Zero : SecondWarning);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<VgVomitingComponent>();
        while (query.MoveNext(out var uid, out var vomit))
        {
            if (vomit.Stage == VgVomitStage.None || now < vomit.Next)
                continue;

            switch (vomit.Stage)
            {
                case VgVomitStage.Nauseous:
                    _popup.Self(uid, Loc.GetString("vg-vomit-about-to"), null);
                    vomit.Stage = VgVomitStage.About;
                    vomit.Next = now + (vomit.Instant ? TimeSpan.Zero : Throw);
                    break;
                case VgVomitStage.About:
                    if (!_mobState.IsDead(uid))
                    {
                        _status.Stun(uid, 5);
                        _vomit.Vomit(uid, -10, -10);

                        _nutrition.AddNutrition(uid, -40);
                        var heal = new DamageSpecifier();
                        heal.DamageDict["Toxin"] = -3;
                        _damageable.TryChangeDamage(uid, heal, true, false);
                    }

                    vomit.Stage = VgVomitStage.Cooldown;
                    vomit.Next = now + (vomit.Instant ? TimeSpan.Zero : Cooldown);
                    break;
                default:
                    vomit.Stage = VgVomitStage.None;
                    break;
            }
        }
    }
}

public enum VgVomitStage : byte
{
    None,
    Nauseous,
    About,
    Cooldown,
}

[RegisterComponent]
public sealed partial class VgVomitingComponent : Component
{
    [ViewVariables]
    public VgVomitStage Stage;

    [ViewVariables]
    public bool Instant;

    [ViewVariables]
    public TimeSpan Next;
}
