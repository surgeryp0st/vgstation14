# vgstation13 medical stacks (code/game/objects/items/stacks/medical.dm).

vg-medical-blocked = You can't apply {THE($item)} through {THE($cover)}!
vg-medical-robotic = This isn't useful at all on a robotic limb.
vg-medical-peg = This isn't useful at all on a peg limb.
vg-medical-apply = {CAPITALIZE(THE($user))} applies {THE($item)} to {THE($target)}.

vg-medical-already-bandaged = The wounds on {THE($target)}'s {$limb} have already been bandaged.
vg-medical-already-treated = The wounds on {THE($target)}'s {$limb} have already been treated.
vg-medical-already-salved = The wounds on {THE($target)}'s {$limb} have already been salved.
vg-medical-open-bandage = {CAPITALIZE(THE($target))}'s {$limb} is cut wide open, you'll need more than a bandage!
vg-medical-open-advanced-bandage = {CAPITALIZE(THE($target))}'s {$limb} is cut wide open, even bioglue won't do!
vg-medical-open-salve = {CAPITALIZE(THE($target))}'s {$limb} is cut wide open, you'll need more than some ointment!
vg-medical-open-advanced-salve = {CAPITALIZE(THE($target))}'s {$limb} is cut wide open, even a regenerative membrane won't do!

vg-medical-bandage-bleeding = {CAPITALIZE(THE($user))} bandages the {$wound} on {THE($target)}'s {$limb}.
vg-medical-bandage-bruise = {CAPITALIZE(THE($user))} places a bruise patch over the {$wound} on {THE($target)}'s {$limb}.
vg-medical-bandage-other = {CAPITALIZE(THE($user))} places a bandaid over the {$wound} on {THE($target)}'s {$limb}.
vg-medical-advanced-bandage-bleeding = {CAPITALIZE(THE($user))} cleans the {$wound} on {THE($target)}'s {$limb} and seals the edges with bioglue.
vg-medical-advanced-bandage-bruise = {CAPITALIZE(THE($user))} disinfects and places a medicine patch over the {$wound} on {THE($target)}'s {$limb}.
vg-medical-advanced-bandage-other = {CAPITALIZE(THE($user))} smears some bioglue over the {$wound} on {THE($target)}'s {$limb}.

vg-medical-salve = {CAPITALIZE(THE($user))} salves the wounds on {THE($target)}'s {$limb}.
vg-medical-advanced-salve = {CAPITALIZE(THE($user))} disinfects the wounds on {THE($target)}'s {$limb} and covers them with a regenerative membrane.

vg-medical-already-splinted = {CAPITALIZE(THE($target))}'s {$limb} is already splinted!
vg-medical-splint-start = [color=red]{CAPITALIZE(THE($user))} starts to apply {THE($item)} to {THE($target)}'s {$limb}.[/color]
vg-medical-splint-start-self = [color=red]{CAPITALIZE(THE($user))} starts to apply {THE($item)} to {POSS-ADJ($user)} {$limb}.[/color]
vg-medical-splint-done = [color=red]{CAPITALIZE(THE($user))} finishes applying {THE($item)} to {THE($target)}'s {$limb}.[/color]
vg-medical-splint-done-self = [color=red]{CAPITALIZE(THE($user))} successfully applies {THE($item)} to {POSS-ADJ($user)} {$limb}.[/color]
vg-medical-splint-fumble = [color=red]{CAPITALIZE(THE($user))} fumbles {THE($item)}.[/color]
