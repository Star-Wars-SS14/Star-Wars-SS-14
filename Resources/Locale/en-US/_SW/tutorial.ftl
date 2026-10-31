sw-tutorial-choice-question = Have you played Space Station 14?
sw-tutorial-choice-new-to-game = No, teach me (about 5 minutes)
sw-tutorial-choice-new-to-server = Yes, show me what is different here (about 3 minutes)
sw-tutorial-choice-new-to-server-soon = Yes, show me what is different here (soon)
sw-tutorial-choice-skip = Skip
sw-tutorial-choice-back = Back
sw-tutorial-choice-hint = You can come back to the tutorial at any time with the button in the lobby.
sw-tutorial-lobby-button = Tutorial
sw-tutorial-exit-button = Exit
sw-tutorial-return-button = Back to the fight

sw-tutorial-step-walk = Walk to the arrow: { $up } { $left } { $down } { $right }
sw-tutorial-step-walk-big = { $up } { $left } { $down } { $right } to walk
sw-tutorial-step-rifle = Walk up to the rifle and click it
sw-tutorial-step-rifle-far = Too far. Come closer
sw-tutorial-step-rifle-busy = That hand is full. { $swap }: the other hand
sw-tutorial-step-combat = Guns stay silent outside combat mode: press the framed button
sw-tutorial-step-combat-mistake = Combat mode first: the framed button
sw-tutorial-step-target = Click the target to shoot
sw-tutorial-step-target-wield = Missing? { $wield }: hold the rifle in both hands
sw-tutorial-step-target-near = The target is closer now. { $wield }: both hands, then shoot
sw-tutorial-step-target-one-hand = A lucky one-handed shot. New target: { $wield }, both hands
sw-tutorial-step-corridor = The target is down. Walk to the arrow, the corridor is next
sw-tutorial-step-enemy = A badge over the head: a friend. No badge: shoot
sw-tutorial-step-dropped-rifle = The rifle is on the floor. Walk up and click it
sw-tutorial-step-dropped-injector = The injector is not in your hand. Pick it up or take the second one from the pocket
sw-tutorial-step-enemy-friend = That is a friend: see the badge
sw-tutorial-step-wounded = You are hit. Click the injector in your pocket, at the bottom
sw-tutorial-step-inject = { $wield }: inject yourself
sw-tutorial-step-radio = Report on the radio: Enter, then :у ready, Enter
sw-tutorial-step-radio-local = Only people nearby hear that. Start the line with :у
sw-tutorial-step-dead = You died. In a battle, each death spends your side's reserve
sw-tutorial-step-shuttle = Walk to the shuttle: the real war is there

sw-tutorial-popup-wounded = A shot from around the corner
sw-tutorial-popup-volley = A volley

sw-tutorial-finish-title = Training is over
sw-tutorial-finish-next = In a round, find your senior and follow orders. No senior: go to the front.
sw-tutorial-finish-republic = For the Republic: { $alive } alive { $weaker ->
    [true] (smaller side)
   *[false] {""}
}
sw-tutorial-finish-empire = For the Empire: { $alive } alive { $weaker ->
    [true] (smaller side)
   *[false] {""}
}
sw-tutorial-finish-wait = The round has not started: press Ready in the lobby.
sw-tutorial-finish-close = To the lobby

sw-tutorial-error-already-started = You are already in the tutorial.
sw-tutorial-error-not-started = You are not in the tutorial.
sw-tutorial-error-not-in-lobby = The tutorial starts only from the lobby.
sw-tutorial-error-not-loaded = Your data is still loading, try again in a few seconds.
sw-tutorial-error-map = Could not load the tutorial map.
sw-tutorial-error-full = Too many players are in the tutorial right now, try again in a couple of minutes.
sw-tutorial-error-mode-not-ready = This tutorial mode is not ready yet.
sw-tutorial-error-no-slot = This side has no places left. Pick a role with Join in the lobby.

cmd-swtutorial-desc = Starts the tutorial or leaves it.
cmd-swtutorial-help = Usage: swtutorial [new|server|exit]

job-name-sw-tutorial-soldier = Student
job-description-sw-tutorial-soldier = In training. This job does not exist in a real round.

sw-tutorial-radio-channel = Training channel
