# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

vgstation14: a remake of vgstation13 (BYOND/DM) on Space Station 14's Robust Toolbox engine (C#). The repo started as upstream SS14 content and is being turned into a port of vgstation13. A read-only reference checkout of vgstation13 lives at `../vgstation13` (`code/`, `icons/`, `sound/`, `maps/`, `config-example/`).

## Environment

The .NET 8 SDK is installed in `~/.dotnet` and is not on PATH by default:

```
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
```

`RobustToolbox/` is the engine (git submodule). After a fresh clone, run `python3 RUN_THIS.py` to init submodules.

## Commands

- Build (one project per `dotnet build`; it rejects several): `dotnet build Content.Server`, `dotnet build Content.Client`
- Run: `./runserver.sh` and `./runclient.sh` (connects to localhost:1212)
- YAML/prototype linter (~2 min): `dotnet run --project Content.YAMLLinter`
- Unit tests: `dotnet test Content.Tests`
- Integration tests (slow; each test spins up a server+client pair): `dotnet build Content.IntegrationTests && dotnet test Content.IntegrationTests --no-build --filter "FullyQualifiedName~_VG" -- NUnit.NumberOfTestWorkers=1`. Keep one worker: this machine has 7 GB of RAM, and parallel pairs get the test host OOM-killed ("Test host process crashed"). `InteractionTest` subclasses drive real client input, which exercises prediction; test maps are vacuum unless you call `AddAtmosphere()`, so barotrauma adds damage.
- CI builds `DebugOpt` with `/p:WarningsAsErrors=nullable` and maps NUnit warnings to failures.
- RSI validation: `python3 RobustToolbox/Schemas/validate_rsis.py Resources/` (needs `pillow` and `jsonschema`)
- Convert vg13 icons: `python3 Tools/_VG/dmi2rsi.py ../vgstation13/icons/obj/foo.dmi Resources/Textures/_VG/obj/foo.rsi [--states a b]`, or `--batch <dir> <outdir>`. Converted sprites mirror vg13's `icons/` layout under `Resources/Textures/_VG/`.

## Architecture

- **Content.Shared / Content.Server / Content.Client:** standard SS14 ECS. Components hold data; `EntitySystem`s hold logic and communicate through directed events. Shared systems are often `Shared*System` with concrete server/client subclasses. Entities, reagents, damage types and so on are YAML prototypes in `Resources/Prototypes`; strings are Fluent `.ftl` in `Resources/Locale/en-US`.
- **Access analyzer:** `[Access(...)]` on a component makes writes, and even method calls on its fields, from other systems a compile error (RA0002). Copy a field into a local before calling methods on it.
- **Networked state:** components use `[AutoGenerateComponentState]`/`[AutoNetworkedField]`. Collections are shallow-copied, so a predicted component holding mutable classes must deep-copy them in `AfterAutoHandleStateEvent` (see `SharedVgBodySystem.OnHandleState`), or prediction writes into the server state the client rolls back to.
- **Client prediction (user directive):** everything that can be client-predicted must be: new vg code, ported vg13 systems, and any SS14 system we replace or rework. Gameplay logic goes in Shared systems, with server/client subclasses only for what can't be predicted. In this engine version, bloodstream and chat emotes are server-only, and there's no predicted spawn or delete, so guard those with `INetManager.IsServer`. Use `PopupClient`/`PopupPredicted` and `PlayPredicted` with the predicting user, so the server leaves that player out and nobody sees a popup or hears a sound twice. Never use `IRobustRandom` for predicted rolls. Seed a `System.Random` from the tick and `NetEntity` (not `HashCode.Combine`, which is seeded per process). Skip handlers while `IGameTiming.ApplyingState`. CVars the client reads must be `CVar.REPLICATED`.

## vgstation13 port conventions

- New and ported code goes in `_VG` folders: `Content.*/_VG/`, `Resources/Prototypes/_VG/`, `Resources/Locale/en-US/_VG/`, `Resources/Textures/_VG/`, `Resources/Audio/_VG/`, `Tools/_VG/`. Namespaces are `Content.Shared._VG.*` etc.
- `_VG` code is AGPLv3 (`LICENSE-AGPLv3.txt`). Upstream SS14 files stay MIT.
- We keep merging upstream SS14, so keep edits to upstream files minimal and mark each one with a `// VG` comment (`# VG:` in YAML). Partial classes and serialization hooks in `_VG` files are preferred over editing upstream files.
- Copied vg13 assets are CC-BY-SA-3.0. RSI `meta.json` and audio `attributions.yml` must credit vgstation13 at the reference commit.
- vg13 gameplay numbers default to the values in `../vgstation13/config-example/game_options.txt`. Configurable ones are CVars in `Content.Shared/_VG/CCVar/VgCCVars.cs` (`vg.*`).

### Damage model (`Content.Shared/_VG/Damage/`)

- Mob health uses vg13's damage types: **Brute, Burn, Toxin, Oxygen, Clone**.
- Upstream type names (Blunt, Slash, Heat, …) remain valid in YAML and C#. `VgDamageTranslation` converts them when prototypes load, via `ISerializationHooks` on `DamageSpecifier`/`DamageModifierSet`, and again in `DamageableSystem.TryChangeDamage` for specifiers built at runtime.
- Slash/Piercing set vg13 `VgSharpness`/`VgEdge` on the `DamageSpecifier`.
- `VgZone` aims damage at a limb. Use `damage.WithVgZone("l_arm")` rather than mutating shared prototype specifiers.
- Damage groups (Brute/Burn/Airloss/Toxin/Genetic) point at the vg types.
- Code that reads damage dictionaries must use the vg type names.

### Controls (`Content.Shared/_VG/Combat/`, `Content.Client/_VG/Combat/`)

- `VgIntentComponent` (intent + selected zone) replaces SS14 combat mode on humanoids: combat mode stays off and its toggle action is removed. Hotkeys are `VgKeyFunctions` (vg13 defaults in `keybinds.yml`); the HUD selectors are added to the hotbar row by `VgCombatUIController`.
- Clicks go through SS14's predicted `SharedInteractionSystem.UserInteraction`, with two `// VG` hooks: `VgCombatSystem.TryUnarmed` (empty hand on a mob: disarm, grab, punch by intent; help falls through to SS14's hand interaction) and `ItemFallback` (an item no interaction handled hits a mob on any intent, other things on harm). `SharedMeleeWeaponSystem` calls `TryAimMelee` before a light attack lands (zone, vg13 miss chances, vg13 punch damage). Melee and gun "in combat mode" checks also accept intent users; the client gun loop asks `ShouldFireGun`.

### Body (`Content.Shared/_VG/Body/`, in progress)

- `VgBodyComponent` holds vg13 limbs (`VgLimb`: wounds, fractures, internal bleeding, robotic/peg status), laid out by a `vgBody` prototype. Wound kinds are `vgWound` prototypes.
- `DamageableComponent` stays the source of *totals*. `SharedVgBodySystem` (predicted; the server subclass only adds blood loss and screams) subscribes to `DamageChangedEvent` (ordered before `MobThresholdSystem`), routes the brute/burn difference onto limbs with vg13's `apply_damage`/`take_overall_damage`/`heal_overall_damage` rules, then writes back the sum of organic, existing limbs.
- `VgLifeSystem` (Shared) raises `VgLifeTickEvent` every 2 s (vg13's mob `Life()` rate). The client predicts only its own mob's tick. Per-tick organ, wound and blood processing hangs off it.
- SS14's body system is still present during the transition. Severing a vg limb deletes the matching SS14 part so hands and legs actually go away. The plan is to replace SS14's body, metabolism, breathing and digestion with vg13's versions entirely.

## Working with the user on the port

Keep working through the plan without stopping between steps (implement, test, commit, push to the fork, next step); only stop when a decision is needed or you are blocked. The user wants to be asked before any big design decision or large gameplay change. That includes every system that exists in both vg13 and SS14, and whether to keep SS14-only content. Decisions so far, and what's done versus planned, are in `docs/_VG/port-decisions.md`. Update it whenever a new decision is made.

Push to the user's fork (`fork` remote, https://github.com/surgeryp0st/vgstation14). `origin` (vgstation-coders) is read-only for this account.
