using Content.Server.Chat.Managers;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared._VG.Telepathy;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Bed.Sleep;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._VG.Telepathy;

/// <summary>
///     vg13's telepathy: <c>mob/heard()</c> records who a mob has heard speak or examined, and the spell sends a message
///     into the head of one of them (or all of them) as long as their mind can be reached.
/// </summary>
public sealed class VgTelepathySystem : EntitySystem
{
    private const int MaxMessageLength = 500;

    /// <summary>
    ///     vg13 SPEECH_RANGE.
    /// </summary>
    private const float SpeechRange = 7;

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VgTelepathyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VgTelepathyComponent, ListenEvent>(OnListen);
        SubscribeLocalEvent<VgTelepathyComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<VgTelepathyComponent, VgTelepathySendMessage>(OnSend);
        SubscribeLocalEvent<VgTelepathyComponent, VgTelepathySetTargetsMessage>(OnSetTargets);
        SubscribeLocalEvent<MobStateComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<VgTelepathyComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);

        // Hearing speech needs a listener.
        EnsureComp<ActiveListenerComponent>(ent);
    }

    private void OnListen(Entity<VgTelepathyComponent> ent, ref ListenEvent args)
    {
        if (_mobState.IsAlive(ent) && !HasComp<SleepingComponent>(ent))
            Heard(ent, args.Source);
    }

    private void OnExamined(Entity<MobStateComponent> ent, ref ExaminedEvent args)
    {
        if (TryComp<VgTelepathyComponent>(args.Examiner, out var telepathy))
            Heard((args.Examiner, telepathy), ent);
    }

    /// <summary>
    ///     vg13 /mob/living/carbon/heard(): a named mob with a mind is remembered the first time it's heard.
    /// </summary>
    private void Heard(Entity<VgTelepathyComponent> ent, EntityUid other)
    {
        if (other == ent.Owner || !HasComp<HumanoidAppearanceComponent>(other)
            || !TryComp<MindContainerComponent>(other, out var mind) || !mind.HasMind)
            return;

        ent.Comp.HeardBefore.TryAdd(Name(other), other);
    }

    private List<string> Names(Entity<VgTelepathyComponent> ent)
    {
        var names = new List<string>();
        foreach (var (name, target) in ent.Comp.HeardBefore)
        {
            if (!Deleted(target))
                names.Add(name);
        }

        names.Sort();
        return names;
    }

    private void OnOpened(Entity<VgTelepathyComponent> ent, ref BoundUIOpenedEvent args)
    {
        _ui.SetUiState(ent.Owner, VgTelepathyUiKey.Key, new VgTelepathyBuiState(Names(ent), ent.Comp.RedirectSpeech));
    }

    private void OnSend(Entity<VgTelepathyComponent> ent, ref VgTelepathySendMessage args)
    {
        if (ent.Comp.RedirectSpeech)
            return;

        var message = args.Message.Trim();
        if (message.Length == 0)
            return;

        if (message.Length > MaxMessageLength)
            message = message[..MaxMessageLength];

        var targets = new List<EntityUid>();
        if (args.Target == null)
        {
            foreach (var (_, target) in ent.Comp.HeardBefore)
            {
                if (!Deleted(target))
                    targets.Add(target);
            }
        }
        else if (ent.Comp.HeardBefore.TryGetValue(args.Target, out var target) && !Deleted(target))
        {
            targets.Add(target);
        }

        var reached = new List<EntityUid>();
        foreach (var target in targets)
        {
            if (!CanMindInteract(ent, target))
            {
                Tell(ent, Loc.GetString("vg-telepathy-unable", ("target", target)));
                continue;
            }

            var heard = HasComp<VgTelepathyComponent>(target)
                ? Loc.GetString("vg-telepathy-hear-named", ("name", Name(ent)), ("message", FormattedMessage.EscapeText(message)))
                : Loc.GetString("vg-telepathy-hear", ("message", FormattedMessage.EscapeText(message)));
            Tell(target, heard);
            reached.Add(target);
        }

        if (reached.Count == 0)
            return;

        var names = string.Join(", ", reached.ConvertAll(e => Name(e)));
        Tell(ent, Loc.GetString("vg-telepathy-projected", ("targets", names), ("message", FormattedMessage.EscapeText(message))));
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(ent):user} projects their mind towards {names}: {message}");
    }

    private void OnSetTargets(Entity<VgTelepathyComponent> ent, ref VgTelepathySetTargetsMessage args)
    {
        if (!ent.Comp.RedirectSpeech)
            return;

        ent.Comp.Targets.Clear();
        ent.Comp.Mode = args.Mode;
        if (args.Mode == VgTelepathyMode.Specific)
        {
            if (args.Target != null && ent.Comp.HeardBefore.TryGetValue(args.Target, out var target) && !Deleted(target))
            {
                if (CanMindInteract(ent, target))
                    ent.Comp.Targets.Add(target);
                else
                    Tell(ent, Loc.GetString("vg-telepathy-unable", ("target", target)));
            }
        }

        Tell(ent, Loc.GetString("vg-telepathy-targets-set"));
    }

    /// <summary>
    ///     vg13 mushroom <c>silent_speech()</c>: a mob with no mouth speaks into the minds of its chosen targets (everyone
    ///     heard before, or those of them in speech range, or one person) instead of out loud. With nobody chosen it
    ///     only hears itself. Returns whether the message was taken.
    /// </summary>
    public bool TryRedirectSpeech(EntityUid user, string message)
    {
        if (!TryComp<VgTelepathyComponent>(user, out var telepathy) || !telepathy.RedirectSpeech)
            return false;

        if (_mobState.IsDead(user))
        {
            Tell(user, Loc.GetString("vg-telepathy-must-be-alive"));
            return true;
        }

        if (!_mobState.IsAlive(user) || HasComp<SleepingComponent>(user))
        {
            Tell(user, Loc.GetString("vg-telepathy-must-be-conscious"));
            return true;
        }

        var targets = new List<EntityUid>();
        if (telepathy.Mode == VgTelepathyMode.Specific)
        {
            foreach (var target in telepathy.Targets)
            {
                if (!Deleted(target))
                    targets.Add(target);
            }
        }
        else
        {
            var userXform = Transform(user);
            foreach (var (_, target) in telepathy.HeardBefore)
            {
                if (Deleted(target))
                    continue;

                var xform = Transform(target);
                if (telepathy.Mode == VgTelepathyMode.Local
                    && (xform.MapID != userXform.MapID || (xform.WorldPosition - userXform.WorldPosition).Length() > SpeechRange))
                    continue;

                targets.Add(target);
            }
        }

        if (targets.Count == 0)
            targets.Add(user);

        message = FormattedMessage.EscapeText(message);
        var reached = new List<EntityUid>();
        foreach (var target in targets)
        {
            if (!CanMindInteract(user, target))
                continue;

            Tell(target, Loc.GetString("vg-telepathy-mushroom-hear", ("name", Name(user)), ("message", message)));
            reached.Add(target);
        }

        if (reached.Exists(e => e != user))
        {
            var names = string.Join(", ", reached.ConvertAll(e => Name(e)));
            Tell(user, Loc.GetString("vg-telepathy-projected", ("targets", names), ("message", message)));
            _adminLog.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(user):user} projects their mind towards {names}: {message}");
        }

        return true;
    }

    /// <summary>
    ///     vg13 can_mind_interact(): the target must be alive, on the same level and awake.
    /// </summary>
    private bool CanMindInteract(EntityUid user, EntityUid target)
    {
        if (!_mobState.IsAlive(target) && !_mobState.IsCritical(target))
        {
            Tell(user, Loc.GetString("vg-telepathy-dead"));
            return false;
        }

        if (Transform(target).MapID != Transform(user).MapID)
        {
            Tell(user, Loc.GetString("vg-telepathy-far"));
            return false;
        }

        if (!_mobState.IsAlive(target) || HasComp<SleepingComponent>(target))
        {
            Tell(user, Loc.GetString("vg-telepathy-unconscious"));
            return false;
        }

        // TODO VG: M_PSY_RESIST and tinfoil/stun/martian helmets block it ("The target mind is resisting!").
        return true;
    }

    private void Tell(EntityUid to, string message)
    {
        if (!TryComp<ActorComponent>(to, out var actor))
            return;

        _chat.ChatMessageToOne(ChatChannel.Local, message, message, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }
}
