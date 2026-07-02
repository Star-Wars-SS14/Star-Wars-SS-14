sw-droid-command-no-droids-nearby = Рядом нет доступных дроидов.
sw-droid-command-accepted = Командование дроидом принято.
sw-droid-module-category-limit = В этом дроиде уже установлен модуль этой категории.
sw-droid-targeting-tablet-linked = Планшет наведения дроида привязан.
sw-droid-targeting-tablet-locked = Сначала разблокируйте дроида.
sw-droid-targeting-tablet-wrong-faction = Этот планшет несовместим с этим дроидом.
sw-targeting-console-target-humanoids-without-id = Атаковать гуманоидов без ID-карты
sw-targeting-console-target-animals = Атаковать животных
sw-droid-control-datapad-window-title = Датапад управления дроидами
sw-droid-control-datapad-tab-droids = Дроиды
sw-droid-control-datapad-tab-targets = Цели
sw-droid-control-datapad-droids-count = Привязанные дроиды: {$count}
sw-droid-control-datapad-no-linked-droids = Нет привязанных дроидов.
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
sw-droid-control-datapad-targets-enabled = Целевые должности
sw-droid-control-datapad-targets-disabled = Игнорируемые должности
sw-droid-control-datapad-faction-allow = Атаковать всю фракцию: {$faction}
sw-droid-control-datapad-faction-disallow = Игнорировать всю фракцию: {$faction}

ent-SWMobDroidBase = боевой дроид
    .desc = Компактный боевой дроид с модульными системами.

ent-SWMobDroid = боевой дроид
    .suffix = ИИ
    .desc = { ent-SWMobDroidBase.desc }

ent-ActionSWDroidTakeCommand = Взять командование
    .desc = Взять командование над ближайшим союзным дроидом.

ent-ActionSWDroidOrderFollow = Следовать
    .desc = Подконтрольные дроиды следуют за вами.

ent-ActionSWDroidOrderStay = Стоять
    .desc = Подконтрольные дроиды удерживают позицию.
