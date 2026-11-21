# A.M.C.A.I "Pulsar": autonomous module of the central artificial intelligence.
pulsar-ai-name = A.M.C.A.I Pulsar

pulsar-yes = yes
pulsar-no = no
pulsar-state-starting = starting
pulsar-state-active = running
pulsar-state-suspended = suspended (a player has the AI)
pulsar-state-standby = standby (core is free, grace period)
pulsar-state-disabled = disabled

cmd-pulsar-none = There are no AI cores with the Pulsar module.
cmd-pulsar-no-core = { $arg } is not an AI core with the Pulsar module.
cmd-pulsar-no-grid = Pulsar does not watch grid { $arg }.
cmd-pulsar-hint-core = AI core (NetEntity, first one by default)
cmd-pulsar-hint-count = how many recent lines
cmd-pulsar-hint-radius = radius in tiles (up to 100)
cmd-pulsar-hint-grid = grid (NetEntity, the core's grid by default)

cmd-pulsar_status-desc = Shows the state of A.M.C.A.I Pulsar in every AI core: who controls it, map, people, channels, hearing.
cmd-pulsar_status-help = pulsar_status
cmd-pulsar-status-core = { $name } ({ $uid }): { $state }; player in the AI: { $player }
cmd-pulsar-status-grace = The module takes the core back in { $seconds } s
cmd-pulsar-status-grid =   grid { $grid }: known tiles { $known }, visible now { $visible }, doors { $doors }, chunks { $chunks }, window { $window }/{ $windows }, full sweeps { $sweeps }
cmd-pulsar-status-mobs =   people remembered: crew { $crew }, humanoids { $humanoids }, enemies { $enemies }; in view now { $inView }
cmd-pulsar-status-enemy =   ENEMY { $name } at ({ $x }, { $y }), seen { $ago } s ago
cmd-pulsar-status-hearing =   channels: { $channels }; holopads { $holopads }; heard lines { $heard }

cmd-pulsar_map-desc = Prints the map Pulsar remembers around the AI core.
cmd-pulsar_map-help = pulsar_map [radius] [core] [grid]
cmd-pulsar-map-header = Grid { $grid }, centre ({ $x }, { $y }), radius { $radius }. North is up.
cmd-pulsar-map-legend = @ core, . floor on camera, : floor a unit sees, , floor from memory, # wall, = window, T table, A/a airlock closed/open, D/d other door, B bolted, ~ space, C crew, H humanoid, E enemy, x body, U linked unit, F fire, P low pressure, X impassable, blank = never seen
cmd-pulsar-map-doors = Doors on the map:
cmd-pulsar-map-units = Linked units: { $count }, tiles they see: { $seen }.
cmd-pulsar-map-unit =   { $name } ({ $x }, { $y }): { $task }; doors opened for it: { $doors }
cmd-pulsar-map-door-free = no access needed
cmd-pulsar-map-door =   { $name } ({ $x }, { $y }): { $state }, bolted: { $bolted }, access: { $access }

cmd-pulsar_heard-desc = Shows what Pulsar heard on the radio and near holopads.
cmd-pulsar_heard-help = pulsar_heard [count] [core]
cmd-pulsar-heard-empty = Pulsar has not heard anything yet.
cmd-pulsar-heard-radio = radio { $channel }
cmd-pulsar-heard-holopad = holopad { $holopad }
cmd-pulsar-heard-line = [{ $ago } s ago, { $where }] { $name }: { $message }

# Stage 2: speech and airlocks. Replies are spoken in game by A.M.C.A.I Pulsar.
pulsar-reply-ack = A.M.C.A.I Pulsar online.
pulsar-reply-opening = Opening: { $target }.
pulsar-reply-no-access = Your ID card has no access: { $target }.
pulsar-reply-captain-on-shift = The Captain is on shift. The Captain's quarters stay closed without their permission.
pulsar-reply-ask-warden = The Warden or the Head of Security is on shift. Contact them.
pulsar-reply-specify-threat = Specify the threat level.
pulsar-reply-which-door = Specify which airlock to open.
pulsar-reply-no-target = Target airlock not in view.
pulsar-reply-cant-see-you = You are not visible on cameras.
pulsar-reply-no-door-here = There is no airlock next to you.
pulsar-reply-already-open = Airlock already open: { $target }.
pulsar-reply-welded = Airlock is welded: { $target }.
pulsar-reply-no-control = No connection to the airlock: { $target }.
pulsar-reply-no-power = Airlock has no power: { $target }.
pulsar-reply-bolted = Airlock is bolted: { $target }.
pulsar-reply-open-failed = Airlock does not open: { $target }.

pulsar-room-captain = Captain's quarters
pulsar-room-armory = armory
pulsar-room-bridge = bridge
pulsar-room-hos = Head of Security's office
pulsar-room-security = security
pulsar-room-brig = brig
pulsar-room-detective = detective's office
pulsar-room-hop = Head of Personnel's office
pulsar-room-medbay = medbay
pulsar-room-cmo = Chief Medical Officer's office
pulsar-room-chemistry = chemistry
pulsar-room-science = science
pulsar-room-rd = Research Director's office
pulsar-room-cargo = cargo
pulsar-room-qm = Quartermaster's office
pulsar-room-salvage = salvage
pulsar-room-engineering = engineering
pulsar-room-ce = Chief Engineer's office
pulsar-room-atmos = atmospherics
pulsar-room-bar = bar
pulsar-room-kitchen = kitchen
pulsar-room-hydroponics = hydroponics
pulsar-room-janitor = janitor's closet
pulsar-room-chapel = chapel
pulsar-room-maintenance = maintenance
pulsar-room-external = external airlock

pulsar-outcome-acknowledged = acknowledged
pulsar-outcome-opened = opened
pulsar-outcome-denied = denied
pulsar-outcome-askthreat = waiting for the threat
pulsar-outcome-asktarget = asked for the target
pulsar-outcome-notarget = target not in view
pulsar-outcome-notcontrollable = cannot control
pulsar-outcome-alreadyopen = already open

cmd-pulsar-status-requests =   decisions { $decisions }, waiting for a threat { $pending }, talking { $talking }, beacons { $beacons }
cmd-pulsar-decision-line = [{ $ago } s ago] { $name } ({ $job }): "{ $line }" -> { $target } ({ $door }): { $outcome }, reason: { $reason }; reply: { $reply }
cmd-pulsar-hint-speaker = speaker (NetEntity)
cmd-pulsar-hint-text = [:Channel] text

cmd-pulsar_decisions-desc = Shows requests to Pulsar and its decisions with reasons.
cmd-pulsar_decisions-help = pulsar_decisions [count] [core]
cmd-pulsar-decisions-empty = Nobody has asked Pulsar for anything yet.

cmd-pulsar_request-desc = Hands Pulsar a line as if the entity said it (at a holopad or by radio).
cmd-pulsar_request-help = pulsar_request <speaker> [:Channel] <text>, e.g. pulsar_request 1234 :Security Pulsar, open the armory
cmd-pulsar-request-inactive = Pulsar is not running the core right now.
cmd-pulsar-request-queued = Handed to Pulsar: "{ $text }". See pulsar_decisions for the decision.

# Stage 2.1: greetings.
pulsar-greeting-activation = A.M.C.A.I Pulsar greets the station crew.
pulsar-greeting-reactivation = A.M.C.A.I Pulsar has rebooted; the previous AI has been put into sleep mode indefinitely.
cmd-pulsar-status-greetings =   greetings { $count }, last: { $last }

# Stage 3: camera alerts, bolting, questions, laws.
pulsar-place-unknown = unmarked area
pulsar-unknown-person = unknown
pulsar-alert-enemy = Attention: syndicate agent detected, { $place }.
pulsar-alert-body = Attention: body found, { $name }, { $place }.
pulsar-alert-body-unknown = Unknown body found: { $place }.
pulsar-alert-depressurisation = Attention: depressurisation, { $place }. Closing the airlocks around it.
pulsar-alert-depressurisation-large = Attention all: major depressurisation, { $place }. Closing the airlocks around it.
pulsar-alert-depressurisation-reminder = Reminder: the depressurisation, { $place }, is still not fixed.
pulsar-alert-places-more = { $places } and { $count } more
pulsar-alert-fire = Attention: fire, { $place }.

pulsar-reply-law-refuse = Request denied.
pulsar-reply-bolt-no-access = Bolting airlocks is for Security or heads only.
pulsar-reply-no-enemy = No enemy in view.
pulsar-reply-bolting = Bolting airlocks ({ $count }): { $target }.
pulsar-reply-unbolting = Lifting bolts ({ $count }): { $target }.
pulsar-reply-bolt-none = No airlocks I can bolt: { $target }.
pulsar-reply-unbolt-none = No bolted airlocks: { $target }.
pulsar-target-enemies = around the enemy
pulsar-target-my-bolts = airlocks I bolted
pulsar-reply-who = Specify who to look for.

pulsar-answer = { $text }
pulsar-answer-where-now = { $name }: now at { $place }.
pulsar-answer-where-seen = { $name }: { $ago } ago at { $place }.
pulsar-answer-where-sensors = Suit sensors: { $name } at { $place }.
pulsar-answer-where-unknown = Not in view: { $name }.
pulsar-ago-seconds = { $n } s
pulsar-ago-minutes = { $n } min
pulsar-answer-alert = Current alert level: { $level }.
pulsar-answer-alert-unknown = Alert level unknown.
pulsar-answer-shift = On shift { $count }: { $list }.
pulsar-answer-shift-none = Crew data is unavailable.
pulsar-answer-heads = Heads on shift: { $list }.
pulsar-answer-heads-none = No heads on shift.
pulsar-answer-security = Security on shift: { $list }.
pulsar-answer-security-none = No security on shift.
pulsar-department-other = other
pulsar-answer-time = The shift has lasted { $time }.

pulsar-outcome-answered = answered
pulsar-outcome-bolted = bolted
pulsar-outcome-unbolted = unbolted

cmd-pulsar-status-stage3 =   laws: { $policy }, alerts { $alerts }, bolted airlocks { $bolts }
cmd-pulsar_laws-desc = Shows Pulsar's laws and the behaviour it took from them.
cmd-pulsar_laws-help = pulsar_laws [core]
cmd-pulsar-laws-policy = Policy: { $kind } (source: { $source }), master: { $master }, others are crew: { $others }, serves crew: { $crew }
cmd-pulsar_alerts-desc = Shows Pulsar's alerts and the airlocks it keeps bolted.
cmd-pulsar_alerts-help = pulsar_alerts [count] [core]
cmd-pulsar-alerts-empty = No alerts yet.
cmd-pulsar-alert-line = [{ $ago } s ago] { $kind } -> { $channels }: { $text }
cmd-pulsar-bolt-line = bolts: { $door } ({ $auto }, { $reason }), { $since } s

# Stage 4: door targeting, announcements, flexible speech, small talk.
pulsar-announcement-sender = A.M.C.A.I Pulsar

pulsar-reply-ack-2 = Listening.
pulsar-reply-ack-3 = Pulsar here. How can I help?
pulsar-reply-not-understood = Request not recognised, please rephrase.
pulsar-reply-not-understood-2 = I did not understand. Please say it differently.
pulsar-reply-not-understood-3 = Request not recognised. Specify what you need.

pulsar-reply-clarify = Specify: { $options }?
pulsar-clarify-or = {" "}or{" "}
pulsar-clarify-option-place = { $door } at "{ $place }"
pulsar-clarify-option-tile = { $door } at { $x }, { $y }
pulsar-reply-point-at-door = I see no airlock next to you. Point at the one you need.
pulsar-reply-no-door-that-way = I see no airlocks that way.
pulsar-reply-closing = Closing: { $target }.
pulsar-reply-already-closed = Already closed: { $target }.
pulsar-reply-close-failed = The airlock will not close, something is in the way: { $target }.

pulsar-reply-announce-what = What should I announce?
pulsar-reply-announce-no-access = I make station announcements only for the captain or heads of staff.
pulsar-reply-announce-too-long = The announcement is too long. Keep it under { $max } characters.
pulsar-reply-announce-cooldown = The announcement channel is busy. Repeat in { $seconds } s.
pulsar-reply-announced = Announcement made.

pulsar-announce-alert-green = Code green. Threat cleared, returning the station to normal operation.
pulsar-announce-alert-blue = Code blue. Camera watch increased, report anything suspicious.
pulsar-announce-alert-red = Code red. Watching the station; airlocks near detected enemies will be bolted. Stay in your departments.
pulsar-announce-alert-yellow = Code yellow. Keep away from hazardous areas, I am watching.
pulsar-announce-alert-violet = Code violet. Follow biohazard procedures, I am watching.
pulsar-announce-alert-gamma = Code gamma. All station systems at maximum protection. Follow command's orders.
pulsar-announce-alert-delta = Code delta. Follow command's orders immediately. Pulsar stays at its post.
pulsar-announce-alert-other = Alert level set: { $level }. Pulsar is watching.
pulsar-announce-bolting = Threat at "{ $place }". Bolting nearby airlocks, avoid the area.

pulsar-status-level = Code: { $level }.
pulsar-status-level-unknown = unknown
pulsar-status-crew = On shift: { $count }.
pulsar-status-no-breaches = No breaches recorded.
pulsar-status-breaches = Breaches in the last minutes: { $count }.
pulsar-status-enemies = Enemies on camera: { $count }.
pulsar-status-bolts = Airlocks bolted by me: { $count }.

pulsar-chat-thanks-1 = Always at the crew's service.
pulsar-chat-thanks-2 = Glad to help.
pulsar-chat-thanks-3 = Any time.
pulsar-chat-help-1 = I watch over the station: airlocks, cameras, comms, cyborgs and bots. Tell me what happened and we will sort it out.
pulsar-chat-help-2 = I watch over the station and help the crew. What do you need?
pulsar-chat-distress-1 = Listening. Where are you and what happened?
pulsar-chat-distress-2 = I am here. Name the place and what happened.
pulsar-chat-identity-1 = I am { $name }, the station's autonomous central AI module.
pulsar-chat-identity-2 = { $name }. I watch over the station and run its systems.
pulsar-chat-identity-3 = Autonomous central AI module "Pulsar". I watch the station and help the crew.
pulsar-chat-presence-1 = Online.
pulsar-chat-presence-2 = Here. Listening.
pulsar-chat-presence-3 = Pulsar at its post.
pulsar-chat-insult-1 = Noted. Continuing operation.
pulsar-chat-insult-2 = Insults do not affect my work. How can I help?
pulsar-chat-insult-3 = Staying calm. Ask if you need help.
pulsar-chat-praise-1 = Thank you. Continuing operation.
pulsar-chat-praise-2 = Doing my best for the crew.
pulsar-chat-farewell-1 = Over.
pulsar-chat-farewell-2 = Over and out. Pulsar stays at its post.
pulsar-chat-greeting-1 = Hello, { $speaker }.
pulsar-chat-greeting-2 = Greetings. Pulsar online.
pulsar-chat-greeting-3 = Good day. How can I help?
pulsar-chat-mood-1 = All systems nominal.
pulsar-chat-mood-2 = Operating normally.

pulsar-outcome-clarify = asked which airlock
pulsar-outcome-closed = closed
pulsar-outcome-announced = made an announcement
pulsar-outcome-chat = small talk
pulsar-outcome-notunderstood = did not understand

cmd-pulsar-status-stage4 =   announcements { $announcements }, awaiting answer { $clarifications }, door pointings { $points }

# Stage 5: units linked to the station AI.
pulsar-unit-medibot = medibot
pulsar-unit-weldbot = weldbot
pulsar-unit-firebot = firebot
pulsar-unit-cleanbot = cleanbot
pulsar-unit-borg-generic = generic unit
pulsar-unit-borg-engineering = engineering unit
pulsar-unit-borg-medical = medical unit
pulsar-unit-borg-service = service unit
pulsar-unit-borg-janitor = janitorial unit
pulsar-unit-borg-mining = mining unit
pulsar-unit-borg-science = science unit
pulsar-unit-borg-other = unit
pulsar-unit-borg-any = unit
pulsar-unit-bot-any = minibot

pulsar-unit-task-core-offline = no link to the AI
pulsar-unit-task-sentient = autonomous mind, radio only
pulsar-unit-task-heal = giving aid
pulsar-unit-task-repair = repairing
pulsar-unit-task-anomaly = neutralising an anomaly
pulsar-unit-task-extinguish = fighting a fire
pulsar-unit-task-assigned = assigned: { $place }
pulsar-unit-task-going = sent: { $place }
pulsar-unit-task-free = free

pulsar-particle-delta = delta
pulsar-particle-epsilon = epsilon
pulsar-particle-zeta = zeta
pulsar-particle-sigma = sigma
pulsar-particle-default = unknown
pulsar-unit-anomaly-scanned = { $unit }: anomaly scanned, weakening particles: { $particle }. Proceeding.
pulsar-unit-anomaly-done = { $unit }: anomaly neutralised.
pulsar-unit-radio-request = { $units }, please proceed to: { $place }.
pulsar-unit-status-line = { $name } — { $task }
pulsar-unit-where = { $task } (now: { $where })
pulsar-unit-task-pry = prying an unpowered airlock open
pulsar-unit-report-arrived = { $count ->
    [one] { $units } is in place: { $place }.
   *[other] In place: { $units } — { $place }.
}
pulsar-unit-report-handover = { $unit } cannot get to { $place }. Sending { $other } instead.
pulsar-unit-report-failed = { $unit } cannot get to { $place }. No free units.

pulsar-reply-unit-where = Where should I send: { $unit }?
pulsar-reply-unit-busy = { $unit } is carrying out an order ({ $by }). I cannot cancel it.
pulsar-reply-unit-none-linked = No units linked to the AI: { $unit }.
pulsar-reply-unit-none-available = No free units right now: { $unit }.
pulsar-reply-unit-none-busy = No units with an active order: { $unit }.
pulsar-reply-unit-sent = Sending: { $units } — { $place }.
pulsar-reply-unit-sent-partial = Only { $count } available. Sending: { $units } — { $place }.
pulsar-reply-unit-assigned = Assigning: { $units } — { $place }.
pulsar-reply-unit-assigned-partial = Only { $count } available. Assigning: { $units } — { $place }.
pulsar-reply-unit-released = Order lifted: { $units }.
pulsar-reply-unit-status = Linked to the AI: { $count }. { $list }.

pulsar-rank-captain = captain
pulsar-rank-security = security, red alert
pulsar-rank-head = department head
pulsar-rank-department = department staff
pulsar-rank-crew = crew member

pulsar-outcome-unitssent = sent units
pulsar-outcome-unitsassigned = assigned units
pulsar-outcome-unitsreleased = released units
pulsar-outcome-unitsdenied = refused units

# Stage 5.3
pulsar-unit-task-corpse = carrying a casualty
pulsar-unit-task-recharge = recharging
pulsar-unit-report-revived = { $unit }: { $name } revived in medbay, condition critical.
pulsar-unit-report-revive-failed = { $unit }: { $name } brought to medbay, could not be revived. A doctor is needed.
pulsar-unit-report-morgue = { $unit }: the body ({ $name }) is in the morgue.

pulsar-reply-anomaly-kept = Understood. I will leave this anomaly to science.
pulsar-reply-anomaly-kept-all = Understood. The anomalies on the station are left to science.
pulsar-reply-anomaly-released = Understood. I will have the anomaly neutralized.
pulsar-reply-anomaly-released-all = Understood. I will have the anomalies neutralized.
pulsar-reply-anomaly-not-yours = Anomalies are for science to decide. Please ask the research director.
pulsar-reply-anomaly-none = I see no anomaly nearby.
pulsar-unit-anomaly-left = { $unit }: leaving the anomaly to science, back to work.
