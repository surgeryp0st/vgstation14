# vgstation13 port: decisions

Decisions made so far for porting vgstation13 into vgstation14. When vg13 and SS14 both have a system, or SS14 has content vg13 doesn't, the call is made case by case and recorded here.

## Ground rules

- **Licensing:** code ported from vgstation13 or written for vgstation14 lives in `_VG` folders and is AGPLv3 (`LICENSE-AGPLv3.txt`). Upstream SS14 files stay MIT. vg13 assets are CC-BY-SA-3.0 and must credit vgstation13 in their `meta.json` / `attributions.yml`.
- **Upstream:** we keep merging space-wizards/space-station-14. vg code stays in `_VG` namespaces and folders, and edits to upstream files are kept minimal and marked `// VG` (`# VG:` in YAML).
- **Overlapping systems:** no default. Each system that exists in both vg13 and SS14 is decided on its own (see below).
- **SS14-only content** (things vg13 doesn't have): decided case by case as it comes up: keep, disable, or remove.
- **Sprites:** vg13 sprites wherever both games have the thing; SS14-only content keeps SS14 art. Convert with `Tools/_VG/dmi2rsi.py` into `Resources/Textures/_VG/`, mirroring vg13's `icons/` layout.
- **Client prediction:** use SS14 client prediction wherever possible. vg13 ran everything on the server, but ported systems put their logic in Shared so the acting player's client predicts it. Only what the engine can't predict yet (bloodstream, chat emotes, spawning) stays server-side.
- **Gameplay numbers:** default to vg13's shipped `config-example/game_options.txt`. Tunables are `vg.*` CVars.

## Plan

1. **Tooling:** dmi → rsi converter (done), dmm → SS14 map converter (next).
2. **Core simulation:** health/damage and combat first, then atmos, power and the rest. Each one is decided separately.
3. **Departments:** chemistry, items/clothing, food, hydroponics, medical, research, mining, virology, genetics, robotics, cargo.
4. **Antagonists:** vg13's Dynamic mode and its factions/roles.
5. **Maps:** convert vg13 stations.
6. **Long tail:** spacepods, fission reactor, telescience, traders, and so on.

## Health, damage and combat

Decided: the **full vg13 model**.

| Area | Decision | Status |
|---|---|---|
| Damage types | vg13's Brute, Burn, Toxin, Oxygen, Clone. Brain damage lives on the brain organ and halloss in the pain system, not as damage types, as in vg13. | Done |
| Upstream damage content | SS14 type names stay valid in YAML/C# and are translated when prototypes load: Blunt→Brute; Slash→Brute with sharpness 1 + edge; Piercing→Brute with sharpness 1; Heat/Cold/Shock/Caustic→Burn; Poison/Radiation→Toxin; Asphyxiation/Bloodloss→Oxygen; Cellular→Clone. Structural and Holy are unchanged. | Done |
| Limbs | Per-limb damage on vg13's 11 limbs (including groin): wounds, fractures, internal bleeding, dismemberment, infection, robotic and peg limbs. Only organic, attached limbs count towards health. Fractures and dismemberment are on, as in vg13's config. | In progress |
| Body system | **Replace** SS14's body/organ system with vg13's. | Planned |
| Metabolism, breathing, digestion | Replace SS14's organ-based versions with vg13's: mob-level reagent metabolism, breathing in the Life() tick, food reagents going straight into the body. | Planned |
| Controls | vg13's four intents (help/disarm/grab/harm) and zone selector, replacing SS14 combat mode. | Planned |
| Stuns | vg13's model: no stamina; stunned/weakened/paralysis; vg13 disarm odds (40% miss, 40% knockdown). | Planned |
| Grabs | vg13's grab levels: passive → aggressive → neck → strangle. | Planned |
| Armor | vg13's armor: melee/bullet/laser/energy/bomb/bio/rad categories, per-limb coverage, additive percentages, armor penetration. SS14-only clothing has its coefficients translated when loaded. | Planned |
| Natural healing | SS14's passive regeneration is removed for organic species; healing happens through vg13 wounds. | Done |
