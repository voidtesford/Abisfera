# SPDX-License-Identifier: AGPL-3.0-or-later

humanoid-profile-editor-traits-header = Очки: {$points}/{$max}  Слоты: {$slots}/{$maxSlots}
humanoid-profile-editor-traits-points-label = Очки: {$points}/{$max}
humanoid-profile-editor-traits-show-unusable-button = Показать недоступные
humanoid-profile-editor-traits-show-unusable-button-tooltip = Также показать особенности, которые этот персонаж не может взять.

character-requirement-desc = Требования:
character-job-requirement = { $inverted ->
    [true] Нельзя взять с должностью: {$jobs}
   *[false] Нужна должность: {$jobs}
}
character-department-requirement = { $inverted ->
    [true] Нельзя взять в отделе: {$departments}
   *[false] Нужен отдел: {$departments}
}
character-species-requirement = { $inverted ->
    [true] Не для расы: {$species}
   *[false] Только для расы: {$species}
}
character-trait-requirement = { $inverted ->
    [true] Несовместимо с: {$traits}
   *[false] Требует: {$traits}
}

self-aware-verb = Ощупать себя
self-aware-verb-desc = Вы оцениваете свои раны.
self-aware-type = {$type}: {$amount}
self-aware-group-1 = Лёгкие повреждения ({$group}).
self-aware-group-2 = Умеренные повреждения ({$group}).
self-aware-group-3 = Серьёзные повреждения ({$group}).
self-aware-group-4 = Тяжёлые повреждения ({$group}).
self-aware-none = Заметных повреждений нет.
fragileCircuits-kill-popup = Схемы {CAPITALIZE($name)} катастрофически отказывают!
metabolizer-type-LiquorLifeline = алкогольная зависимость к жизни
