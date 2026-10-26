sw-droid-command-no-droids-nearby = No droids nearby.
sw-droid-command-accepted = Droid command accepted.
sw-droid-command-relinquished = Droid command relinquished.
sw-droid-module-category-limit = This droid already has a module of that category installed.
sw-droid-targeting-tablet-linked = Droid targeting tablet linked.
sw-droid-targeting-tablet-locked = Unlock the droid first.
sw-droid-targeting-tablet-wrong-faction = This tablet is incompatible with this droid.
sw-droid-targeting-tablet-disabled = This datapad has been remotely disabled.
sw-droid-targeting-tablet-examine-control-id = Control ID: {$id}. Status: {$status}.
sw-droid-repair-missing-steel = You need steel to repair the droid.
sw-targeting-console-target-humanoids-without-id = Target humanoids without ID
sw-targeting-console-target-animals = Target animals
sw-droid-control-console-window-title = Droid control console
sw-droid-control-console-datapads-count = Control datapads: {$count}
sw-droid-control-console-no-datapads = No registered control datapads.
sw-droid-control-console-status-enabled = Enabled
sw-droid-control-console-status-disabled = Disabled
sw-droid-control-console-datapad-heading = {$id} - {$name}
sw-droid-control-console-datapad-details = Status: {$status}; linked droids: {$count}
sw-droid-control-console-disable = Disable
sw-droid-control-console-enable = Enable
sw-droid-control-datapad-window-title = Droid control datapad
sw-droid-control-datapad-disabled-overlay = DISABLED
sw-droid-control-datapad-tab-droids = Droids
sw-droid-control-datapad-tab-targets = Targets
sw-droid-control-datapad-tab-map = Map
sw-droid-control-datapad-droids-count = Linked droids: {$count}
sw-droid-control-datapad-map-droids-count = Tracked droids: {$count}
sw-droid-control-datapad-no-linked-droids = No linked droids.
sw-droid-control-datapad-no-map-droids = No linked droids are being tracked.
sw-droid-control-datapad-map-unavailable = Droid map unavailable.
sw-droid-control-datapad-unknown = Unknown
sw-droid-control-datapad-no-battery = No battery
sw-droid-control-datapad-charge-percent = {$percent}%
sw-droid-control-datapad-coordinates = {$x}, {$y}
sw-droid-control-datapad-status-alive = Operational
sw-droid-control-datapad-status-critical = Damaged
sw-droid-control-datapad-status-dead = Broken
sw-droid-control-datapad-status-unknown = Unknown
sw-droid-control-datapad-droid-status = Status: {$status}
sw-droid-control-datapad-droid-charge = Charge: {$charge}
sw-droid-control-datapad-droid-coordinates = Coordinates: {$coordinates}
sw-droid-control-datapad-follow-datapad = Follow datapad
sw-droid-control-datapad-stop-following = Stop following
sw-droid-control-datapad-map-selected = {$name}
    Status: {$status}
    Charge: {$charge}
sw-droid-control-datapad-map-entry-details = {$status}, {$charge}
sw-droid-control-datapad-targets-enabled = Ignored jobs
sw-droid-control-datapad-targets-disabled = Targeted jobs
sw-droid-control-datapad-faction-allow = Ignore all {$faction}
sw-droid-control-datapad-faction-disallow = Target all {$faction}

ent-SWMobDroidBase = Mark II
    .desc = A compact combat droid with modular systems.

ent-SWMobDroid = Mark II
    .suffix = AI
    .desc = { ent-SWMobDroidBase.desc }

ent-SWMobDroidChassis = Mark II chassis
    .suffix = Empty
    .desc = An empty Mark II droid chassis awaiting modules.

ent-ActionSWDroidTakeCommand = Take command
    .desc = Assume command of a nearby allied droid.

ent-ActionSWDroidRelinquishCommand = Relinquish command
    .desc = Release command of a nearby controlled droid.

ent-ActionSWDroidCommandMenu = Droid commands
    .desc = Open the command wheel for controlled droids.

ent-ActionSWDroidOrderFollow = Follow
    .desc = Ordered droids follow you.

ent-ActionSWDroidOrderStay = Stay
    .desc = Ordered droids hold position.

ent-ActionSWDroidOrderGoTo = Go!
    .desc = Ordered droids move to the selected point and hold there.

ent-ActionSWDroidOrderAttack = Fire!
    .desc = Ordered droids fire at the selected target.

ent-ActionSWDroidOrderDismiss = Dismiss!
    .desc = Ordered droids stop executing their current order.

ent-SWDroidChargingPad = droid charging pad
    .desc = A flat powered platform that recharges droids standing on it.

ent-SWDroidControlConsoleEmpire = imperial droid control console
    .suffix = Empire
    .desc = A command console for monitoring and disabling Imperial droid control datapads.

ent-SWDroidControlConsoleRepublic = republic droid control console
    .suffix = Republic
    .desc = A command console for monitoring and disabling Republic droid control datapads.
