# vg13 armor table

`Resources/Prototypes/_VG/Armor/armor.yml` gives SS14 clothing vgstation13 armor, keyed by entity prototype id.
Regenerate it from the repo root with:

```
python3 Tools/_VG/armor/ss14_armor.py /tmp/armor/ss14.json        # SS14 clothing with Armor
python3 -c "import json; d=json.load(open('/tmp/armor/ss14.json')); json.dump([x for x in d if x['coef'] or x['flat']], open('/tmp/armor/ss14_real.json','w'))"
python3 Tools/_VG/armor/vg_armor.py /tmp/armor/vg.json            # vg13 clothing armor (needs ../vgstation13)
python3 Tools/_VG/armor/gen_armor.py /tmp/armor Resources/Prototypes/_VG/Armor/armor.yml
```

`gen_armor.py` holds the hand-made `MAP` of SS14 ids to their vg13 counterparts (whose armor and coverage are used
as-is); everything else armored is converted from its SS14 resistances.
