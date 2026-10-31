sw-tutorial-choice-question = Вы играли в Space Station 14?
sw-tutorial-choice-new-to-game = Нет, научите (около 5 минут)
sw-tutorial-choice-new-to-server = Да, расскажите, что тут своего (около 3 минут)
sw-tutorial-choice-new-to-server-soon = Да, расскажите, что тут своего (скоро)
sw-tutorial-choice-skip = Пропустить
sw-tutorial-choice-back = Назад
sw-tutorial-choice-hint = Вернуться к обучению можно в любой момент кнопкой в лобби.
sw-tutorial-lobby-button = Обучение
sw-tutorial-exit-button = Выйти
sw-tutorial-return-button = Вернуться в бой

sw-tutorial-step-walk = Идите к стрелке: { $up } { $left } { $down } { $right }
sw-tutorial-step-walk-big = { $up } { $left } { $down } { $right }: так ходят
sw-tutorial-step-rifle = Подойдите к винтовке и кликните по ней
sw-tutorial-step-rifle-far = Далеко. Подойдите ближе
sw-tutorial-step-rifle-busy = Эта рука занята. { $swap }: другая рука
sw-tutorial-step-combat = Без боевого режима оружие молчит: нажмите кнопку в рамке
sw-tutorial-step-combat-mistake = Сначала боевой режим: кнопка в рамке
sw-tutorial-step-target = Кликните по мишени: это выстрел
sw-tutorial-step-target-wield = Пули мимо? { $wield }: возьмите винтовку двумя руками
sw-tutorial-step-target-near = Мишень ближе. { $wield }: две руки, потом стреляйте
sw-tutorial-step-target-one-hand = С одной руки повезло. Новая мишень: { $wield }, две руки
sw-tutorial-step-corridor = Мишень разбита. Идите к стрелке, дальше коридор
sw-tutorial-step-enemy = Значок над головой: свой. Без значка: стреляйте
sw-tutorial-step-dropped-rifle = Винтовка на полу. Подойдите и кликните по ней
sw-tutorial-step-dropped-injector = Инъектор не в руке. Поднимите его или возьмите второй из кармана
sw-tutorial-step-enemy-friend = Это свой: у него значок
sw-tutorial-step-wounded = Вас ранили. Кликните по инъектору в кармане, внизу экрана
sw-tutorial-step-inject = { $wield }: уколите себя инъектором
sw-tutorial-step-radio = Доложите по рации: Enter, затем :у готов, Enter
sw-tutorial-step-radio-local = Это слышат только рядом. В начале строки нужно :у
sw-tutorial-step-dead = Вы погибли. В бою каждая смерть тратит резерв стороны
sw-tutorial-step-shuttle = Идите к шаттлу: там настоящая война

sw-tutorial-popup-wounded = Вас задело из-за угла
sw-tutorial-popup-volley = Залп

sw-tutorial-finish-title = Учебка окончена
sw-tutorial-finish-next = В раунде идите к старшему и слушайте приказы. Старшего нет: идите на фронт.
sw-tutorial-finish-republic = За Республику: живых { $alive } { $weaker ->
    [true] (сторона меньше)
   *[false] {""}
}
sw-tutorial-finish-empire = За Империю: живых { $alive } { $weaker ->
    [true] (сторона меньше)
   *[false] {""}
}
sw-tutorial-finish-wait = Раунд ещё не начался: нажмите «Готов» в лобби.
sw-tutorial-finish-close = В лобби

sw-tutorial-error-already-started = Вы уже в обучении.
sw-tutorial-error-not-started = Вы не в обучении.
sw-tutorial-error-not-in-lobby = Обучение запускается только из лобби.
sw-tutorial-error-not-loaded = Ваши данные ещё загружаются, попробуйте через пару секунд.
sw-tutorial-error-map = Не удалось загрузить карту обучения.
sw-tutorial-error-full = Сейчас обучение проходит слишком много игроков, попробуйте через пару минут.
sw-tutorial-error-mode-not-ready = Этот режим обучения ещё не готов.
sw-tutorial-error-no-slot = На этой стороне нет мест. Выберите роль через «Присоединиться» в лобби.

cmd-swtutorial-desc = Запускает обучение или выходит из него.
cmd-swtutorial-help = Использование: swtutorial [new|server|exit]

job-name-sw-tutorial-soldier = Ученик
job-description-sw-tutorial-soldier = Проходит обучение. В настоящем раунде этой работы нет.

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
