"""Generates Resources/Prototypes/_VG/Clothing/clothing.yml: vg13 armor, slowdown and heat protection for SS14 clothing.

Clothing both games have gets vg13's values (MAP, SS14 id -> vg13 path, None = vg13 has it with no armor or slowdown).
SS14-only clothing gets its original SS14 resistances converted to vg13 armor categories, its speed modifier
converted to a vg13 slowdown (a move delay multiplier), and its temperature protection converted to a vg13 heat
conductivity and heat protection temperature.
"""
import json, sys

S = sys.argv[1]
ss14 = {x['id']: x for x in json.load(open(f'{S}/ss14.json'))
        if x['coef'] or x['flat'] or x['speed'] not in (None, 1.0) or x['heating'] is not None}
vg = {x['path']: x for x in json.load(open(f'{S}/vg.json'))}

C = '/obj/item/clothing/'
MAP = {
    # Hardsuits
    'ClothingHeadHelmetHardsuitBasic': 'head/helmet/space/rig', 'ClothingOuterHardsuitBasic': 'suit/space/rig',
    'ClothingHeadHelmetHardsuitAtmos': 'head/helmet/space/rig/atmos', 'ClothingOuterHardsuitAtmos': 'suit/space/rig/atmos',
    'ClothingHeadHelmetHardsuitEngineering': 'head/helmet/space/rig/engineer', 'ClothingOuterHardsuitEngineering': 'suit/space/rig/engineer',
    'ClothingHeadHelmetHardsuitSalvage': 'head/helmet/space/rig/mining', 'ClothingOuterHardsuitSalvage': 'suit/space/rig/mining',
    'ClothingHeadHelmetHardsuitSecurity': 'head/helmet/space/rig/security', 'ClothingOuterHardsuitSecurity': 'suit/space/rig/security',
    'ClothingHeadHelmetHardsuitCap': 'head/helmet/space/rig/capspace', 'ClothingOuterHardsuitCap': 'suit/space/rig/captain',
    'ClothingHeadHelmetHardsuitEngineeringWhite': 'head/helmet/space/rig/engineer/elite', 'ClothingOuterHardsuitEngineeringWhite': 'suit/space/rig/engineer/elite',
    'ClothingHeadHelmetHardsuitMedical': 'head/helmet/space/rig/medical', 'ClothingOuterHardsuitMedical': 'suit/space/rig/medical',
    'ClothingHeadHelmetHardsuitSyndie': 'head/helmet/space/rig/syndi', 'ClothingOuterHardsuitSyndie': 'suit/space/rig/syndi',
    'ClothingHeadHelmetHardsuitSyndieMedic': 'head/helmet/space/rig/syndi', 'ClothingOuterHardsuitSyndieMedic': 'suit/space/rig/syndi',
    'ClothingHeadHelmetHardsuitSyndieElite': 'head/helmet/space/rig/syndicate_elite', 'ClothingOuterHardsuitSyndieElite': 'suit/space/rig/syndicate_elite',
    'ClothingHeadHelmetHardsuitSyndieCommander': 'head/helmet/space/rig/syndi/commander', 'ClothingOuterHardsuitSyndieCommander': 'suit/space/rig/syndi/commander',
    'ClothingHeadHelmetHardsuitWizard': 'head/helmet/space/rig/wizard', 'ClothingOuterHardsuitWizard': 'suit/space/rig/wizard',
    'ClothingHeadHelmetHardsuitPirateEVA': 'head/helmet/space/pirate', 'ClothingOuterHardsuitPirateEVA': 'suit/space/pirate',
    'ClothingHeadHelmetHardsuitPirateCap': 'head/helmet/space/pirate', 'ClothingOuterHardsuitPirateCap': 'suit/space/pirate',
    'ClothingHeadHelmetHardsuitERTLeader': 'head/helmet/space/ert/commander', 'ClothingOuterHardsuitERTLeader': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitERTChaplain': 'head/helmet/space/ert', 'ClothingOuterHardsuitERTChaplain': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitERTEngineer': 'head/helmet/space/ert/engineer', 'ClothingOuterHardsuitERTEngineer': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitERTMedical': 'head/helmet/space/ert/medical', 'ClothingOuterHardsuitERTMedical': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitERTSecurity': 'head/helmet/space/ert/security', 'ClothingOuterHardsuitERTSecurity': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitERTJanitor': 'head/helmet/space/ert', 'ClothingOuterHardsuitERTJanitor': 'suit/space/ert',
    'ClothingHeadHelmetHardsuitDeathsquad': 'head/helmet/space/rig/deathsquad', 'ClothingOuterHardsuitDeathsquad': 'suit/space/rig/deathsquad',
    'ClothingHeadHelmetHardsuitClown': 'head/helmet/space/clown', 'ClothingOuterHardsuitClown': 'suit/space/clown',
    'ClothingHeadHelmetHardsuitSanta': 'head/helmet/space/santahat', 'ClothingOuterHardsuitSanta': 'suit/space/santa',
    'ClothingOuterSuitSpaceNinja': 'suit/space/ninja', 'ClothingHeadHelmetSpaceNinja': 'head/helmet/space/ninja',
    'ClothingHeadHelmetEVA': 'head/helmet/space', 'ClothingHeadHelmetEVALarge': 'head/helmet/space', 'ClothingOuterHardsuitEVA': 'suit/space',
    'ClothingHeadHelmetVoidParamed': 'head/helmet/space/paramedic', 'ClothingOuterHardsuitVoidParamed': 'suit/space/paramedic',
    # Helmets
    'ClothingHeadHelmetBasic': 'head/helmet/tactical/sec',
    'ClothingHeadHelmetSwat': 'head/helmet/tactical/swat', 'ClothingHeadHelmetSwatSyndicate': 'head/helmet/tactical/swat',
    'ClothingHeadHelmetRiot': 'head/helmet/tactical/riot',
    'ClothingHeadHelmetBombSuit': 'head/bomb_hood', 'ClothingHeadHelmetJanitorBombSuit': 'head/bomb_hood',
    'ClothingHeadHelmetCult': 'head/culthood',
    'ClothingHeadHatHoodRad': 'head/radiation',
    'ClothingHeadHatHardhatBlue': 'head/hardhat/dblue', 'ClothingHeadHatHardhatOrange': 'head/hardhat/orange',
    'ClothingHeadHatHardhatRed': 'head/hardhat/red', 'ClothingHeadHatHardhatWhite': 'head/hardhat/white',
    'ClothingHeadHatHardhatYellow': 'head/hardhat', 'ClothingHeadHatHardhatYellowDark': 'head/hardhat',
    'ClothingHeadHatHoodBioGeneral': 'head/bio_hood/general', 'ClothingHeadHatHoodBioCmo': 'head/bio_hood/cmo',
    'ClothingHeadHatHoodBioJanitor': 'head/bio_hood/janitor', 'ClothingHeadHatHoodBioScientist': 'head/bio_hood/scientist',
    'ClothingHeadHatHoodBioSecurity': 'head/bio_hood/security', 'ClothingHeadHatHoodBioVirology': 'head/bio_hood/virology',
    'ClothingMaskSterile': 'mask/surgical',
    'ClothingShoesGaloshes': 'shoes/galoshes',
    # Armor and coats
    'ClothingOuterArmorBasic': 'suit/armor/vest/security', 'ClothingOuterArmorBasicSlim': 'suit/armor/vest',
    'ClothingOuterArmorRiot': 'suit/armor/riot', 'ClothingOuterArmorBulletproof': 'suit/armor/bulletproof',
    'ClothingOuterArmorReflective': 'suit/armor/laserproof', 'ClothingOuterArmorCult': 'suit/cultrobes',
    'ClothingOuterArmorCaptainCarapace': 'suit/storage/armorjacketcapt',
    'ClothingOuterCoatDetective': 'suit/storage/det_suit', 'ClothingOuterCoatDetectiveLoadout': 'suit/storage/det_suit',
    'ClothingOuterCoatDetectiveLoadoutGrey': 'suit/storage/det_suit/noir',
    'ClothingOuterCoatHoSTrench': 'suit/armor/hos/jensen', 'ClothingOuterCoatWarden': 'suit/armor/vest/warden',
    'ClothingOuterVestDetective': 'suit/armor/det_suit',
    'ClothingOuterSuitBomb': 'suit/bomb_suit', 'ClothingOuterSuitJanitorBomb': 'suit/bomb_suit',
    'ClothingOuterSuitRad': 'suit/radiation',
    'ClothingOuterSuitFire': 'suit/fire/firefighter', 'ClothingOuterSuitAtmosFire': 'suit/fire/heavy',
    'ClothingHeadHelmetFire': 'head/hardhat/red', 'ClothingHeadHelmetAtmosFire': 'head/hardhat/red',
    'ClothingOuterBioGeneral': 'suit/bio_suit/general', 'ClothingOuterBioCmo': 'suit/bio_suit/cmo',
    'ClothingOuterBioJanitor': 'suit/bio_suit/janitor', 'ClothingOuterBioScientist': 'suit/bio_suit/scientist',
    'ClothingOuterBioSecurity': 'suit/bio_suit/security', 'ClothingOuterBioVirology': 'suit/bio_suit/virology',
    # Labcoats
    'ClothingOuterCoatLab': 'suit/storage/labcoat', 'ClothingOuterCoatLabOpened': 'suit/storage/labcoat',
    'ClothingOuterCoatLabChem': 'suit/storage/labcoat/chemist', 'ClothingOuterCoatLabChemOpened': 'suit/storage/labcoat/chemist',
    'ClothingOuterCoatLabViro': 'suit/storage/labcoat/virologist', 'ClothingOuterCoatLabViroOpened': 'suit/storage/labcoat/virologist',
    'ClothingOuterCoatLabGene': 'suit/storage/labcoat/genetics', 'ClothingOuterCoatLabGeneOpened': 'suit/storage/labcoat/genetics',
    'ClothingOuterCoatLabCmo': 'suit/storage/labcoat/cmo', 'ClothingOuterCoatLabCmoOpened': 'suit/storage/labcoat/cmo',
    'ClothingOuterCoatRnd': 'suit/storage/labcoat/science', 'ClothingOuterCoatRndOpened': 'suit/storage/labcoat/science',
    'ClothingOuterCoatRobo': 'suit/storage/labcoat', 'ClothingOuterCoatRoboOpened': 'suit/storage/labcoat',
    'ClothingOuterCoatRD': 'suit/storage/labcoat/rd', 'ClothingOuterCoatRDOpened': 'suit/storage/labcoat/rd',
    # Winter coats
    'ClothingOuterWinterCoat': 'suit/storage/wintercoat', 'ClothingOuterWinterCoatToggleable': 'suit/storage/wintercoat',
    'ClothingOuterWinterBar': 'suit/storage/wintercoat/bartender', 'ClothingOuterWinterCap': 'suit/storage/wintercoat/security/captain',
    'ClothingOuterWinterCargo': 'suit/storage/wintercoat', 'ClothingOuterWinterClown': 'suit/storage/wintercoat', 'ClothingOuterWinterMime': 'suit/storage/wintercoat',
    'ClothingOuterWinterCE': 'suit/storage/wintercoat/engineering/ce', 'ClothingOuterWinterCMO': 'suit/storage/wintercoat/medical/cmo',
    'ClothingOuterWinterEngi': 'suit/storage/wintercoat/engineering', 'ClothingOuterWinterAtmos': 'suit/storage/wintercoat/engineering/atmos',
    'ClothingOuterWinterHoP': 'suit/storage/wintercoat/hop', 'ClothingOuterWinterHoS': 'suit/storage/wintercoat/security/hos',
    'ClothingOuterWinterHydro': 'suit/storage/wintercoat', 'ClothingOuterWinterJani': 'suit/storage/wintercoat',
    'ClothingOuterWinterMed': 'suit/storage/wintercoat/medical', 'ClothingOuterWinterPara': 'suit/storage/wintercoat/medical/paramedic',
    'ClothingOuterWinterMiner': 'suit/storage/wintercoat/miner', 'ClothingOuterWinterSec': 'suit/storage/wintercoat/security',
    'ClothingOuterWinterWarden': 'suit/storage/wintercoat/security/warden',
}

# vg13 flags -> VgBodyParts
FLAGS = {'HEAD': 'Head', 'UPPER_TORSO': 'UpperTorso', 'LOWER_TORSO': 'LowerTorso', 'LEGS': 'Legs', 'FEET': 'Feet',
         'ARMS': 'Arms', 'HANDS': 'Hands', 'EYES': 'Eyes', 'MOUTH': 'Mouth', 'EARS': 'Ears', 'BEARD': 'Beard',
         'FULL_TORSO': 'FullTorso', 'FULL_HEAD': 'FullHead', 'FULL_BODY': 'FullBody', 'FACE': 'Eyes|Mouth'}
CATS = ['melee', 'bullet', 'laser', 'energy', 'bomb', 'bio', 'rad']

def vg_cov(s):
    out = []
    for f in (s or '').replace(' ', '').split('|'):
        if f in FLAGS:
            out += FLAGS[f].split('|')
    return sorted(set(out))

def default_cov(entry):
    f = entry['file']
    if '/Head' in f: return ['FullHead'] if 'Hardsuit' in entry['id'] or 'EVA' in entry['id'] else ['Head', 'Eyes', 'Ears']
    if '/Masks' in f: return ['Mouth']
    if '/Hands' in f: return ['Hands']
    if '/Shoes' in f: return ['Feet']
    if '/Back' in f: return []
    if '/Uniforms' in f: return ['FullTorso', 'Arms', 'Legs']
    if '/Eyes' in f: return ['Eyes']
    if 'hardsuit' in f or 'softsuit' in f or 'suits.yml' in f or 'bio.yml' in f: return ['FullTorso', 'Arms', 'Legs', 'Feet', 'Hands']
    if 'armor.yml' in f or 'vests.yml' in f: return ['FullTorso']
    if '/OuterClothing' in f: return ['FullTorso', 'Arms']
    return ['FullTorso']

def convert(entry):
    c = entry['coef']
    get = lambda *ks: min([c.get(k, 1.0) for k in ks])
    pct = lambda v: max(0, round((1 - v) * 100))
    blunt, slash = c.get('Blunt', 1.0), c.get('Slash', 1.0)
    armor = {'melee': pct((blunt + slash) / 2), 'bullet': pct(c.get('Piercing', 1.0)), 'laser': pct(c.get('Heat', 1.0)),
             'energy': pct(c.get('Shock', 1.0)), 'bomb': pct(entry['explosion']) if entry.get('explosion') is not None else 0,
             'bio': pct(get('Caustic', 'Poison')), 'rad': pct(c.get('Radiation', 1.0))}
    return armor

def convert_heat(entry):
    """SS14 temperature protection -> vg13 heat_conductivity and max_heat_protection_temperature."""
    if not entry or entry.get('heating') is None:
        return None, None
    cooling, heating = entry['cooling'], entry['heating']
    conductivity = None if cooling >= 1 else (0.0 if cooling <= 0.01 else round(cooling, 2))
    # Fire suit grade, space suit grade, or none.
    maxheat = 30000 if heating <= 0.005 else 5000 if heating <= 0.1 else None
    return conductivity, maxheat

lines = ['# vgstation13 armor, slowdown and heat protection for clothing, keyed by entity prototype id (children inherit through parents).',
         '# Generated by Tools/_VG/clothing/gen_armor.py: clothing both games have uses vg13\'s values, SS14-only clothing',
         '# has its SS14 resistances and speed modifiers converted.', '']
missing = []
ids = sorted(set(ss14) | set(MAP))
for id_ in ids:
    if id_ in MAP:
        path = MAP[id_]
        if path is None:
            armor, cov, src, slow = {k: 0 for k in CATS}, None, 'vg13 counterpart has no armor', 1.0
            conductivity, maxheat = convert_heat(ss14.get(id_))
        else:
            v = vg.get(C + path)
            if v is None:
                missing.append((id_, path)); continue
            armor, cov, src = {k: int(v['armor'].get(k, 0)) for k in CATS}, vg_cov(v['covered']), C + path
            slow = v.get('slowdown', 1.0)
            conductivity, maxheat = v.get('conductivity'), v.get('maxheat')
        e = ss14.get(id_)
        if cov is None:
            cov = default_cov(e) if e else ['FullTorso', 'Arms']
    else:
        e = ss14[id_]
        # Held objects with speed modifiers aren't worn; anything armored is (wet floor signs fit on heads).
        if 'Fills' in e['file'] or ('/Clothing/' not in e['file'] and not e['coef']):
            continue
        if '/Back' in e['file'] and not e.get('speed'):
            continue
        armor, cov, src = convert(e), default_cov(e), 'converted from SS14'
        conductivity, maxheat = convert_heat(e)
        speed = e.get('speed')
        slow = round(1 / speed, 2) if speed else 1.0
    lines.append('- type: vgClothing')
    lines.append(f'  id: {id_}')
    lines.append(f'  # {src}')
    if any(armor.values()):
        lines.append('  armor: { ' + ', '.join(f'{k}: {armor[k]}' for k in CATS if armor[k]) + ' }')
    lines.append('  coverage: [ ' + ', '.join(cov) + ' ]')
    if slow != 1.0:
        lines.append(f'  slowdown: {slow:g}')
    if conductivity is not None:
        lines.append(f'  heatConductivity: {conductivity:g}')
    if maxheat:
        lines.append(f'  maxHeatProtection: {maxheat:g}')
    lines.append('')

open(sys.argv[2], 'w').write('\n'.join(lines))
print('entries:', sum(1 for l in lines if l.startswith('- type')), 'missing vg paths:', missing)
