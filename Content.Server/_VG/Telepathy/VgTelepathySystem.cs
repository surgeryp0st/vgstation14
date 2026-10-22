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
        _ui.SetUiState(ent.Owner, VgTelepathyUiKey.Key, new VgTelepathyBuiState(Names(ent)));
    }

    private void OnSend(Entity<VgTelepathyComponent> ent, ref VgTelepathySendMessage args)
    {
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
