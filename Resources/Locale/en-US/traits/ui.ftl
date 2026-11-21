# SPDX-License-Identifier: AGPL-3.0-or-later

humanoid-profile-editor-traits-header = Points: {$points}/{$max}  Slots: {$slots}/{$maxSlots}
humanoid-profile-editor-traits-points-label = Points: {$points}/{$max}
humanoid-profile-editor-traits-show-unusable-button = Show unavailable
humanoid-profile-editor-traits-show-unusable-button-tooltip = Also list traits this character cannot take.

character-requirement-desc = Requirements:
character-job-requirement = { $inverted ->
    [true] Must not have the job: {$jobs}
   *[false] Must have the job: {$jobs}
}
character-department-requirement = { $inverted ->
    [true] Must not be in department: {$departments}
   *[false] Must be in department: {$departments}
}
character-species-requirement = { $inverted ->
    [true] Not for species: {$species}
   *[false] Only for species: {$species}
}
character-trait-requirement = { $inverted ->
    [true] Incompatible with: {$traits}
   *[false] Requires: {$traits}
}

self-aware-verb = Check your body
self-aware-verb-desc = You take stock of your injuries.
self-aware-type = {$type}: {$amount}
self-aware-group-1 = Slight {$group} damage.
self-aware-group-2 = Moderate {$group} damage.
self-aware-group-3 = Serious {$group} damage.
self-aware-group-4 = Severe {$group} damage.
self-aware-none = You find no remarkable injuries.
fragileCircuits-kill-popup = {CAPITALIZE($name)}'s circuits fail catastrophically!
metabolizer-type-LiquorLifeline = liquor lifeline
