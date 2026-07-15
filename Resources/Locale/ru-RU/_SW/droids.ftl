sw-droid-command-no-droids-nearby = Рядом нет доступных дроидов.
sw-droid-command-accepted = Командование дроидом принято.
sw-droid-command-relinquished = Командование дроидом прекращено.
sw-droid-module-category-limit = В этом дроиде уже установлен модуль этой категории.
sw-droid-targeting-tablet-linked = Планшет наведения дроида привязан.
sw-droid-targeting-tablet-locked = Сначала разблокируйте дроида.
sw-droid-targeting-tablet-wrong-faction = Этот планшет несовместим с этим дроидом.
sw-droid-repair-missing-steel = Для ремонта дроида нужна сталь.
sw-targeting-console-target-humanoids-without-id = Атаковать гуманоидов без ID-карты
sw-targeting-console-target-animals = Атаковать животных
sw-droid-control-datapad-window-title = Датапад управления дроидами
sw-droid-control-datapad-tab-droids = Дроиды
sw-droid-control-datapad-tab-targets = Цели
sw-droid-control-datapad-tab-map = Карта
sw-droid-control-datapad-droids-count = Привязанные дроиды: {$count}
sw-droid-control-datapad-map-droids-count = Отслеживаемые дроиды: {$count}
sw-droid-control-datapad-no-linked-droids = Нет привязанных дроидов.
sw-droid-control-datapad-no-map-droids = Нет отслеживаемых привязанных дроидов.
sw-droid-control-datapad-map-unavailable = Карта дроидов недоступна.
sw-droid-control-datapad-unknown = Неизвестно
sw-droid-control-datapad-no-battery = Нет батареи
sw-droid-control-datapad-charge-percent = {$percent}%
sw-droid-control-datapad-coordinates = {$x}, {$y}
sw-droid-control-datapad-status-alive = Работает
sw-droid-control-datapad-status-critical = Повреждён
sw-droid-control-datapad-status-dead = Сломан
sw-droid-control-datapad-status-unknown = Неизвестно
sw-droid-control-datapad-droid-status = Статус: {$status}
sw-droid-control-datapad-droid-charge = Заряд: {$charge}
sw-droid-control-datapad-droid-coordinates = Координаты: {$coordinates}
sw-droid-control-datapad-follow-datapad = Следовать за датападом
sw-droid-control-datapad-stop-following = Прекратить следование
sw-droid-control-datapad-map-selected = {$name}
    Статус: {$status}
    Заряд: {$charge}
sw-droid-control-datapad-map-entry-details = {$status}, {$charge}
sw-droid-control-datapad-targets-enabled = Игнорируемые должности
sw-droid-control-datapad-targets-disabled = Атакуемые должности
sw-droid-control-datapad-faction-allow = Игнорировать всю фракцию: {$faction}
sw-droid-control-datapad-faction-disallow = Атаковать всю фракцию: {$faction}

ent-SWMobDroidBase = боевой дроид
    .desc = Компактный боевой дроид с модульными системами.

ent-SWMobDroid = боевой дроид
    .suffix = ИИ
    .desc = { ent-SWMobDroidBase.desc }

ent-ActionSWDroidTakeCommand = Взять командование
    .desc = Взять командование над ближайшим союзным дроидом.

ent-ActionSWDroidRelinquishCommand = Отстраниться от командования
    .desc = Отпустить командование ближайшим подконтрольным дроидом.

ent-ActionSWDroidCommandMenu = Команды дроида
    .desc = Открыть колесо команд для подконтрольных дроидов.

ent-ActionSWDroidOrderFollow = Следовать
    .desc = Подконтрольные дроиды следуют за вами.

ent-ActionSWDroidOrderStay = Стоять
    .desc = Подконтрольные дроиды удерживают позицию.
sw-droid-targeting-tablet-disabled = Этот датапад дистанционно отключён.
sw-droid-targeting-tablet-examine-control-id = Код управления: {$id}. Статус: {$status}.
sw-droid-control-console-window-title = Консоль управления дроидами
sw-droid-control-console-datapads-count = Датапады управления: {$count}
sw-droid-control-console-no-datapads = Нет зарегистрированных датападов управления.
sw-droid-control-console-status-enabled = Включён
sw-droid-control-console-status-disabled = Отключён
sw-droid-control-console-datapad-heading = {$id} - {$name}
sw-droid-control-console-datapad-details = Статус: {$status}; привязанные дроиды: {$count}
sw-droid-control-console-disable = Отключить
sw-droid-control-console-enable = Включить

ent-SWDroidControlConsoleEmpire = имперская консоль управления дроидами
    .suffix = Империя
    .desc = Командная консоль для отслеживания и отключения имперских датападов управления дроидами.

ent-SWDroidControlConsoleRepublic = республиканская консоль управления дроидами
    .suffix = Республика
    .desc = Командная консоль для отслеживания и отключения республиканских датападов управления дроидами.
sw-droid-control-datapad-disabled-overlay = ОТКЛЮЧЕН
ent-ActionSWDroidOrderGoTo = Иди!
    .desc = Подконтрольные дроиды идут в указанную точку и удерживают ее.

ent-ActionSWDroidOrderAttack = Стреляй!
    .desc = Подконтрольные дроиды открывают огонь по выбранной цели.

ent-ActionSWDroidOrderDismiss = Отставить!
    .desc = Подконтрольные дроиды прекращают выполнение текущего приказа.
