# vgstation13 port: decisions

Decisions made so far for porting vgstation13 into vgstation14. When vg13 and SS14 both have a system, or SS14 has content vg13 doesn't, the call is made case by case and recorded here.

## Ground rules

- **Licensing:** code ported from vgstation13 or written for vgstation14 lives in `_VG` folders and is AGPLv3 (`LICENSE-AGPLv3.txt`). Upstream SS14 files stay MIT. vg13 assets are CC-BY-SA-3.0 and must credit vgstation13 in their `meta.json` / `attributions.yml`.
- **Upstream:** we keep merging space-wizards/space-station-14. vg code stays in `_VG` namespaces and folders, and edits to upstream files are kept minimal and marked `// VG` (`# VG:` in YAML).
- **Overlapping systems:** no default. Each system that exists in both vg13 and SS14 is decided on its own (see below).
- **SS14-only content** (things vg13 doesn't have): decided case by case as it comes up: keep, disable, or remove.
- **Sprites:** vg13 sprites wherever both games have the thing; SS14-only content keeps SS14 art. Convert with `Tools/_VG/dmi2rsi.py` into `Resources/Textures/_VG/`, mirroring vg13's `icons/` layout.
- **Client prediction:** everything that can be client-predicted must be, including SS14 systems we replace or rework. vg13 ran everything on the server, but ported systems put their logic in Shared so the acting player's client predicts it. Only what the engine can't predict yet (bloodstream, chat emotes, spawning) stays server-side.
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
| Body system | **Replace** SS14's body/organ system with vg13's. Order: vg13 internal organs → blood → breathing → chemistry and metabolism → remove SS14's body (hands and legs from vg13 limbs) and convert species. | In progress: internal organs done (heart, lungs, liver, kidneys, brain, appendix, eyes, with vg13 damage from limb hits, kidney filtering, liver toxin damage, bruised lungs, blind broken eyes, brain damage effects); blood done (vg13 regeneration, heart efficiency and blood loss effects on SS14's blood solution, which stays the vessel; SS14's own regeneration, bleeding and bloodloss damage no longer run on vg13 bodies) |
| Metabolism, breathing, digestion | Replace SS14's organ-based versions with vg13's: mob-level reagent metabolism, breathing in the Life() tick, food reagents going straight into the body. | Breathing done: vg13 breath (internals or the tile, 0.5 l), lung gas rules (O2 16-140 kPa, CO2 overload, plasma toxicity with gas-mask relief, N2O), breath temperature burns, losebreath; SS14's respirator no longer runs on vg13 bodies. Metabolism and digestion come with chemistry. |
| Controls | vg13's four intents (help/disarm/grab/harm) and zone selector, replacing SS14 combat mode. Pure vg13: SS14's wide swing (heavy attack) and combat-mode shove are removed; harm is a single click attack, disarm uses vg13's odds. Items no interaction handles hit mobs on any intent and other things on harm intent. Guns fire at anything out of reach on any intent, point blank on adjacent mobs only on harm. Grab intent is an SS14 pull until grab levels are ported; stuns are SS14 knockdowns until the stun model is. | Done (predicted) |
| Zone selector | vg13's body-doll sprite, click regions and hotkeys, placed beside SS14's hotbar/hands so it fits the SS14 HUD layout (not vg13's exact corner). Midnight style (vg13's default). Hotkeys are vg13's: Ctrl+1-4, Insert, Ctrl/Alt+Numpad. | Done |
| Stuns | vg13's model: no stamina; stunned/weakened/paralysis; vg13 disarm odds (40% miss, 40% knockdown). Stun weapons both games have (stun baton, taser) use vg13's numbers: one-hit stuns, ~20 s. Pain and traumatic shock (halloss, pain_shock_stage) come with it. Stuns map onto SS14's status effects with vg13 durations (Life() ticks × 2 s). Not yet: eye blur and jitter (wait on vg13 eyes), painkillers (wait on chemistry). | Done (predicted) |
| SS14-only stamina damage | Disablers, beanbag/rubber/baton-grenade rounds, cane, cowbar, boxing gloves, toy hammer, luminous person, tesla gun, anti-materiel round: their stamina damage becomes vg13 halloss, feeding pain shock. | Done |
| Grabs | vg13's grab levels: passive → aggressive → neck → strangle. A grab rides on an SS14 pull (dragging uses SS14's pull physics), with vg13's grab item in hand: use it or click the victim to tighten, drop it to let go, throw it (aggressive+) to throw them. Struggling free is vg13's 25% (aggressive) / 5% (neck) per try. One grab at a time for now; losebreath is approximated until vg13 breathing is in. | Done (predicted; the grab item spawns server-side) |
| Armor | vg13's armor: melee/bullet/laser/energy/bomb/bio/rad categories, per-limb coverage, additive percentages, armor penetration. SS14-only clothing has its coefficients translated when loaded. Clothing both games have gets vg13's armor values in the armor port. Done through a `vgClothing` table keyed by entity id (upstream clothing YAML untouched; `Tools/_VG/clothing/` regenerates it). Melee and punches check melee armor at the aimed limb; projectiles and hitscan hit the shooter's zone with vg13's miss chance and check bullet or laser armor; explosions are scaled by average bomb armor (until explosions themselves are ported). SS14 armor and worn explosion resistance no longer apply to vg13 mobs. | Done |
| Examine | vg13's examine text for limbs: wounds by stage (bleeding/bandaged/infected), missing limbs, peg and robot limbs, splints, open incisions. Uniform, gloves, shoes and gas masks hide what's under them; bleeding still shows through. Predicted. | Done |
| Wound treatment items | Replace SS14's bruise packs, ointment and gauze with vg13's medical stacks (bruise pack, ointment, advanced trauma/burn kits, splints) and their behaviour: bandage/salve/splint the aimed limb's wounds. SS14's `Brutepack`/`Gauze`/`Ointment` ids are kept as hidden aliases of vg13's roll of gauze and ointment, so maps, fills and recipes give vg13 items. SS14's advanced medkit holds vg13's advanced first-aid kit contents. Predicted. | Done |
| SS14-only medical items | Tourniquet, blood pack, regenerative mesh, medicated suture, aloe cream, healing toolbox: **disabled for now**. Taken out of vendors, lockers and maps, prototypes kept so this can be revisited once vg13 medical is in. References are `# VG:` comments; `migration.yml` drops them from maps; their microwave recipes are commented out. | Done |
| Natural healing | SS14's passive regeneration is removed for organic species; healing happens through vg13 wounds. | Done |

## Chemistry

| Area | Decision | Status |
|---|---|---|
| Chemistry | **Replace** SS14's reagents and recipes with vg13's, like the body system. Infection waits for it (spaceacillin). | In progress: metabolism framework and first medicines (below) |
| Chemistry engine | Keep SS14's solution/reaction engine (containers, machines, transfers, prediction); replace its data: reagents, recipes, effects and metabolism become vg13's. vg13-only engine mechanics (container-specific reactions, slime extracts, ...) are added as `_VG` extensions. | In progress: vg13 reagents are ordinary `reagent` prototypes with vg13's lowercase ids (no clash with SS14's CamelCase ids), plus a `vgReagent` prototype of the same id holding `on_mob_life` as data (metabolism rate, overdose, pain resistance, a list of effects). `VgMetabolismSystem` (server, since the bloodstream is) runs them on the Life() tick over the bloodstream's chemical solution, with vg13's tick counters, overdose and liver-rate removal. Effects so far: heal limb damage, adjust tox/oxy/clone, brain loss, losebreath, purge other reagents. Ported with their vg13 recipes: dylovene, bicaridine, kelotane, dexalin, inaprovaline, tricordrazine (inputs are still SS14's elements until the basic reagents are replaced). Also: synthocarisol, rezadone (tick-ranged effects via `minTick`/`maxTick`; face repair and recipe wait), alkysine, alkycosine, dermaline, dexalin plus, cryoxadone, clonexadone (heal only when the body is cold, via a temperature event answered server-side), and the painkillers paracetamol, oxycodone and tramadol. Painkillers work as in vg13: reagent pain resistance is summed onto `VgStatusComponent` each tick and subtracted from the pain level; oxycodone numbs pain (`pain_numb`) and tramadol speeds shock recovery. Effects have to live in Shared, since the client loads the prototypes too. Next: the rest of vg13's medicines, then the dispensers. |
| Chem dispenser | Decided: replace with vg13's. SS14's jug-based machine and UI stay; the stock list becomes vg13's (iodine out, water and sulfuric acid in; tungsten waits for its reagent). Ported medicines join once the basic elements are vg13's. | In progress |
| SS14-only reagents | Food, drink and juice reagents **stay**, rewritten for vg13 metabolism. SS14-only medicines, toxins, narcotics and fun/misc chems are **disabled** (out of dispensers, recipes, vendors and starting contents; items needing one get the closest vg13 reagent; prototypes kept). Species bloods follow their species; gases (frezon, tritium, N2O) wait for atmos. | Planned |

## Species and movement

| Area | Decision | Status |
|---|---|---|
| Shared species | Become vg13's versions (body layout, organs, flags, stats, sprites): Human, Diona, Vox; SS14 Reptilian becomes Unathi, Slime person becomes vg13 Slime, Skeleton becomes Skellington. | Planned (with the body replacement) |
| vg13-only species | Add the player-selectable ones: Tajaran, Skrell, Grey, Insectoid, Mushroom, Plasmaman. Special ones (Golem, Muton, Ghoul, vampire forms) come with the antags/events that make them. | Planned |
| SS14-only species | Arachnid, Moth, Dwarf, Gingerbread: **disabled for now** (not selectable, prototypes kept, existing characters fall back to Human). | Planned |
| Movement speed | vg13's movement delays: base run/walk speeds and all slowdowns (shock, missing/broken legs, hunger, ...) as in vg13. vg13's delay (tally × multipliers, quantized to its 0.33 ds tick) becomes SS14 speeds: running 7.6 tiles/s, walking 2.3. In: grabs, damage, shock, legs (canes help), bare feet, clothing slowdowns (from the `vgClothing` table, replacing SS14 clothing speed modifiers and SlowOnDamage). Still to come with their systems: hunger, cold, drowsiness, chemicals, floors. | Done (partly) |

