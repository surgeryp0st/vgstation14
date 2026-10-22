verb-categories-vg-debug = VG Debug

vg-debug-verb-readout = Health readout
vg-debug-verb-break-limb = Break limb ({$zone})
vg-debug-verb-sever-limb = Sever limb ({$zone})
vg-debug-verb-internal-bleeding = Internal bleeding ({$zone})
vg-debug-verb-break-organs = Break organs ({$zone})
vg-debug-verb-halloss = Add 50 halloss
vg-debug-verb-stun = Stun (10 ticks)
vg-debug-verb-paralyse = Paralyse (10 ticks)
vg-debug-verb-ignite = Ignite (5 fire stacks)
vg-debug-verb-extinguish = Extinguish
vg-debug-verb-life-ticks = Run 10 Life() ticks

vg-debug-done-break-limb = Broke {$target}'s {$limb}.
vg-debug-done-sever-limb = Severed {$target}'s {$limb}.
vg-debug-done-internal-bleeding = Opened a vein in {$target}'s {$limb}.
vg-debug-done-break-organs = Broke the organs in {$target}'s {$limb}.
vg-debug-done-halloss = Added 50 halloss to {$target}.
vg-debug-done-stun = Stunned {$target} for 10 ticks.
vg-debug-done-paralyse = Paralysed {$target} for 10 ticks.
vg-debug-done-ignite = Set {$target} on fire.
vg-debug-done-extinguish = Put out {$target}.
vg-debug-done-life-ticks = Ran 10 Life() ticks on {$target}.

vg-debug-not-applicable = {$action} doesn't apply to {$target}.
vg-debug-no-limb = {$target} has no {$zone} to do that to.
vg-debug-no-sever-torso = The chest and groin can't be severed.
vg-debug-no-organs = There are no organs in the {$limb}.

vg-debug-readout-header = vg health of {$target}:
vg-debug-readout-damage = damage: brute {$brute}, burn {$burn}, toxin {$toxin}, oxygen {$oxygen}, clone {$clone}
vg-debug-readout-status = halloss {$halloss}, shock stage {$shock}, pain {$pain}, lose breath {$losebreath}, paralysed {$paralysed}
vg-debug-readout-brain = brain loss {$brain}
vg-debug-readout-limb = {"  "}{$limb}: brute {$brute}, burn {$burn}; {$flags}; wounds: {$wounds}
vg-debug-readout-organs = organs: {$organs}
vg-debug-readout-fire = on fire {$onfire}, fire stacks {$stacks}

cmd-vgdebug-desc = Applies a VG Debug action to a mob.
cmd-vgdebug-help = Usage: vgdebug <action> <entity> [zone]
    Actions: readout, break-limb, sever-limb, internal-bleeding, break-organs, halloss, stun, paralyse, ignite, extinguish, life-ticks.
    Limb actions use the zone given, or the one you have selected.
cmd-vgdebug-bad-action = Unknown action {$action}.
cmd-vgdebug-arg-action = <action>
cmd-vgdebug-arg-entity = <entity>
cmd-vgdebug-arg-zone = [zone]
