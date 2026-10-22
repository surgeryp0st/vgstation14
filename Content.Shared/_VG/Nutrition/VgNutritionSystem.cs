using Content.Shared._VG.Life;
using Content.Shared._VG.Popups;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Nutrition;

public sealed class VgNutritionSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;

    // SS14's hunger alerts, which show the same states as vg13's food alert (hungry, starving).
    private static readonly ProtoId<AlertPrototype> HungryAlert = "Peckish";
    private static readonly ProtoId<AlertPrototype> StarvingAlert = "Starving";
    private static readonly ProtoId<AlertCategoryPrototype> HungerCategory = "Hunger";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgNutritionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VgNutritionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VgNutritionComponent, VgLifeTickEvent>(OnLifeTick);
        SubscribeLocalEvent<VgNutritionComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<VgNutritionComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<VgNutritionComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnStartup(Entity<VgNutritionComponent> ent, ref ComponentStartup args)
    {
        // vg13 has no thirst, and nutrition replaces hunger.
        RemComp<HungerComponent>(ent);
        RemComp<ThirstComponent>(ent);
        UpdateAlert(ent);
    }

    private void OnShutdown(Entity<VgNutritionComponent> ent, ref ComponentShutdown args)
    {
        _alerts.ClearAlertCategory(ent, HungerCategory);
    }

    private void OnHandleState(Entity<VgNutritionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateAlert(ent);
    }

    private void OnRejuvenate(Entity<VgNutritionComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Nutrition = 400;
        ent.Comp.OverEatDuration = 0;
        Dirty(ent);
        UpdateAlert(ent);
    }

    private void OnLifeTick(Entity<VgNutritionComponent> ent, ref VgLifeTickEvent args)
    {
        var comp = ent.Comp;

        if (!_mobState.IsDead(ent))
        {
            // vg13: sleeping burns 25% less.
            var burn = comp.CalorieBurnRate * (HasComp<SleepingComponent>(ent) ? 0.75f : 1f);
            comp.Nutrition = MathF.Max(comp.Nutrition - burn, 0);
        }

        if (comp.Nutrition > VgNutritionComponent.OverEatThreshold)
        {
            if (comp.OverEatDuration < 600)
                comp.OverEatDuration++;
        }
        else if (comp.OverEatDuration > 1)
        {
            // Those already fat take twice as long to unfat.
            comp.OverEatDuration -= comp.Fat ? 1 : 2;
        }

        if (comp.Fat && comp.OverEatDuration < 100)
        {
            comp.Fat = false;
            _popup.Self(ent, Loc.GetString("vg-nutrition-fit-again"), ent);
        }
        else if (!comp.Fat && comp.OverEatDuration > 500)
        {
            comp.Fat = true;
            _popup.Self(ent, Loc.GetString("vg-nutrition-blubbery"), ent);
        }

        Dirty(ent);
        UpdateAlert(ent);
    }

    /// <summary>
    ///     vg13 <c>nutrition += x</c>, never going below 0.
    /// </summary>
    public void AddNutrition(EntityUid uid, float amount, VgNutritionComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false) || amount == 0)
            return;

        comp.Nutrition = MathF.Max(comp.Nutrition + amount, 0);
        Dirty(uid, comp);
        UpdateAlert((uid, comp));
    }

    /// <summary>
    ///     vg13 <c>overeatduration = 0</c>.
    /// </summary>
    public void ResetOvereat(EntityUid uid, VgNutritionComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false) || comp.OverEatDuration == 0)
            return;

        comp.OverEatDuration = 0;
        Dirty(uid, comp);
    }

    public float GetNutrition(EntityUid uid)
    {
        return TryComp<VgNutritionComponent>(uid, out var comp) ? comp.Nutrition : 400;
    }

    private void UpdateAlert(Entity<VgNutritionComponent> ent)
    {
        // vg13 handle_regular_hud_updates(): 250 to 450 shows nothing, 150 to 250 hungry, below that starving.
        var nutrition = ent.Comp.Nutrition;
        if (nutrition >= 150 && nutrition < 250)
            _alerts.ShowAlert(ent, HungryAlert);
        else if (nutrition < 150)
            _alerts.ShowAlert(ent, StarvingAlert);
        else
            _alerts.ClearAlertCategory(ent, HungerCategory);
    }

    private void OnExamined(Entity<VgNutritionComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Nutrition < 100)
            args.PushMarkup(Loc.GetString("vg-nutrition-examine-malnourished", ("ent", ent)));
        else if (ent.Comp.Nutrition >= 500)
            args.PushMarkup(Loc.GetString("vg-nutrition-examine-chubby", ("ent", ent)));
    }
}
