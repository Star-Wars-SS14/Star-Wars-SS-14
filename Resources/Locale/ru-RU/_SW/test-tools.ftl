sw-test-preset-title = Тест (быстрый раунд)
sw-test-preset-description = Тестовый раунд для проверки геймдизайна. Все таймеры раунда в 10 раз короче.

sw-cmd-no-faction = Фракции { $faction } нет в этом раунде.
sw-cmd-no-factions = В раунде нет фракций. Нужен SW-пресет, например SWTestPreset.
sw-cmd-hint-faction = <фракция>

cmd-swtask-desc = Выдаёт фракции задачу или засчитывает её.
cmd-swtask-help = Использование: swtask add <фракция> <прототип задачи> [force] | swtask complete <фракция> <номер задачи из swscore>
sw-cmd-task-unknown-action = Неизвестное действие { $action }: нужно add или complete.
sw-cmd-task-no-task = { $task } не прототип задачи (нет компонента SWTask).
sw-cmd-task-requirements = Условия задачи { $task } не выполнены (например, нет цели нужной профессии). Добавьте force, чтобы выдать всё равно.
sw-cmd-task-added = Задача «{ $task }» выдана фракции { $faction }, её номер { $number }.
sw-cmd-task-bad-number = Номер задачи должен быть от 1 до { $count }.
sw-cmd-task-completed = Задача «{ $task }» засчитана, очков: { $score }.
sw-cmd-task-hint-action = <add|complete>
sw-cmd-task-hint-task = <прототип задачи>
sw-cmd-task-hint-number = <номер задачи>
sw-cmd-task-hint-force = [force]

cmd-swscore-desc = Показывает текущий счёт, деньги и задачи фракций и кто победит, если раунд кончится сейчас.
cmd-swscore-help = Использование: swscore [фракция]
sw-cmd-score-faction = == { $name } ({ $id }): { $score } очков
sw-cmd-score-money = Деньги: { $money }
sw-cmd-score-task = { $number }. { $name } [{ $proto }]: { $progress }%, очков { $score }
sw-cmd-score-draw = ничья
sw-cmd-score-winner = Если раунд кончится сейчас, победа: { $winners }

cmd-swmoney-desc = Начисляет фракции деньги или очки снабжения (минус списывает).
cmd-swmoney-help = Использование: swmoney <фракция> <валюта> <сумма>
sw-cmd-money-no-currency = Валюты { $currency } нет.
sw-cmd-money-done = Счёт { $faction } в { $currency } теперь { $amount }.
sw-cmd-money-hint-currency = <валюта>
sw-cmd-money-hint-amount = <сумма>

cmd-swbots-desc = Спавнит ботов-солдат фракции там, где вы стоите, или в точке карты.
cmd-swbots-help = Использование: swbots <фракция> <число> [Hold|EnemyBase|Centre|Towers|Point] [x y [mapId]]. Point — охранять точку спавна. По умолчанию EnemyBase.
sw-cmd-bots-no-bot = Для фракции { $faction } нет прототипа бота (компонент SWCombatBot).
sw-cmd-bots-bad-count = Число ботов должно быть от 1 до { $max }.
sw-cmd-bots-bad-objective = Неизвестная цель { $objective }.
sw-cmd-bots-no-position = Нет точки спавна: зайдите в игру (aghost) или укажите x y и mapId.
sw-cmd-bots-spawned = Заспавнено ботов: { $count }, фракция { $faction }, цель { $objective }.
sw-cmd-bots-hint-count = <число>
sw-cmd-bots-hint-objective = [цель]
