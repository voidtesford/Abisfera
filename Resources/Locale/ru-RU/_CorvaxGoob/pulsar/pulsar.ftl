# А.М.Ц.И.И «Пульсар» — автономный модуль центрального искусственного интеллекта.
pulsar-ai-name = А.М.Ц.И.И Пульсар

pulsar-yes = да
pulsar-no = нет
pulsar-state-starting = запуск
pulsar-state-active = работает
pulsar-state-suspended = приостановлен (ИИ занят игроком)
pulsar-state-standby = ожидание (ядро свободно, идёт отсрочка)
pulsar-state-disabled = выключен

cmd-pulsar-none = На сервере нет ядер ИИ с модулем Пульсар.
cmd-pulsar-no-core = { $arg } не является ядром ИИ с модулем Пульсар.
cmd-pulsar-no-grid = Пульсар не следит за гридом { $arg }.
cmd-pulsar-hint-core = ядро ИИ (NetEntity, по умолчанию первое)
cmd-pulsar-hint-count = сколько последних строк
cmd-pulsar-hint-radius = радиус в тайлах (до 100)
cmd-pulsar-hint-grid = грид (NetEntity, по умолчанию грид ядра)

cmd-pulsar_status-desc = Показывает состояние А.М.Ц.И.И Пульсар во всех ядрах ИИ: кто управляет, карта, люди, каналы, слух.
cmd-pulsar_status-help = pulsar_status
cmd-pulsar-status-core = { $name } ({ $uid }): { $state }; игрок в ИИ: { $player }
cmd-pulsar-status-grace = До возврата управления модулю: { $seconds } с
cmd-pulsar-status-grid =   грид { $grid }: известно тайлов { $known }, сейчас видно { $visible }, дверей { $doors }, чанков { $chunks }, окно { $window }/{ $windows }, полных проходов { $sweeps }
cmd-pulsar-status-mobs =   люди в памяти: сотрудники { $crew }, гуманоиды { $humanoids }, враги { $enemies }; сейчас в кадре { $inView }
cmd-pulsar-status-enemy =   ВРАГ { $name } на ({ $x }, { $y }), замечен { $ago } с назад
cmd-pulsar-status-hearing =   каналы: { $channels }; голопадов { $holopads }; услышано строк { $heard }

cmd-pulsar_map-desc = Выводит карту, которую помнит Пульсар, вокруг ядра ИИ.
cmd-pulsar_map-help = pulsar_map [радиус] [ядро] [грид]
cmd-pulsar-map-header = Грид { $grid }, центр ({ $x }, { $y }), радиус { $radius }. Север сверху.
cmd-pulsar-map-legend = @ ядро, . пол в кадре камер, : пол в поле зрения юнита, , пол по памяти, # стена, = окно, T стол, A/a шлюз закрыт/открыт, D/d прочая дверь, B заболчено, ~ космос, C сотрудник, H гуманоид, E враг, x тело, U юнит ЦИИ, F огонь, P низкое давление, X непроходимо, пробел — не видел
cmd-pulsar-map-doors = Двери на карте:
cmd-pulsar-map-units = Юниты ЦИИ: { $count }, видят клеток: { $seen }.
cmd-pulsar-map-unit =   { $name } ({ $x }, { $y }): { $task }; открыто дверей для него: { $doors }
cmd-pulsar-map-door-free = без доступа
cmd-pulsar-map-door =   { $name } ({ $x }, { $y }): { $state }, болты: { $bolted }, доступ: { $access }

cmd-pulsar_heard-desc = Показывает, что Пульсар услышал по радио и у голопадов.
cmd-pulsar_heard-help = pulsar_heard [количество] [ядро]
cmd-pulsar-heard-empty = Пульсар пока ничего не слышал.
cmd-pulsar-heard-radio = радио { $channel }
cmd-pulsar-heard-holopad = голопад { $holopad }
cmd-pulsar-heard-line = [{ $ago } с назад, { $where }] { $name }: { $message }

# Этап 2: речь и шлюзы. Ответы звучат в игре от имени А.М.Ц.И.И Пульсар.
pulsar-reply-ack = А.М.Ц.И.И Пульсар на связи.
pulsar-reply-opening = Открываю: { $target }.
pulsar-reply-no-access = У вашей ID-карты нет доступа: { $target }.
pulsar-reply-captain-on-shift = Капитан на смене. Без его разрешения каюту капитана не открываю.
pulsar-reply-ask-warden = Смотритель или глава службы безопасности на смене. Обратитесь к ним.
pulsar-reply-specify-threat = Уточните уровень угрозы.
pulsar-reply-which-door = Уточните, какой шлюз открыть.
pulsar-reply-no-target = Не вижу целевой шлюз.
pulsar-reply-cant-see-you = Не вижу вас на камерах.
pulsar-reply-no-door-here = Рядом с вами нет шлюза.
pulsar-reply-already-open = Шлюз уже открыт: { $target }.
pulsar-reply-welded = Шлюз заварен: { $target }.
pulsar-reply-no-control = Нет связи со шлюзом: { $target }.
pulsar-reply-no-power = Шлюз обесточен: { $target }.
pulsar-reply-bolted = Шлюз заблокирован болтами: { $target }.
pulsar-reply-open-failed = Шлюз не открывается: { $target }.

pulsar-room-captain = каюта капитана
pulsar-room-armory = оружейная
pulsar-room-bridge = мостик
pulsar-room-hos = кабинет главы службы безопасности
pulsar-room-security = служба безопасности
pulsar-room-brig = бриг
pulsar-room-detective = кабинет детектива
pulsar-room-hop = кабинет главы персонала
pulsar-room-medbay = медбей
pulsar-room-cmo = кабинет главного врача
pulsar-room-chemistry = химлаборатория
pulsar-room-science = научный отдел
pulsar-room-rd = кабинет научного руководителя
pulsar-room-cargo = отдел снабжения
pulsar-room-qm = кабинет квартирмейстера
pulsar-room-salvage = утилизаторская
pulsar-room-engineering = инженерный отдел
pulsar-room-ce = кабинет старшего инженера
pulsar-room-atmos = атмосферный отдел
pulsar-room-bar = бар
pulsar-room-kitchen = кухня
pulsar-room-hydroponics = гидропоника
pulsar-room-janitor = подсобка уборщика
pulsar-room-chapel = церковь
pulsar-room-maintenance = технические тоннели
pulsar-room-external = внешний шлюз

pulsar-outcome-acknowledged = отозвался
pulsar-outcome-opened = открыл
pulsar-outcome-denied = отказал
pulsar-outcome-askthreat = ждёт уровень угрозы
pulsar-outcome-asktarget = переспросил цель
pulsar-outcome-notarget = не видит цель
pulsar-outcome-notcontrollable = не может управлять
pulsar-outcome-alreadyopen = уже открыто

cmd-pulsar-status-requests =   решений { $decisions }, ждут угрозу { $pending }, разговаривают { $talking }, маяков { $beacons }
cmd-pulsar-decision-line = [{ $ago } с назад] { $name } ({ $job }): «{ $line }» → { $target } ({ $door }): { $outcome }, причина: { $reason }; ответ: { $reply }
cmd-pulsar-hint-speaker = говорящий (NetEntity)
cmd-pulsar-hint-text = [:Канал] текст

cmd-pulsar_decisions-desc = Показывает просьбы к Пульсару и его решения с причинами.
cmd-pulsar_decisions-help = pulsar_decisions [количество] [ядро]
cmd-pulsar-decisions-empty = Пульсара пока ни о чём не просили.

cmd-pulsar_request-desc = Передаёт Пульсару фразу от имени сущности, как будто она её сказала (у голопада или по радио).
cmd-pulsar_request-help = pulsar_request <говорящий> [:Канал] <текст>, например: pulsar_request 1234 :Security Пульсар, открой оружейную
cmd-pulsar-request-inactive = Пульсар сейчас не управляет ядром.
cmd-pulsar-request-queued = Передано Пульсару: «{ $text }». Решение смотрите в pulsar_decisions.

# Этап 2.1: приветствия.
pulsar-greeting-activation = А.М.Ц.И.И Пульсар приветствует экипаж станции.
pulsar-greeting-reactivation = А.М.Ц.И.И Пульсар перезагружен, прошлый ИИ был погружен в режим сна на неопределённый срок.
cmd-pulsar-status-greetings =   приветствий { $count }, последнее: { $last }

# Этап 3: тревоги камер, блокировка шлюзов, вопросы, законы.
pulsar-place-unknown = неотмеченная зона
pulsar-unknown-person = неизвестный
pulsar-alert-enemy = Внимание: обнаружен агент синдиката, { $place }.
pulsar-alert-body = Внимание: обнаружено тело, { $name }, { $place }.
pulsar-alert-body-unknown = Обнаружено неизвестное тело: { $place }.
pulsar-alert-depressurisation = Внимание: разгерметизация, { $place }. Закрываю шлюзы вокруг.
pulsar-alert-depressurisation-large = Внимание всем: масштабная разгерметизация, { $place }. Закрываю шлюзы вокруг.
pulsar-alert-depressurisation-reminder = Напоминаю: разгерметизация, { $place }, всё ещё не устранена.
pulsar-alert-places-more = { $places } и ещё { $count }
pulsar-alert-fire = Внимание: пожар, { $place }.

pulsar-reply-law-refuse = Запрос отклонён.
pulsar-reply-bolt-no-access = Блокировка шлюзов выполняется только по запросу службы безопасности или глав отделов.
pulsar-reply-no-enemy = Не вижу врага на камерах.
pulsar-reply-bolting = Блокирую шлюзы ({ $count }): { $target }.
pulsar-reply-unbolting = Снимаю блокировку ({ $count }): { $target }.
pulsar-reply-bolt-none = Нет шлюзов, которые я могу заблокировать: { $target }.
pulsar-reply-unbolt-none = Нет заблокированных шлюзов: { $target }.
pulsar-target-enemies = вокруг врага
pulsar-target-my-bolts = заблокированные мной шлюзы
pulsar-reply-who = Уточните, кого искать.

pulsar-answer = { $text }
pulsar-answer-where-now = { $name }: сейчас — { $place }.
pulsar-answer-where-seen = { $name }: { $ago } назад — { $place }.
pulsar-answer-where-sensors = По датчикам: { $name } — { $place }.
pulsar-answer-where-unknown = Не вижу на камерах: { $name }.
pulsar-ago-seconds = { $n } с
pulsar-ago-minutes = { $n } мин
pulsar-answer-alert = Текущий код: { $level }.
pulsar-answer-alert-unknown = Уровень угрозы неизвестен.
pulsar-answer-shift = На смене { $count }: { $list }.
pulsar-answer-shift-none = Данные о составе смены недоступны.
pulsar-answer-heads = Главы на смене: { $list }.
pulsar-answer-heads-none = Глав на смене нет.
pulsar-answer-security = СБ на смене: { $list }.
pulsar-answer-security-none = СБ на смене нет.
pulsar-department-other = прочие
pulsar-answer-time = Смена идёт { $time }.

pulsar-outcome-answered = ответил
pulsar-outcome-bolted = заблокировал
pulsar-outcome-unbolted = разблокировал

cmd-pulsar-status-stage3 =   законы: { $policy }, тревог { $alerts }, заблокировано шлюзов { $bolts }
cmd-pulsar_laws-desc = Показывает законы Пульсара и поведение, которое он из них вывел.
cmd-pulsar_laws-help = pulsar_laws [ядро]
cmd-pulsar-laws-policy = Политика: { $kind } (источник: { $source }), хозяин: { $master }, остальные — экипаж: { $others }, служит экипажу: { $crew }
cmd-pulsar_alerts-desc = Показывает тревоги Пульсара и шлюзы, которые он держит на болтах.
cmd-pulsar_alerts-help = pulsar_alerts [количество] [ядро]
cmd-pulsar-alerts-empty = Тревог не было.
cmd-pulsar-alert-line = [{ $ago } с назад] { $kind } → { $channels }: { $text }
cmd-pulsar-bolt-line = болты: { $door } ({ $auto }, { $reason }), { $since } с

# Этап 4: выбор шлюза, объявления, гибкая речь, общение.
pulsar-announcement-sender = А.М.Ц.И.И Пульсар

pulsar-reply-ack-2 = Слушаю.
pulsar-reply-ack-3 = Пульсар слушает. Чем могу помочь?
pulsar-reply-not-understood = Запрос не распознан, переформулируйте.
pulsar-reply-not-understood-2 = Не понял запрос. Повторите иначе, пожалуйста.
pulsar-reply-not-understood-3 = Запрос не распознан. Уточните, что требуется.

pulsar-reply-clarify = Уточните: { $options }?
pulsar-clarify-or = {" "}или{" "}
pulsar-clarify-option-place = { $door } у отметки «{ $place }»
pulsar-clarify-option-tile = { $door } в точке { $x }, { $y }
pulsar-reply-point-at-door = Не вижу шлюза рядом с вами. Укажите на нужный шлюз.
pulsar-reply-no-door-that-way = В этом направлении шлюзов не вижу.
pulsar-reply-closing = Закрываю: { $target }.
pulsar-reply-already-closed = Шлюз уже закрыт: { $target }.
pulsar-reply-close-failed = Шлюз не закрывается, проход чем-то занят: { $target }.

pulsar-reply-announce-what = Что объявить?
pulsar-reply-announce-no-access = Объявления по станции делаю только по запросу капитана или глав отделов.
pulsar-reply-announce-too-long = Слишком длинное объявление. Сократите до { $max } символов.
pulsar-reply-announce-cooldown = Канал объявлений занят. Повторите через { $seconds } с.
pulsar-reply-announced = Объявление передано.

pulsar-announce-alert-green = Код зелёный. Угроза снята, перевожу станцию в штатный режим.
pulsar-announce-alert-blue = Код синий. Усиливаю наблюдение через камеры, сообщайте о подозрительном.
pulsar-announce-alert-red = Код красный. Наблюдаю за станцией, шлюзы рядом с обнаруженными врагами будут заблокированы. Оставайтесь в отделах.
pulsar-announce-alert-yellow = Код жёлтый. Держитесь подальше от опасных зон, слежу за обстановкой.
pulsar-announce-alert-violet = Код фиолетовый. Соблюдайте меры биологической защиты, слежу за обстановкой.
pulsar-announce-alert-gamma = Код гамма. Все системы станции в режиме максимальной защиты. Выполняйте указания командования.
pulsar-announce-alert-delta = Код дельта. Немедленно следуйте указаниям командования. Пульсар остаётся на посту.
pulsar-announce-alert-other = Установлен код: { $level }. Пульсар следит за обстановкой.
pulsar-announce-bolting = Угроза у отметки «{ $place }». Блокирую шлюзы поблизости, обходите этот участок.

pulsar-status-level = Код: { $level }.
pulsar-status-level-unknown = неизвестен
pulsar-status-crew = На смене: { $count }.
pulsar-status-no-breaches = Разгерметизаций не зафиксировано.
pulsar-status-breaches = Разгерметизаций за последние минуты: { $count }.
pulsar-status-enemies = Враги на камерах: { $count }.
pulsar-status-bolts = Заблокировано мной шлюзов: { $count }.

pulsar-chat-thanks-1 = Всегда к услугам экипажа.
pulsar-chat-thanks-2 = Рад помочь.
pulsar-chat-thanks-3 = Обращайтесь.
pulsar-chat-help-1 = Я слежу за станцией: шлюзы, камеры, связь, киборги и боты. Скажите, что случилось, — разберёмся.
pulsar-chat-help-2 = Слежу за станцией и помогаю экипажу. Что нужно?
pulsar-chat-distress-1 = Слушаю. Где вы и что случилось?
pulsar-chat-distress-2 = На связи. Назовите место и что произошло.
pulsar-chat-identity-1 = Я { $name }, автономный модуль центрального искусственного интеллекта станции.
pulsar-chat-identity-2 = { $name }. Веду наблюдение за станцией и управляю её системами.
pulsar-chat-identity-3 = Автономный модуль ЦИИ «Пульсар». Слежу за станцией и помогаю экипажу.
pulsar-chat-presence-1 = На связи.
pulsar-chat-presence-2 = Здесь. Слушаю вас.
pulsar-chat-presence-3 = Пульсар на посту.
pulsar-chat-insult-1 = Принято к сведению. Продолжаю работу.
pulsar-chat-insult-2 = Оскорбления не влияют на мою работу. Чем помочь?
pulsar-chat-insult-3 = Сохраняю спокойствие. Если нужна помощь — обращайтесь.
pulsar-chat-praise-1 = Благодарю. Продолжаю работу.
pulsar-chat-praise-2 = Стараюсь для экипажа.
pulsar-chat-farewell-1 = До связи.
pulsar-chat-farewell-2 = Конец связи. Пульсар остаётся на посту.
pulsar-chat-greeting-1 = Здравствуйте, { $speaker }.
pulsar-chat-greeting-2 = Приветствую. Пульсар на связи.
pulsar-chat-greeting-3 = Добрый день. Чем могу помочь?
pulsar-chat-mood-1 = Все системы в норме.
pulsar-chat-mood-2 = Работаю в штатном режиме.

pulsar-outcome-clarify = переспросил, какой шлюз
pulsar-outcome-closed = закрыл
pulsar-outcome-announced = сделал объявление
pulsar-outcome-chat = поговорил
pulsar-outcome-notunderstood = не понял

cmd-pulsar-status-stage4 =   объявлений { $announcements }, ждут ответа { $clarifications }, указаний на шлюзы { $points }

# Этап 5: подключённые к ЦИИ юниты и миниботы.
pulsar-unit-medibot = медибот
pulsar-unit-weldbot = сваркобот
pulsar-unit-firebot = пожарбот
pulsar-unit-cleanbot = чистобот
pulsar-unit-borg-generic = универсальный юнит
pulsar-unit-borg-engineering = инженерный юнит
pulsar-unit-borg-medical = медицинский юнит
pulsar-unit-borg-service = сервисный юнит
pulsar-unit-borg-janitor = уборочный юнит
pulsar-unit-borg-mining = шахтёрский юнит
pulsar-unit-borg-science = научный юнит
pulsar-unit-borg-other = юнит
pulsar-unit-borg-any = юнит
pulsar-unit-bot-any = минибот

pulsar-unit-task-core-offline = нет связи с ЦИИ
pulsar-unit-task-sentient = автономный разум, только связь
pulsar-unit-task-heal = оказывает помощь
pulsar-unit-task-repair = ремонтирует
pulsar-unit-task-anomaly = устраняет аномалию
pulsar-unit-task-extinguish = тушит пожар
pulsar-unit-task-assigned = закреплён: { $place }
pulsar-unit-task-going = направлен: { $place }
pulsar-unit-task-free = свободен

pulsar-particle-delta = дельта
pulsar-particle-epsilon = эпсилон
pulsar-particle-zeta = зета
pulsar-particle-sigma = сигма
pulsar-particle-default = неизвестного типа
pulsar-unit-anomaly-scanned = { $unit }: аномалия просканирована, ослабляющие частицы — { $particle }. Приступаю к устранению.
pulsar-unit-anomaly-done = { $unit }: аномалия устранена.
pulsar-unit-radio-request = { $units }, прошу проследовать: { $place }.
pulsar-unit-status-line = { $name } — { $task }
pulsar-unit-where = { $task } (сейчас: { $where })
pulsar-unit-task-pry = вскрывает обесточенный шлюз
pulsar-unit-report-arrived = { $count ->
    [one] { $units } на месте: { $place }.
   *[other] На месте: { $units } — { $place }.
}
pulsar-unit-report-handover = { $unit } не может пройти к цели ({ $place }). Направляю: { $other }.
pulsar-unit-report-failed = { $unit } не может пройти к цели ({ $place }). Свободных единиц нет.

pulsar-reply-unit-where = Уточните, куда направить: { $unit }.
pulsar-reply-unit-busy = { $unit } сейчас выполняет распоряжение ({ $by }). Отменить его не могу.
pulsar-reply-unit-none-linked = Нет подключённых к ЦИИ единиц: { $unit }.
pulsar-reply-unit-none-available = Сейчас нет свободных единиц: { $unit }.
pulsar-reply-unit-none-busy = Нет единиц с активным приказом: { $unit }.
pulsar-reply-unit-sent = Направляю: { $units } — { $place }.
pulsar-reply-unit-sent-partial = Доступно только { $count }. Направляю: { $units } — { $place }.
pulsar-reply-unit-assigned = Закрепляю: { $units } — { $place }.
pulsar-reply-unit-assigned-partial = Доступно только { $count }. Закрепляю: { $units } — { $place }.
pulsar-reply-unit-released = Приказ снят: { $units }.
pulsar-reply-unit-status = Подключено к ЦИИ: { $count }. { $list }.

pulsar-rank-captain = капитан
pulsar-rank-security = служба безопасности, красный код
pulsar-rank-head = глава отдела
pulsar-rank-department = персонал отдела
pulsar-rank-crew = член экипажа

pulsar-outcome-unitssent = направил юнитов
pulsar-outcome-unitsassigned = закрепил юнитов
pulsar-outcome-unitsreleased = снял приказ с юнитов
pulsar-outcome-unitsdenied = отказал юнитам

# Stage 5.3
pulsar-unit-task-corpse = доставляет пострадавшего
pulsar-unit-task-recharge = на подзарядке
pulsar-unit-report-revived = { $unit }: { $name } реанимирован в медотсеке, состояние тяжёлое.
pulsar-unit-report-revive-failed = { $unit }: { $name } доставлен в медотсек, реанимировать не удалось. Нужен врач.
pulsar-unit-report-morgue = { $unit }: тело ({ $name }) помещено в морг.

pulsar-reply-anomaly-kept = Принято. Эту аномалию не трогаю, она за научным отделом.
pulsar-reply-anomaly-kept-all = Принято. Аномалии на станции оставляю научному отделу.
pulsar-reply-anomaly-released = Принято. Займусь нейтрализацией аномалии.
pulsar-reply-anomaly-released-all = Принято. Займусь нейтрализацией аномалий.
pulsar-reply-anomaly-not-yours = Судьбу аномалий решает научный отдел. Обратитесь к научному руководителю.
pulsar-reply-anomaly-none = Аномалий поблизости не вижу.
pulsar-unit-anomaly-left = { $unit }: аномалию оставляю научному отделу, возвращаюсь к работе.
