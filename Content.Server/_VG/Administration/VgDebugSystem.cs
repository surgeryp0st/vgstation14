using System.Linq;
using System.Text;
using Content.Server.Administration.Managers;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Content.Shared._VG.Life;
using Content.Shared._VG.Status;
using Content.Shared.Administration;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Server._VG.Administration;

/// <summary>
///     What the vg debug verbs and the <c>vgdebug</c> command can do to a mob.
/// </summary>
public enum VgDebugAction : byte
{
    Readout,
    BreakLimb,
    SeverLimb,
    InternalBleeding,
    BreakOrgans,
    Halloss,
    Stun,
    Paralyse,
    Ignite,
    Extinguish,
    LifeTicks,
}

/// <summary>
///     Admin tools for testing the vg13 port: right-click a mob for the "VG Debug" verbs, or use <c>vgdebug</c>.
///     Limb actions hit the zone the admin has selected, or the one given to the command.
/// </summary>
public sealed class VgDebugSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedVgBodySystem _body = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;
    [Dependency] private readonly VgLifeSystem _life = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;

    public static readonly VerbCategory Category = new("verb-categories-vg-debug", null);

    public const float HallossAmount = 50;
    public const float StunTicks = 10;
    public const float FireStacks = 5;
    public const int LifeTickCount = 10;

    private static readonly VgDebugAction[] ZoneActions =
    {
        VgDebugAction.BreakLimb, VgDebugAction.SeverLimb, VgDebugAction.InternalBleeding, VgDebugAction.BreakOrgans,
    };

    /// <summary>
    ///     The action's name in the <c>vgdebug</c> command and in locale ids: BreakLimb is break-limb.
    /// </summary>
    public static string Id(VgDebugAction action)
    {
        return System.Text.RegularExpressions.Regex.Replace(action.ToString(), "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Debug))
            return;

        if (!HasComp<VgBodyComponent>(args.Target) && !HasComp<VgStatusComponent>(args.Target))
            return;

        var session = actor.PlayerSession;
        var target = args.Target;
        var zone = CompOrNull<VgIntentComponent>(args.User)?.Zone ?? VgLimbs.Chest;
        var zoneName = HasComp<VgBodyComponent>(target) && _body.TryGetLimbForZone(target, zone, out var limb)
            ? Loc.GetString(limb.Name)
            : zone;

        foreach (var action in Enum.GetValues<VgDebugAction>())
        {
            var isZone = ZoneActions.Contains(action);
            if (!Applies(target, action))
                continue;

            args.Verbs.Add(new Verb
            {
                Text = isZone
                    ? Loc.GetString($"vg-debug-verb-{Id(action)}", ("zone", zoneName))
                    : Loc.GetString($"vg-debug-verb-{Id(action)}"),
                Category = Category,
                Priority = -(int) action,
                Impact = action == VgDebugAction.Readout ? LogImpact.Low : LogImpact.Medium,
                Act = () => Perform(session, target, action, zone, out _),
            });
        }
    }

    /// <summary>
    ///     Whether an action makes sense on this mob, so it gets a verb.
    /// </summary>
    public bool Applies(EntityUid target, VgDebugAction action)
    {
        return action switch
        {
            VgDebugAction.Readout => true,
            VgDebugAction.BreakLimb or VgDebugAction.SeverLimb or VgDebugAction.InternalBleeding or VgDebugAction.BreakOrgans
                => HasComp<VgBodyComponent>(target),
            VgDebugAction.Halloss or VgDebugAction.Stun or VgDebugAction.Paralyse => HasComp<VgStatusComponent>(target),
            VgDebugAction.Ignite or VgDebugAction.Extinguish => HasComp<FlammableComponent>(target),
            VgDebugAction.LifeTicks => HasComp<VgLifeComponent>(target),
            _ => false,
        };
    }

    /// <summary>
    ///     Does an action and tells the admin (if any) what happened.
    /// </summary>
    public bool Perform(ICommonSession? admin, EntityUid target, VgDebugAction action, string zone, out string result)
    {
        var ok = TryPerform(target, action, zone, out result);
        if (admin != null)
            _chat.DispatchServerMessage(admin, result, suppressLog: true);
        return ok;
    }

    private bool TryPerform(EntityUid target, VgDebugAction action, string zone, out string result)
    {
        if (!Applies(target, action))
        {
            result = Loc.GetString("vg-debug-not-applicable", ("target", target), ("action", Id(action)));
            return false;
        }

        if (action == VgDebugAction.Readout)
        {
            result = Readout(target);
            return true;
        }

        if (ZoneActions.Contains(action))
        {
            var body = Comp<VgBodyComponent>(target);
            if (!_body.TryGetLimbForZone((target, body), zone, out var limb) || !limb.IsExisting)
            {
                result = Loc.GetString("vg-debug-no-limb", ("target", target), ("zone", zone));
                return false;
            }

            _body.BeginUnpredicted(target);
            switch (action)
            {
                case VgDebugAction.BreakLimb:
                    _body.Fracture((target, body), limb);
                    break;
                case VgDebugAction.SeverLimb:
                    if (limb.IsTorso)
                    {
                        result = Loc.GetString("vg-debug-no-sever-torso");
                        return false;
                    }
                    _body.DropLimb((target, body), limb, force: true);
                    break;
                case VgDebugAction.InternalBleeding:
                    _body.AddInternalBleeding((target, body), limb);
                    Dirty(target, body);
                    break;
                case VgDebugAction.BreakOrgans:
                    var organs = body.Organs.Values.Where(o => o.Parent == limb.Id).ToList();
                    if (organs.Count == 0)
                    {
                        result = Loc.GetString("vg-debug-no-organs", ("limb", Loc.GetString(limb.Name)));
                        return false;
                    }
                    foreach (var organ in organs)
                    {
                        _body.TakeOrganDamage((target, body), organ, Math.Max(0, organ.MinBrokenDamage - organ.Damage));
                    }
                    break;
            }

            result = Loc.GetString($"vg-debug-done-{Id(action)}", ("target", target), ("limb", Loc.GetString(limb.Name)));
            return true;
        }

        switch (action)
        {
            case VgDebugAction.Halloss:
                _status.AdjustHalloss(target, HallossAmount);
                break;
            case VgDebugAction.Stun:
                _status.Stun(target, StunTicks);
                break;
            case VgDebugAction.Paralyse:
                _status.Paralyse(target, StunTicks);
                break;
            case VgDebugAction.Ignite:
                _flammable.AdjustFireStacks(target, FireStacks, ignite: true);
                break;
            case VgDebugAction.Extinguish:
                _flammable.Extinguish(target);
                break;
            case VgDebugAction.LifeTicks:
                var life = Comp<VgLifeComponent>(target);
                for (var i = 0; i < LifeTickCount && !TerminatingOrDeleted(target); i++)
                {
                    _life.RunTick((target, life));
                }
                break;
        }

        result = Loc.GetString($"vg-debug-done-{Id(action)}", ("target", target));
        return true;
    }

    /// <summary>
    ///     Everything vg-specific about a mob's health, one line per limb.
    /// </summary>
    public string Readout(EntityUid target)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.GetString("vg-debug-readout-header", ("target", target)));

        if (TryComp<DamageableComponent>(target, out var damageable))
        {
            var dict = damageable.Damage.DamageDict;
            float Get(string type) => dict.TryGetValue(type, out var v) ? v.Float() : 0;
            sb.Append('\n').Append(Loc.GetString("vg-debug-readout-damage",
                ("brute", Get("Brute")), ("burn", Get("Burn")), ("toxin", Get("Toxin")), ("oxygen", Get("Oxygen")),
                ("clone", Get("Clone"))));
        }

        if (TryComp<VgStatusComponent>(target, out var status))
        {
            sb.Append('\n').Append(Loc.GetString("vg-debug-readout-status",
                ("halloss", status.Halloss), ("shock", status.ShockStage), ("pain", _status.GetPainLevel(target, status)),
                ("losebreath", status.LoseBreath), ("paralysed", _status.IsParalysed(target) ? "yes" : "no")));
        }

        if (TryComp<VgBodyComponent>(target, out var body))
        {
            sb.Append('\n').Append(Loc.GetString("vg-debug-readout-brain", ("brain", body.BrainLoss)));

            foreach (var limb in body.Limbs.Values)
            {
                var flags = new List<string>();
                if (!limb.IsExisting)
                    flags.Add("severed");
                if ((limb.Status & VgLimbStatus.Broken) != 0)
                    flags.Add("broken");
                if ((limb.Status & VgLimbStatus.Splinted) != 0)
                    flags.Add("splinted");
                if ((limb.Status & VgLimbStatus.Bleeding) != 0)
                    flags.Add("bleeding");
                if (limb.InternallyBleeding)
                    flags.Add("internal bleeding");
                if (limb.IsRobotic)
                    flags.Add("robotic");
                if (limb.IsPeg)
                    flags.Add("peg");

                var wounds = string.Join(", ", limb.Wounds.Select(w =>
                    $"{w.Prototype} s{w.Stage}{(w.Amount > 1 ? $" x{w.Amount}" : "")} {w.Damage:0.#}"));

                sb.Append('\n').Append(Loc.GetString("vg-debug-readout-limb",
                    ("limb", Loc.GetString(limb.Name)), ("brute", limb.BruteDamage), ("burn", limb.BurnDamage),
                    ("flags", flags.Count == 0 ? "-" : string.Join(", ", flags)),
                    ("wounds", wounds.Length == 0 ? "-" : wounds)));
            }

            if (body.Organs.Count > 0)
            {
                var organs = string.Join(", ", body.Organs.Values.Select(o =>
                    $"{Loc.GetString(o.Name)} {o.Damage:0.#}{(o.IsBroken ? " (broken)" : o.IsBruised ? " (bruised)" : "")}"));
                sb.Append('\n').Append(Loc.GetString("vg-debug-readout-organs", ("organs", organs)));
            }
        }

        if (TryComp<FlammableComponent>(target, out var flammable))
        {
            sb.Append('\n').Append(Loc.GetString("vg-debug-readout-fire",
                ("onfire", flammable.OnFire ? "yes" : "no"), ("stacks", flammable.FireStacks)));
        }

        return sb.ToString();
    }
}
