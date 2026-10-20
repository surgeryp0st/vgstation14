# vgstation13 human examine(): limbs, wounds, prosthetics and blood.

vg-examine-splint = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} a splint on {POSS-ADJ($target)} {$limb}![/color]

vg-examine-limb-missing = [color=red][bold]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} missing {POSS-ADJ($target)} {$limb}.[/bold][/color]
vg-examine-limb-open = [color=lightblue]{CAPITALIZE(POSS-ADJ($target))} {$limb} is [color=red][bold]wide open[/bold][/color].[/color]
vg-examine-limb-wounds = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} {$wounds} on {POSS-ADJ($target)} {$limb}.[/color]

vg-examine-limb-peg = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} a peg {$limb}![/color]
vg-examine-limb-peg-damaged = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} a peg {$limb}, it has {$damage}![/color]
vg-examine-peg-brute-light = some marks
vg-examine-peg-brute-heavy-1 = a lot of damage
vg-examine-peg-brute-heavy-2 = severe cracks and splintering
vg-examine-peg-burn-light = some burns
vg-examine-peg-burn-heavy-1 = a lot of burns
vg-examine-peg-burn-heavy-2 = severe charring

vg-examine-limb-robot = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} a robot {$limb}![/color]
vg-examine-limb-robot-damaged = [color=red]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-HAVE($target)} a robot {$limb}, it has {$damage}![/color]
vg-examine-robot-brute-light = some dents
vg-examine-robot-brute-heavy-1 = a lot of dents
vg-examine-robot-brute-heavy-2 = severe denting
vg-examine-robot-burn-light = some burns
vg-examine-robot-burn-heavy-1 = a lot of burns
vg-examine-robot-burn-heavy-2 = severe melting

vg-examine-and = {" "}and{" "}

# One kind of wound, by how many of it there are. $maybe: whether it's hard to make out.
vg-examine-wound-one = { $maybe ->
    [true] what might be { $vowel ->
        [true] an
       *[false] a
    } {$wound}
   *[false] { $vowel ->
        [true] an
       *[false] a
    } {$wound}
}
vg-examine-wound-pair = { $maybe ->
    [true] what might be a pair of {$wound}s
   *[false] a pair of {$wound}s
}
vg-examine-wound-several = several {$wound}s
vg-examine-wound-bunch = a bunch of {$wound}s
vg-examine-wound-ton = a ton of {$wound}s

vg-examine-wound-bleeding = bleeding {$wound}
vg-examine-wound-bandaged = bandaged {$wound}
vg-examine-wound-badly-infected = badly infected {$wound}
vg-examine-wound-lightly-infected = lightly infected {$wound}

# Injuries under clothing only show when they bleed through.
vg-examine-blood-face = [color=red]{CAPITALIZE(THE($target))} {CONJUGATE-HAVE($target)} blood running down {POSS-ADJ($target)} face![/color]
vg-examine-blood-clothing = [color=red][bold]{CAPITALIZE(THE($target))} {CONJUGATE-HAVE($target)} blood soaking through from under {POSS-ADJ($target)} clothing![/bold][/color]
vg-examine-blood-shoes = [color=red][bold]{CAPITALIZE(THE($target))} {CONJUGATE-HAVE($target)} blood running from {POSS-ADJ($target)} shoes![/bold][/color]
vg-examine-blood-gloves = [color=red][bold]{CAPITALIZE(THE($target))} {CONJUGATE-HAVE($target)} blood running from under {POSS-ADJ($target)} gloves![/bold][/color]
