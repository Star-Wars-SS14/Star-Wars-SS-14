sw-tutorial-choice-question = Вы играли в Space Station 14?
sw-tutorial-choice-new-to-game = Начать обучение
sw-tutorial-choice-new-to-server = Знаю игру: производство и снабжение
sw-tutorial-choice-new-to-server-soon = Знаю игру (скоро)
sw-tutorial-choice-skip = Пропустить
sw-tutorial-choice-back = Назад
sw-tutorial-choice-hint = Этап сохраняется. Выйти можно в любой момент.
sw-tutorial-lobby-button = Обучение
sw-tutorial-exit-button = Выйти
sw-tutorial-return-button = Вернуться



sw-tutorial-finish-title = Учебка окончена
sw-tutorial-finish-next = В раунде иди к старшему. Старшего нет: на фронт.
sw-tutorial-finish-republic = За Республику: живых { $alive } { $weaker ->
    [true] (сторона меньше)
   *[false] {""}
}
sw-tutorial-finish-empire = За Империю: живых { $alive } { $weaker ->
    [true] (сторона меньше)
   *[false] {""}
}
sw-tutorial-finish-wait = Раунд не начался. Нажми «Готов» в лобби.
sw-tutorial-finish-campaign-next = Практика окончена. Начни гражданским, смотри местные поручения.
sw-tutorial-finish-campaign-republic = Гражданский Республики
sw-tutorial-finish-campaign-empire = Гражданский Империи
sw-tutorial-finish-campaign-wait = Кампании сейчас нет. Выбери роль в лобби.
sw-tutorial-finish-close = В лобби

sw-tutorial-error-already-started = Вы уже в обучении.
sw-tutorial-error-not-started = Вы не в обучении.
sw-tutorial-error-not-in-lobby = Обучение запускается только из лобби.
sw-tutorial-error-not-loaded = Данные загружаются. Попробуй через пару секунд.
sw-tutorial-error-map = Не удалось загрузить карту обучения.
sw-tutorial-error-full = Обучение занято. Попробуй через пару минут.
sw-tutorial-error-mode-not-ready = Этот режим обучения ещё не готов.
sw-tutorial-error-no-slot = Мест нет. Выбери роль в лобби.

cmd-swtutorial-desc = Запускает обучение или выходит из него.
cmd-swtutorial-help = Использование: swtutorial [new|server|exit]

job-name-sw-tutorial-soldier = Ученик
job-description-sw-tutorial-soldier = Проходит обучение.

sw-tutorial-radio-channel = Учебный канал

ent-SWTutorialSpawnPoint = точка появления в обучении
    .desc = Здесь появляется ученик на карте обучения.
ent-SWTutorialPointDoor = метка обучения: дверь
    .desc = Цель первого шага.
ent-SWTutorialPointRifle = метка обучения: винтовка
    .desc = Здесь сценарий кладёт винтовку.
ent-SWTutorialPointTargetFar = метка обучения: дальняя мишень
    .desc = Здесь сценарий ставит мишень.
ent-SWTutorialPointTargetNear = метка обучения: ближняя мишень
    .desc = Сюда мишень переезжает, если игрок не попадает.
ent-SWTutorialPointRangeExit = метка обучения: выход из тира
    .desc = Сюда ведёт стрелка после мишени.
ent-SWTutorialPointRangeWay = метка обучения: дорога из тира
    .desc = Стрелка, которую видно от стойки.
ent-SWTutorialPointFriend = метка обучения: свой
    .desc = Здесь стоит союзник.
ent-SWTutorialPointDroid = метка обучения: дроид
    .desc = Здесь стоит дроид.
ent-SWTutorialPointHeal = метка обучения: ранение
    .desc = Здесь игрока ранит.
ent-SWTutorialPointTower = метка обучения: вышка
    .desc = Цель перед боем.
ent-SWTutorialPointAlly = метка обучения: союзный бот
    .desc = Здесь появляется союзный бот в бою.
ent-SWTutorialPointEnemy = метка обучения: враг
    .desc = Отсюда идут дроиды.
ent-SWTutorialPointRespawn = метка обучения: возврат
    .desc = Здесь игрок приходит в себя после смерти.
ent-SWTutorialPointShuttle = метка обучения: шаттл
    .desc = Цель последнего шага.
ent-SWTutorialArrow = стрелка
    .desc = Идите сюда.
ent-SWTutorialTarget = мишень
    .desc = Учебная мишень.
ent-SWTutorialFriendDummy = союзник
    .desc = Свой, значок над головой.
ent-SWTutorialAllyBot = союзный боец
    .desc = Свой, значок над головой.
ent-SWTutorialDroidDummy = неисправный дроид
    .desc = Стоит и не стреляет.
ent-SWTutorialDroid = боевой дроид
    .desc = Вышел из-под контроля.
ent-SWTutorialInjector = учебный инъектор
    .desc = Лечит ожоги.
ent-SWTutorialHeadset = учебная рация
    .desc = Работает только на учебной площадке.
ent-SWTutorialEncryptionKey = ключ учебного канала
    .desc = Для учебной рации.
ent-SWTutorialIdCard = жетон ученика
    .desc = Дроиды стреляют по тем, у кого есть жетон.
ent-SWTutorialFaction = учебная сторона
    .desc = Существует только на учебной площадке.
ent-SWTutorialEnemyFaction = учебный противник
    .desc = Существует только на учебной площадке.
