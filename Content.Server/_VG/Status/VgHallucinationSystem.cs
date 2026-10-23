using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Status;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Flash.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VG.Status;

/// <summary>
///     vg13 handle_hallucinations() and fake_attack() (code/modules/flufftext/Hallucination.dm): from 20 hallucination
///     up, now and then something happens that only the hallucinating player sees or hears. The server picks it and
///     does whatever really happens (a fake death's sleep, a fake flash's stun, a fake attacker's imagined pain); the
///     player's client draws the rest from a <see cref="VgHallucinationEvent"/>.
/// </summary>
public sealed class VgHallucinationSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly VgPopupSystem _popup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    private const string Audio = "/Audio/_VG/Hallucination/";

    /// <summary>
    ///     vg13 MIN_HAL_SLEEP, s.
    /// </summary>
    private const float MinSleep = 3;

    private const float View = 7;

    private static readonly string[] StrangeItems = { "WeaponRevolverDeckard", "C4", "EnergySword", "Stunbaton", "Emag", "GrenadeFlashBang" };

    private static readonly string[] CreepySounds =
    {
        "ghost.ogg", "ghost2.ogg", "heart_beat_single.ogg", "ear_ring_single.ogg", "screech.ogg", "behind_you1.ogg",
        "behind_you2.ogg", "far_noise.ogg", "growl1.ogg", "growl2.ogg", "growl3.ogg", "im_here1.ogg", "im_here2.ogg",
        "i_see_you1.ogg", "i_see_you2.ogg", "look_up1.ogg", "look_up2.ogg", "over_here1.ogg", "over_here2.ogg",
        "over_here3.ogg", "turn_around1.ogg", "turn_around2.ogg", "veryfar_noise.ogg", "wail.ogg",
    };

    private sealed class Attacker
    {
        public int Id;
        public string Name = string.Empty;
        public string? Weapon;
        public EntityUid? Look;
        public EntityCoordinates Where;
        public TimeSpan Next;
        public TimeSpan End;
    }

    private sealed class State
    {
        public TimeSpan Next;
        public readonly List<Attacker> Attackers = new();
    }

    private readonly Dictionary<EntityUid, State> _states = new();
    private int _nextAttacker;

    private List<string>? _foods;
    private List<string>? _monsters;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => (_foods, _monsters) = (null, null));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<VgStatusComponent>();
        while (query.MoveNext(out var uid, out var status))
        {
            if (!_states.TryGetValue(uid, out var state))
            {
                if (status.Hallucination < 20)
                    continue;

                state = _states[uid] = new State { Next = now };
            }

            UpdateAttackers(uid, state, now);

            if (status.Hallucination < 20)
            {
                // vg13: once it's all gone, the hallucinations go too.
                if (status.Hallucination <= 0)
                    EndAll(uid, state);
                continue;
            }

            if (now < state.Next || _mobState.IsIncapacitated(uid) || _status.IsParalysed(uid))
                continue;

            // vg13: sleep(max(MIN_HAL_SLEEP, rand(200, 500) / (hallucination / 25))) deciseconds.
            state.Next = now + TimeSpan.FromSeconds(MathF.Max(MinSleep, _random.Next(200, 501) / (status.Hallucination / 25) / 10));

            if (_random.Prob(0.03f) && state.Attackers.Count < 3)
                FakeAttack(uid, state);

            Hallucinate(uid, _random.Next(1, 107));
        }

        // Forget the ones who are gone.
        foreach (var uid in _states.Keys.ToList())
        {
            if (TerminatingOrDeleted(uid))
                _states.Remove(uid);
        }
    }

    private void EndAll(EntityUid uid, State state)
    {
        foreach (var attacker in state.Attackers)
        {
            Send(uid, new VgHallucinationEvent { Kind = VgHallucinationKind.AttackerGone, Id = attacker.Id });
        }

        _states.Remove(uid);
    }

    /// <summary>
    ///     One go of vg13's switch(rand(1, 106)).
    /// </summary>
    public void Hallucinate(EntityUid uid, int pick)
    {
        switch (pick)
        {
            case <= 15:
                // Screwy HUD: crit, dead, oxygen or toxins.
                Send(uid, new VgHallucinationEvent
                {
                    Kind = VgHallucinationKind.ScrewyHud,
                    Variant = _random.Pick(new[] { 1, 2, 3, 3, 4, 4 }),
                    Duration = _random.Next(10, 26),
                });
                break;
            case <= 25:
                Send(uid, new VgHallucinationEvent
                {
                    Kind = VgHallucinationKind.StrangeItem,
                    Prototype = _random.Pick(StrangeItems),
                    Duration = _random.Next(10, 26),
                });
                break;
            case <= 40:
                DangerFlash(uid);
                break;
            case <= 65:
                StrangeAudio(uid);
                break;
            case <= 70:
                FakeBody(uid);
                break;
            case <= 72:
                // Fake death: really asleep, and the HUD says crit, for 5 to 10 s.
                var duration = _random.Next(5, 11);
                _status.SleepFor(uid, TimeSpan.FromSeconds(duration));
                Send(uid, new VgHallucinationEvent { Kind = VgHallucinationKind.ScrewyHud, Variant = 1, Duration = duration });
                break;
            case <= 75:
                Message(uid, Loc.GetString(_random.Prob(0.0001f) ? "vg-hal-prick-huge" : "vg-hal-prick"));
                break;
            case 76:
                if (_random.Prob(0.05f))
                {
                    Message(uid, Loc.GetString("vg-hal-shuttle-title") + "\n" + Loc.GetString("vg-hal-shuttle"));
                    Sound(uid, "shuttledock.ogg");
                }
                else
                {
                    Message(uid, Loc.GetString("vg-hal-urge",
                        ("verb", Loc.GetString($"vg-hal-urge-verb-{_random.Next(4)}")),
                        ("place", Loc.GetString($"vg-hal-urge-place-{_random.Next(8)}")),
                        ("end", _random.Pick(new[] { "...", "!", "." }))));
                }
                break;
            case 77:
                if (_random.Prob(0.05f))
                {
                    Message(uid, Loc.GetString("vg-hal-delta"));
                    Sound(uid, "aimalf.ogg");
                    _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(uid):player} just got a fake delta AI message from hallucinating");
                }
                else
                {
                    var nanite = _random.Next(1, 11);
                    if (nanite == 5)
                        _chat.TrySendInGameICMessage(uid, Loc.GetString($"vg-hal-nanite-say-{_random.Next(3)}"), InGameICChatType.Speak, false, ignoreActionBlocker: true);
                    else
                        Message(uid, Loc.GetString($"vg-hal-nanite-{nanite}"));
                }
                break;
            case <= 80:
                Message(uid, Loc.GetString($"vg-hal-boo-{_random.Next(8)}"));
                break;
            case 81:
                // Fake flash: a real flash of the screen, and a fifth of the time it floors you.
                Sound(uid, "flash.ogg");
                _statusEffects.TryAddStatusEffect<FlashedComponent>(uid, "Flashed", TimeSpan.FromSeconds(2), true);
                if (_random.Prob(0.2f))
                {
                    _status.Knockdown(uid, 10);
                    _status.Stun(uid, 10);
                }
                break;
            case <= 85:
                Sound(uid, _random.Pick(new[] { "clownstep1.ogg", "clownstep2.ogg" }));
                Sound(uid, _random.Pick(new[] { "clownstep1.ogg", "clownstep2.ogg" }), _random.Next(16, 29) / 10f);
                break;
            case 86:
                if (_random.Prob(0.15f))
                    FoodDisguise(uid);
                break;
            case 87:
                MonsterDisguise(uid);
                break;
            case 88:
                Send(uid, new VgHallucinationEvent
                {
                    Kind = VgHallucinationKind.TurnScreen,
                    Variant = _random.Next(1, 4) * 90,
                    Duration = _random.Next(10, 41),
                });
                Message(uid, Loc.GetString($"vg-hal-turn-{_random.Next(3)}"));
                break;
        }
    }

    private void StrangeAudio(EntityUid uid)
    {
        switch (_random.Next(1, 17))
        {
            case 1: Sound(uid, "airlock.ogg"); break;
            case 2: Sound(uid, _random.Prob(0.5f) ? "Explosion1.ogg" : "Explosion2.ogg"); break;
            case 3: Sound(uid, "explosionfar.ogg"); break;
            case 4: Sound(uid, "Glassbr1.ogg"); break;
            case 5: Sound(uid, "Glassbr2.ogg"); break;
            case 6: Sound(uid, "Glassbr3.ogg"); break;
            case 7: Sound(uid, "twobeep.ogg"); break;
            case 8: Sound(uid, "windowdoor.ogg"); break;
            case 9:
                // Two gunshots: enough to kill.
                Sound(uid, "Gunshot.ogg");
                Sound(uid, "Gunshot.ogg", _random.Next(10, 31) / 10f);
                break;
            case 10: Sound(uid, "smash.ogg"); break;
            case 11:
                Sound(uid, "Taser.ogg");
                Sound(uid, "Taser.ogg", _random.Next(10, 31) / 10f);
                break;
            case 12: Sound(uid, _random.Pick(CreepySounds)); break;
            case 13: Sound(uid, _random.Prob(0.5f) ? "Welder.ogg" : "Welder2.ogg"); break;
            case 14: Sound(uid, _random.Prob(0.5f) ? "Screwdriver.ogg" : "Screwdriver2.ogg"); break;
            case 15: Sound(uid, $"hiss{_random.Next(1, 6)}.ogg"); break;
            default:
                Sound(uid, "ya_dun_clucked.ogg");
                Message(uid, Loc.GetString("vg-hal-sadness"));
                break;
        }
    }

    private void DangerFlash(EntityUid uid)
    {
        if (RandomFloorInView(uid) is not { } where)
            return;

        var ev = new VgHallucinationEvent
        {
            Kind = VgHallucinationKind.DangerFlash,
            Where = GetNetCoordinates(where),
            // vg13: seen only for a moment, 1 to 5 deciseconds... spawn(rand(10, 50)): 1 to 5 s.
            Duration = _random.Next(10, 51) / 10f,
        };

        switch (_random.Next(1, 5))
        {
            case 1:
                ev.Rsi = "/Textures/_VG/turf/space.rsi";
                ev.State = _random.Next(1, 26).ToString();
                break;
            case 2:
                ev.Rsi = "/Textures/_VG/effects/fire.rsi";
                ev.State = "key1";
                break;
            case 3:
                ev.Prototype = "C4";
                break;
            default:
                ev.Prototype = "GrenadeFlashBang";
                break;
        }

        Send(uid, ev);
    }

    private void FakeBody(EntityUid uid)
    {
        if (RandomFloorInView(uid) is not { } where)
            return;

        // vg13's husk_l state doesn't exist, so a quarter of the time nothing shows.
        var (rsi, state) = _random.Next(1, 5) switch
        {
            1 => ((string?) null, (string?) null),
            4 => ("/Textures/_VG/mob/alien.rsi", "alienother"),
            _ => ("/Textures/_VG/mob/human.rsi", "husk_s"),
        };
        if (rsi == null)
            return;

        Send(uid, new VgHallucinationEvent
        {
            Kind = VgHallucinationKind.FakeBody,
            Where = GetNetCoordinates(where),
            Rsi = rsi,
            State = state,
            Duration = _random.Next(50, 81) / 10f,
        });
    }

    private void FoodDisguise(EntityUid uid)
    {
        _foods ??= _proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && !p.HideSpawnMenu && p.Components.ContainsKey("Food") && p.Components.ContainsKey("Sprite"))
            .Select(p => p.ID)
            .ToList();
        if (_foods.Count == 0)
            return;

        var target = uid;
        if (_random.Prob(0.5f) && MobsInView(uid) is { Count: > 0 } mobs)
            target = _random.Pick(mobs);

        var food = _random.Pick(_foods);
        Send(uid, new VgHallucinationEvent
        {
            Kind = VgHallucinationKind.Disguise,
            Target = GetNetEntity(target),
            Prototype = food,
            Duration = _random.Next(60, 121),
        });

        var name = _proto.Index<EntityPrototype>(food).Name;
        Message(uid, Loc.GetString(target == uid ? "vg-hal-food-self" : "vg-hal-food-other", ("food", name)));
    }

    private void MonsterDisguise(EntityUid uid)
    {
        _monsters ??= _proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.Components.ContainsKey("MobState") && IsHostile(p))
            .Select(p => p.ID)
            .ToList();
        if (_monsters.Count == 0 || MobsInView(uid) is not { Count: > 0 } mobs)
            return;

        var target = _random.Pick(mobs);
        var monster = _random.Pick(_monsters);
        Send(uid, new VgHallucinationEvent
        {
            Kind = VgHallucinationKind.Disguise,
            Target = GetNetEntity(target),
            Prototype = monster,
            Duration = _random.Next(60, 121),
        });
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(uid):player} just imagined that {ToPrettyString(target):target} looks like a {monster}");
    }

    private bool IsHostile(EntityPrototype proto)
    {
        if (!proto.TryGetComponent<NpcFactionMemberComponent>(out var faction, _factory))
            return false;

        var factions = faction.Factions;
        return factions.Contains("SimpleHostile");
    }

    /// <summary>
    ///     vg13 fake_attack(): a copy of someone standing comes at you for half a minute.
    /// </summary>
    private void FakeAttack(EntityUid uid, State state)
    {
        EntityUid? clone = null;
        var humans = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent>();
        while (humans.MoveNext(out var human, out _, out _))
        {
            if (!_mobState.IsAlive(human) || _status.IsParalysed(human))
                continue;
            clone = human;
            break;
        }

        if (clone is not { } source)
            return;

        string? weapon = null;
        foreach (var held in _hands.EnumerateHeld(source))
        {
            weapon = Name(held);
            break;
        }

        var attacker = new Attacker
        {
            Id = ++_nextAttacker,
            Name = Name(source),
            Weapon = weapon,
            Look = source,
            Where = StepAway(uid, Transform(uid).Coordinates, 2),
            Next = _timing.CurTime,
            End = _timing.CurTime + TimeSpan.FromSeconds(30),
        };
        state.Attackers.Add(attacker);
        SendAttacker(uid, attacker);
    }

    /// <summary>
    ///     vg13 attack_loop(): every half second to a second, it closes in, and when next to you it hits now and then.
    /// </summary>
    private void UpdateAttackers(EntityUid uid, State state, TimeSpan now)
    {
        for (var i = state.Attackers.Count - 1; i >= 0; i--)
        {
            var attacker = state.Attackers[i];
            if (now >= attacker.End)
            {
                Send(uid, new VgHallucinationEvent { Kind = VgHallucinationKind.AttackerGone, Id = attacker.Id });
                state.Attackers.RemoveAt(i);
                continue;
            }

            if (now < attacker.Next)
                continue;

            attacker.Next = now + TimeSpan.FromSeconds(_random.Next(5, 11) / 10f);
            var target = Transform(uid).Coordinates;
            var offset = _transform.ToMapCoordinates(target).Position - _transform.ToMapCoordinates(attacker.Where).Position;

            if (offset.Length() > 1.5f)
            {
                attacker.Where = attacker.Where.Offset(Step(offset));
            }
            else if (_random.Prob(0.15f))
            {
                Hit(uid, attacker);
            }

            if (_random.Prob(0.15f))
                attacker.Where = StepAway(uid, attacker.Where, 2);

            SendAttacker(uid, attacker);
        }
    }

    private void Hit(EntityUid uid, Attacker attacker)
    {
        var blood = _random.Prob(0.33f);
        if (attacker.Weapon is { } weapon)
        {
            _status.AddHalloss(uid, 8);
            if (_random.Prob(0.2f))
                _status.AdjustBlur(uid, 3);
            Message(uid, Loc.GetString("vg-hal-attacked-with", ("target", uid), ("weapon", weapon), ("attacker", attacker.Name)), PopupType.MediumCaution);
            Send(uid, new VgHallucinationEvent
            {
                Kind = VgHallucinationKind.AttackerHit,
                Id = attacker.Id,
                Sound = Audio + $"genhit{_random.Next(1, 4)}.ogg",
                Where = blood ? GetNetCoordinates(Transform(uid).Coordinates) : null,
                State = $"floor{_random.Next(1, 8)}",
            });
        }
        else
        {
            _status.AddHalloss(uid, 4);
            Message(uid, Loc.GetString("vg-hal-punched", ("target", uid), ("attacker", attacker.Name)), PopupType.MediumCaution);
            Send(uid, new VgHallucinationEvent
            {
                Kind = VgHallucinationKind.AttackerHit,
                Id = attacker.Id,
                Sound = Audio + $"punch{_random.Next(1, 5)}.ogg",
                Where = blood ? GetNetCoordinates(Transform(uid).Coordinates) : null,
                State = $"floor{_random.Next(1, 8)}",
            });
        }
    }

    private void SendAttacker(EntityUid uid, Attacker attacker)
    {
        Send(uid, new VgHallucinationEvent
        {
            Kind = VgHallucinationKind.AttackerMove,
            Id = attacker.Id,
            Where = GetNetCoordinates(attacker.Where),
            Target = attacker.Look is { } look && !TerminatingOrDeleted(look) ? GetNetEntity(look) : null,
        });
    }

    private static Vector2 Step(Vector2 offset)
    {
        return new Vector2(Math.Sign(MathF.Round(offset.X)), Math.Sign(MathF.Round(offset.Y)));
    }

    /// <summary>
    ///     vg13 step_away(src, target, 2): one step further from the target.
    /// </summary>
    private EntityCoordinates StepAway(EntityUid uid, EntityCoordinates from, float distance)
    {
        var away = _transform.ToMapCoordinates(from).Position - _transform.GetWorldPosition(uid);
        if (away.LengthSquared() < 0.01f)
            away = _random.NextAngle().ToVec();

        return away.Length() >= distance ? from : from.Offset(Step(away.Normalized() * 1.4f));
    }

    private EntityCoordinates? RandomFloorInView(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        var center = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var floors = new List<EntityCoordinates>();
        for (var x = -7; x <= 7; x++)
        for (var y = -7; y <= 7; y++)
        {
            var tile = center + new Vector2i(x, y);
            if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            var blocked = false;
            foreach (var ent in _map.GetAnchoredEntities(grid, gridComp, tile))
            {
                if (TryComp<PhysicsComponent>(ent, out var physics) && physics.Hard && physics.CanCollide)
                {
                    blocked = true;
                    break;
                }
            }

            var coords = _map.ToCenterCoordinates(grid, tile, gridComp);
            if (!blocked && _examine.InRangeUnOccluded(uid, coords, View))
                floors.Add(coords);
        }

        return floors.Count == 0 ? null : _random.Pick(floors);
    }

    private List<EntityUid> MobsInView(EntityUid uid)
    {
        var mobs = new HashSet<Entity<MobStateComponent>>();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, View, mobs);
        return mobs.Select(m => m.Owner).Where(m => m == uid || _examine.InRangeUnOccluded(uid, m, View)).ToList();
    }

    private void Message(EntityUid uid, string message, PopupType type = PopupType.Small)
    {
        _popup.Self(uid, message, null, type);
    }

    private void Sound(EntityUid uid, string file, float delay = 0)
    {
        Send(uid, new VgHallucinationEvent { Kind = VgHallucinationKind.Sound, Sound = Audio + file, Delay = delay });
    }

    private void Send(EntityUid uid, VgHallucinationEvent ev)
    {
        if (TryComp<ActorComponent>(uid, out var actor))
            RaiseNetworkEvent(ev, actor.PlayerSession);
    }
}
