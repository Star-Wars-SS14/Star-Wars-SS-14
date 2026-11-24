sw-test-preset-title = Test (fast round)
sw-test-preset-description = A test round for checking game design. Every round timer is 10 times shorter.

sw-cmd-no-faction = Faction { $faction } is not in this round.
sw-cmd-no-factions = The round has no factions. Use an SW preset, for example SWTestPreset.
sw-cmd-hint-faction = <faction>

cmd-swtask-desc = Gives a faction a task or completes one.
cmd-swtask-help = Usage: swtask add <faction> <task prototype> [force] | swtask complete <faction> <task number from swscore>
sw-cmd-task-unknown-action = Unknown action { $action }: use add or complete.
sw-cmd-task-no-task = { $task } is not a task prototype (no SWTask component).
sw-cmd-task-requirements = The requirements of task { $task } are not met (for example, no target with the right job). Add force to give it anyway.
sw-cmd-task-added = Task "{ $task }" given to { $faction }, its number is { $number }.
sw-cmd-task-bad-number = The task number must be from 1 to { $count }.
sw-cmd-task-completed = Task "{ $task }" completed, score: { $score }.
sw-cmd-task-hint-action = <add|complete>
sw-cmd-task-hint-task = <task prototype>
sw-cmd-task-hint-number = <task number>
sw-cmd-task-hint-force = [force]

cmd-swscore-desc = Shows the live score, money and tasks of every faction, and who wins if the round ends now.
cmd-swscore-help = Usage: swscore [faction]
sw-cmd-score-faction = == { $name } ({ $id }): { $score } points
sw-cmd-score-money = Money: { $money }
sw-cmd-score-task = { $number }. { $name } [{ $proto }]: { $progress }%, score { $score }
sw-cmd-score-draw = draw
sw-cmd-score-winner = If the round ends now, the winner is: { $winners }

cmd-swmoney-desc = Adds money or supply points to a faction (a negative amount takes them away).
cmd-swmoney-help = Usage: swmoney <faction> <currency> <amount>
sw-cmd-money-no-currency = Currency { $currency } does not exist.
sw-cmd-money-done = { $faction } now has { $amount } { $currency }.
sw-cmd-money-hint-currency = <currency>
sw-cmd-money-hint-amount = <amount>

cmd-swbots-desc = Spawns soldier bots of a faction where you stand or at a map point.
cmd-swbots-help = Usage: swbots <faction> <count> [Hold|EnemyBase|Centre|Towers|Point] [x y [mapId]]. Point guards the spawn point. EnemyBase is the default.
sw-cmd-bots-no-bot = Faction { $faction } has no bot prototype (SWCombatBot component).
sw-cmd-bots-bad-count = The bot count must be from 1 to { $max }.
sw-cmd-bots-bad-objective = Unknown objective { $objective }.
sw-cmd-bots-no-position = No spawn point: enter the game (aghost) or give x y and mapId.
sw-cmd-bots-spawned = Spawned { $count } bots of { $faction }, objective { $objective }.
sw-cmd-bots-hint-count = <count>
sw-cmd-bots-hint-objective = [objective]
