cmd-procanim-desc = Проигрывает процедурную катсцену (прототип procAnimScene) на указанных гуманоидах или останавливает её.
cmd-procanim-help = procanim play <id сцены> <uid актёров в порядке ролей...> [keepdown=true]  |  procanim stop [uid...]  |  procanim preview <id сцены> [расы...]  |  procanim list
    Без uid stop останавливает все идущие сцены.
cmd-procanim-usage = Использование: procanim play <id сцены> <uid актёров...> [keepdown true/false], procanim stop [uid...], procanim preview <id сцены> [расы...] или procanim list
cmd-procanim-bad-uid = { $uid } — нет такой сущности.
cmd-procanim-started = Сцена { $scene } запущена: { $actors }.
cmd-procanim-stopped = Остановлено.
cmd-procanim-stopped-all = Все сцены остановлены (освобождено актёров: { $count }).
cmd-procanim-hint-mode = play | stop | preview | list
cmd-procanim-hint-scene = <id сцены>
cmd-procanim-hint-uid = [uid]
cmd-procanim-hint-actor = <uid: { $role }>
cmd-procanim-hint-species = [раса, по порядку ролей]
cmd-procanim-bad-species = { $species } — не раса и не прототип сущности.
cmd-procanim-preview-no-entity = Для preview нужно быть в игре: манекены появляются рядом с вами.
cmd-procanim-preview-started = Предпросмотр { $scene }: { $actors } (исчезнут через несколько секунд после сцены).

procanim-error-no-proto = Нет такого прототипа procAnimScene.
procanim-error-actor-count = Неверное число актёров; сцене нужны: { $roles }.
procanim-error-same = Все актёры должны быть разными сущностями.
procanim-error-not-humanoid = Все актёры должны быть гуманоидами.
procanim-error-busy = Кто-то из них уже в катсцене.
procanim-error-buckled = Кто-то из них пристёгнут.
procanim-error-other-map = Все актёры должны быть на одной карте.
procanim-error-missing-item = У актёра нет предмета, нужного сцене.
