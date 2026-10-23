# vg13 clothing table (armor, slowdown, heat protection)

`Resources/Prototypes/_VG/Clothing/clothing.yml` gives SS14 clothing vgstation13 armor, keyed by entity prototype id.
Regenerate it from the repo root with:

```
python3 Tools/_VG/clothing/ss14_armor.py /tmp/armor/ss14.json        # SS14 clothing with Armor, speed or temperature protection
python3 Tools/_VG/clothing/vg_armor.py /tmp/armor/vg.json            # vg13 clothing armor (needs ../vgstation13)
python3 Tools/_VG/clothing/gen_armor.py /tmp/armor Resources/Prototypes/_VG/Clothing/clothing.yml
```

`gen_armor.py` holds the hand-made `MAP` of SS14 ids to their vg13 counterparts (whose armor and coverage are used
as-is); everything else armored is converted from its SS14 resistances, and SS14 temperature protection becomes a
vg13 heat conductivity (the cooling coefficient) and heat protection temperature (fire suit grade, 30000 K, at a heating
coefficient of 0.005 or less; space suit grade, 5000 K, up to 0.1).
