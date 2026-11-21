ghost-role-information-zakat-name = Zakat
ghost-role-information-zakat-description = You are a sentient disease of flesh. Pick your first two hosts, gather biomass, grow symptoms in them and spread until the whole station is yours.
ghost-role-information-zakat-rules = You are a major antagonist. Your goal is to infect the entire station crew.

alerts-zakat-biomass-name = Biomass
alerts-zakat-biomass-desc = The flesh you have gathered.

disease-spread-contact = Contact

# only Zakat itself sees these
zakat-infect-not-organic = There is no living flesh here.
zakat-infect-already = { CAPITALIZE(THE($target)) } is already your host.
zakat-infect-start-success = { CAPITALIZE(THE($target)) } is now your host. Picks left: { $left }.
zakat-not-enough-biomass = Not enough biomass: you need { $cost }.
zakat-growth-success = The core grows.
zakat-enslave-success = { CAPITALIZE(THE($target)) } is now a hive slave.
zakat-symptom-bought = { $symptom } takes root in { $target }.
zakat-host-not-infected = That is not your host.
zakat-new-host = New host: { $target } (+{ $gain } biomass).
zakat-victory = The station is consumed. Every living crew member is your host.
zakat-extinct = You have no hosts left. Zakat fades away.

# neural map
zakat-shop-title = Core
zakat-shop-center = ZAKAT
zakat-host-title = Host
zakat-header = Biomass: { $biomass }
zakat-cost = { $cost }
zakat-growth = Growth
zakat-growth-level = Growth { $level }
zakat-enslave = Enslavement
zakat-enslave-caption = enslavement · { $cost }
zakat-slave = hive slave

zakat-branch-0 = Lungs
zakat-branch-1 = Gut
zakat-branch-2 = Skin and mind
zakat-branch-3 = Organs

zakat-symptom-sneeze = Sneezing
zakat-symptom-cough = Coughing
zakat-symptom-pneumonia = Pneumonia
zakat-symptom-lungfibrosis = Lung fibrosis
zakat-symptom-pulmonaryedema = Pulmonary edema
zakat-symptom-nausea = Nausea
zakat-symptom-immunesuppression = Immune suppression
zakat-symptom-sweating = Sweating
zakat-symptom-dysentery = Dysentery
zakat-symptom-bloodyvomit = Bloody vomit
zakat-symptom-rash = Rash
zakat-symptom-skinlesions = Skin lesions
zakat-symptom-psychosis = Psychosis
zakat-symptom-epilepsy = Epilepsy
zakat-symptom-tachycardia = Tachycardia
zakat-symptom-hemophilia = Hemophilia
zakat-symptom-rotblood = Rotten blood
zakat-symptom-organdecay = Organ decay

zakat-mutation-spaceresistance = Space resistance
zakat-mutation-nopain = No pain
zakat-mutation-regeneration = Regeneration
zakat-mutation-livingarmor = Living armor
zakat-mutation-rotgrowths = Rotten growths
zakat-mutation-parasiteheart = Parasite heart
zakat-mutation-claws = Claws
zakat-mutation-meatharpoon = Meat harpoon
zakat-mutation-devourer = Devourer

# popups for the host
zakat-sym-pneumonia = Your lungs clench - you can't breathe in!
zakat-sym-edema = Your lungs fill with fluid, you are choking!
zakat-sym-bloody-vomit = You vomit blood!
zakat-sym-rash = Your whole body itches.
zakat-sym-skin = Your skin cracks and bleeds!
zakat-sym-psychosis-drop = Your hands open on their own.
zakat-sym-psychosis-stupor = The world freezes. You can't move.
zakat-sym-psychosis-hit = Something inside makes you hit yourself!
zakat-sym-epilepsy = Your body seizes up!

zakat-sym-rotblood = Your blood tastes of rot.
zakat-sym-organdecay = Something inside you is rotting.

# hive slave
zakat-slave-briefing = The voice of the hive fills your head. You are a hive slave. Carry the plague on: all living flesh must join the hive.
zakat-mutation-grown-spaceresistance = Your flesh no longer needs air or warmth.
zakat-mutation-grown-nopain = The pain is gone.
zakat-mutation-grown-regeneration = Your wounds start closing on their own.
zakat-mutation-grown-livingarmor = Living armor hardens under your skin.
zakat-mutation-grown-rotgrowths = Your body is covered in rotten growths.
zakat-mutation-grown-parasiteheart = A second heart beats in your chest.
zakat-mutation-grown-claws = Bone claws sprout from your fingers.
zakat-mutation-grown-meatharpoon = A meat harpoon coils in your arm.
zakat-mutation-grown-devourer = You hunger for dead flesh.
zakat-harpoon-hit = The meat harpoon sinks into { $target }!
zakat-devour-not-corpse = That is not a corpse.
zakat-devour-success = The corpse is torn to pieces!
zakat-possess-start = You are in someone else's body.
zakat-possess-victim = Your body no longer obeys you.
zakat-bioshmat-burst = A lump of flesh bursts out of { $target }'s chest!
zakat-bioshmat-not-corpse = You can't crawl in there.
zakat-bioshmat-infest = The lump of flesh crawls into { $target }!
zakat-bioshmat-grown = The lump of flesh grows into a man of meat.
zakat-flesh-humanoid-name = flesh humanoid

collective-mind-zakat-hivemind = Hive mind
roles-antag-zakat-slave-name = Hive slave
roles-antag-zakat-slave-objective = Infect as much organic life as you can.
objective-issuer-zakat = [color=#d01028]Zakat[/color]
ghost-role-information-zakat-bioshmat-name = Bioshmat
ghost-role-information-zakat-bioshmat-description = You are a lump of hive flesh. Crawl into a corpse or grow yourself a body.

zakat-roundend-unknown = unknown
zakat-roundend = The sentient virus [color=#d01028]Zakat[/color] ({ $player }) was in this round: { $percent }% of the crew infected ({ $crewInfected } of { $crew }, peak { $peak }%), { $infected } hosts in total, { $immune } immune. { $victory ->
        [vines] [color=#d01028]The flesh vines devoured the station. Zakat wins.[/color]
        [yes] [color=#d01028]The station was consumed.[/color]
       *[no] The station held out.
    }

## Growth 3: birth, flesh, buildings
zakat-birth = The host tears apart, and living mass rises from its remains.
zakat-capture-too-far = The flesh can't reach here.
zakat-attack-armored = The flesh can not break through this armored door.
zakat-build-no-flesh = This needs free living flesh.

## Growth 4
zakat-growth-locked = There is not enough flesh yet.
zakat-growth-locked-hosts = There are not enough hosts yet.
zakat-vines-burst = Predatory flesh vines burst out of the core!
zakat-cocoon-hatch = The cocoon bursts!
zakat-defeat = The core is destroyed. The flesh stiffens and dies.
zakat-praetorian-bad-target = That body won't hold.
zakat-praetorian-born = The body rebuilds itself. You are a praetorian of the hive.
zakat-praetorian-claws-out = Bone claws slide out of your fingers.
zakat-praetorian-claws-in = The claws slide back in.
zakat-absorb-start = Tentacles wrap around { $target }.
zakat-absorb-bad-target = That can't be absorbed.
zakat-shift = Face and body change.
zakat-shift-nobody = There is no one to become.
zakat-absorb-burst = Tentacles tear out of the body of { $user }!
zakat-absorb-pull = The tentacles draw { $target } into the body.
zakat-absorb-done = The body is absorbed. Its look can be taken: { $name }.
zakat-shift-selected = Look picked: { $name }.
zakat-slave-claws-out = Bone claws crunch out of the fingers.
zakat-slave-claws-in = The claws slide back into the hand.
zakat-slave-claws-no-hand = You need a free hand.
zakat-hunter-devour-start = The hunter bites into { $target }!
zakat-hunter-devour-bad-target = That is not prey.
zakat-host-praetorian = praetorian
ghost-role-information-zakat-hunter-name = Hunter
ghost-role-information-zakat-hunter-description = You are a hunter of the hive. Guard the flesh and devour the living.
ghost-role-information-zakat-flesh-humanoid-name = Flesh creature
ghost-role-information-zakat-flesh-humanoid-description = You are a creature of the hive. Guard the flesh and the core.

## Cure
zakat-diagnosis-title = [head=3]Sample analysis[/head]
zakat-diagnosis-subject = Sample taken from: { $name }
zakat-diagnosis-positive = [color=#b01020]Infectious disease "ZAKAT" detected[/color]
zakat-diagnosis-negative = No infectious diseases detected.
zakat-vaccinator-clean = There is nothing in the sample to weaken.
zakat-vaccine-subject = Made for: [color=yellow]{ $name }[/color]
zakat-vaccine-used = The vaccine is spent.
zakat-vaccine-wrong = This vaccine was made for someone else.
zakat-vaccine-injected = You inject { $target } with the vaccine.
zakat-vaccine-rollback = You feel a little better.
zakat-vaccine-cured = The illness recedes.
