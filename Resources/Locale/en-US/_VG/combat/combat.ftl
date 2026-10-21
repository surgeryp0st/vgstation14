# vgstation13 attack messages.

vg-attack-punch = [color=red]{CAPITALIZE(THE($user))} {$verb} {THE($target)}![/color]
vg-attack-knockdown = [color=red][bold]{CAPITALIZE(THE($user))} has knocked down {THE($target)}![/bold][/color]
vg-attack-miss-unarmed = [color=orange][bold]{CAPITALIZE(THE($user))} misses {THE($target)}![/bold][/color]
vg-attack-miss-item = [color=orange][bold]{CAPITALIZE(THE($user))} misses {THE($target)} with {THE($item)}![/bold][/color]
vg-attack-miss-missing-limb = [color=orange][bold]{CAPITALIZE(THE($user))} misses {THE($target)} with {THE($item)} due to aiming at where {POSS-ADJ($target)} [color=red]{$limb}[/color] used to be![/bold][/color]
vg-attack-hit-item = [color=red]{CAPITALIZE(THE($user))} attacks {THE($target)} in the {$limb} with {THE($item)}![/color]

vg-disarm-slap = [color=red]{CAPITALIZE(THE($user))} slaps {THE($target)} in the face![/color]
vg-disarm-miss = [color=red]{CAPITALIZE(THE($user))} has attempted to disarm {THE($target)}![/color]
vg-disarm-push = [color=red][bold]{CAPITALIZE(THE($user))} has pushed {THE($target)}![/bold][/color]
vg-disarm-break-pull = [color=red]{CAPITALIZE(THE($user))} has broken {POSS-ADJ($target)} grip on {THE($pulled)}![/color]
vg-disarm-success = [color=red]{CAPITALIZE(THE($user))} has disarmed {THE($target)}![/color]

vg-grab-passive = [color=orange]{CAPITALIZE(THE($user))} grabs {THE($target)} passively![/color]
vg-grab-aggressive = [color=orange]{CAPITALIZE(THE($user))} has grabbed {THE($target)} aggressively (now hands)![/color]
vg-grab-neck = [color=orange]{CAPITALIZE(THE($user))} has reinforced {POSS-ADJ($user)} grip on {THE($target)} (now neck)![/color]
vg-grab-strangle-start = [color=red][bold]{CAPITALIZE(THE($user))} starts to tighten {POSS-ADJ($user)} grip on {THE($target)}'s neck![/bold][/color]
vg-grab-strangle = [color=red][bold]{CAPITALIZE(THE($user))} has tightened {POSS-ADJ($user)} grip on {THE($target)}'s neck![/bold][/color]
vg-grab-strangle-fail = [color=orange]{CAPITALIZE(THE($user))} was unable to tighten {POSS-ADJ($user)} grip on {THE($target)}'s neck![/color]
vg-grab-break-free = [color=red][bold]{CAPITALIZE(THE($target))} has broken free of {THE($user)}'s grip![/bold][/color]
vg-grab-break-headlock = [color=red][bold]{CAPITALIZE(THE($target))} has broken free of {THE($user)}'s headlock![/bold][/color]
vg-grab-buckled = You cannot grab {THE($target)}, {SUBJECT($target)} {CONJUGATE-BE($target)} buckled in!

vg-intent-help = help
vg-intent-disarm = disarm
vg-intent-grab = grab
vg-intent-harm = harm
vg-zone-selector-tooltip = Damage zone: {$zone}
vg-intent-selector-tooltip = Intent: {$intent}

# Key binds
ui-options-header-vg-combat = Intents and targeting
ui-options-function-vg-intent-help = Help intent
ui-options-function-vg-intent-disarm = Disarm intent
ui-options-function-vg-intent-grab = Grab intent
ui-options-function-vg-intent-harm = Harm intent
ui-options-function-vg-intent-cycle = Cycle intent
ui-options-function-vg-zone-head = Target head
ui-options-function-vg-zone-eyes = Target eyes
ui-options-function-vg-zone-mouth = Target mouth
ui-options-function-vg-zone-chest = Target chest
ui-options-function-vg-zone-groin = Target groin
ui-options-function-vg-zone-left-arm = Target left arm
ui-options-function-vg-zone-right-arm = Target right arm
ui-options-function-vg-zone-left-hand = Target left hand
ui-options-function-vg-zone-right-hand = Target right hand
ui-options-function-vg-zone-left-leg = Target left leg
ui-options-function-vg-zone-right-leg = Target right leg
ui-options-function-vg-zone-left-foot = Target left foot
ui-options-function-vg-zone-right-foot = Target right foot

vg-attack-verb-punches = punches
vg-attack-verb-scratches = scratches
vg-attack-verb-claws = claws
