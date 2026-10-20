using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._VG.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VG.Body;

public sealed partial class VgBodySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private static readonly ProtoId<DamageTypePrototype> OxygenDamage = "Asphyxiation";
    private static readonly ProtoId<DamageTypePrototype> ToxinDamage = "Poison";

    // vg13 blood levels, in vg13 units (a full human has 560).
    private const float BloodVolumeSafe = 501;
    private const float BloodVolumeWarn = 392;
    private const float BloodVolumeOkay = 336;
    private const float BloodVolumeBad = 224;
    private const float BloodVolumeSurvive = 122;

    /// <summary>
    ///     vg13: dead or cryo-cooled people don't pump blood.
    /// </summary>
    private const float MinPumpTemperature = 170;

    private static readonly string[] FaintWords = { "dizzy", "woosey", "faint" };

    /// <summary>
    ///     vg13 handle_blood(). The SS14 bloodstream's blood solution is the vessel; vg13 amounts are scaled onto it.
    /// </summary>
    protected override void HandleBlood(Entity<VgBodyComponent> ent)
    {
        if (!ent.Comp.HasBlood || !TryComp<BloodstreamComponent>(ent, out var bloodstream) || _mobState.IsDead(ent))
            return;

        if (TryComp<TemperatureComponent>(ent, out var temperature) && temperature.CurrentTemperature < MinPumpTemperature)
            return;

        FixedPoint2 maxVolume = bloodstream.BloodMaxVolume;
        var scale = maxVolume.Float() / VgBloodVolumeMax;
        var blood = _bloodstream.GetBloodLevelPercentage(ent, bloodstream) * VgBloodVolumeMax;

        // Very slow regeneration.
        // TODO VG: nutriment and iron speed it up, once vg13 chemistry is in.
        if (blood > 0 && blood < VgBloodVolumeMax)
            _bloodstream.TryModifyBloodLevel(ent, FixedPoint2.New(0.1f * scale), bloodstream);

        // A damaged heart doesn't pump the blood properly, so it may as well not be there.
        if (ent.Comp.Organs.Count > 0)
        {
            if (!ent.Comp.Organs.TryGetValue(VgOrgans.Heart, out var heart))
                blood = 0;
            else if (heart.IsBroken)
                blood *= 0.2f;
            else if (heart.IsBruised)
                blood *= 0.6f;
            else if (heart.Damage > 1)
                blood *= 0.8f;
        }

        var oxygen = GetDamageOf(ent, "Oxygen");
        var word = Random.Pick(FaintWords);

        if (blood >= BloodVolumeSafe)
        {
            ent.Comp.Pale = false;
        }
        else if (blood >= BloodVolumeOkay)
        {
            if (!ent.Comp.Pale)
            {
                ent.Comp.Pale = true;
                Feel(ent, Loc.GetString("vg-blood-faint", ("word", word)));
            }
            else if (Random.Prob(blood > BloodVolumeWarn ? 0.01f : 0.03f))
            {
                Feel(ent, Loc.GetString(blood > BloodVolumeWarn ? "vg-blood-faint" : "vg-blood-very-faint", ("word", word)));
            }

            if (oxygen < 20)
                Damage(ent, OxygenDamage, 2);
        }
        else if (blood >= BloodVolumeBad)
        {
            ent.Comp.Pale = true;
            // TODO VG: eye_blurry 2, once vg13 eyes are in.
            Damage(ent, OxygenDamage, oxygen < 40 ? 6 : 3);
            if (Random.Prob(0.15f))
            {
                _status.Paralyse(ent, 1);
                Feel(ent, Loc.GetString("vg-blood-extremely-faint", ("word", word)));
            }
        }
        else if (blood >= BloodVolumeSurvive)
        {
            ent.Comp.Pale = true;
            Damage(ent, OxygenDamage, oxygen < 60 ? 10 : 5);
            Damage(ent, ToxinDamage, 1);
            if (Random.Prob(0.15f))
            {
                _status.Paralyse(ent, Random.Next(1, 4));
                Feel(ent, Loc.GetString("vg-blood-deathly-faint", ("word", word)));
            }
        }
        else
        {
            // Kills you fast, but not too fast.
            ent.Comp.Pale = true;
            Damage(ent, OxygenDamage, 8);
            Damage(ent, ToxinDamage, 2);
            _status.Paralyse(ent, 5);
        }

        Dirty(ent);
    }

    private void Damage(EntityUid uid, ProtoId<DamageTypePrototype> type, float amount)
    {
        _damageable.TryChangeDamage(uid, new DamageSpecifier(Prototypes.Index(type), amount), true, interruptsDoAfters: false);
    }

    private float GetDamageOf(EntityUid uid, string type)
    {
        return TryComp<DamageableComponent>(uid, out var damageable) &&
               damageable.Damage.DamageDict.TryGetValue(type, out var value)
            ? value.Float()
            : 0;
    }

    /// <summary>
    ///     A blood loss message for the mob. The client doesn't predict blood, so the server always sends it.
    /// </summary>
    private void Feel(EntityUid uid, string message)
    {
        _vgPopup.Self(uid, message, null, PopupType.MediumCaution);
    }
}
