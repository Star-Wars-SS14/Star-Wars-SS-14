sw-droid-command-no-droids-nearby = No droids nearby.
sw-droid-command-accepted = Droid command accepted.
sw-droid-module-category-limit = This droid already has a module of that category installed.
sw-droid-targeting-tablet-linked = Droid targeting tablet linked.
sw-droid-targeting-tablet-locked = Unlock the droid first.
sw-droid-targeting-tablet-wrong-faction = This tablet is incompatible with this droid.
sw-targeting-console-target-humanoids-without-id = Target humanoids without ID
sw-targeting-console-target-animals = Target animals
sw-droid-control-datapad-window-title = Droid control datapad
sw-droid-control-datapad-tab-droids = Droids
sw-droid-control-datapad-tab-targets = Targets
sw-droid-control-datapad-tab-map = Map
sw-droid-control-datapad-droids-count = Linked droids: {$count}
sw-droid-control-datapad-map-droids-count = Droids on current grid: {$count}
sw-droid-control-datapad-no-linked-droids = No linked droids.
sw-droid-control-datapad-no-map-droids = No linked droids on your current grid.
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
sw-droid-control-datapad-map-selected = {$name}
    Status: {$status}
    Charge: {$charge}
sw-droid-control-datapad-map-entry-details = {$status}, {$charge}
sw-droid-control-datapad-targets-enabled = Targeted jobs
sw-droid-control-datapad-targets-disabled = Ignored jobs
sw-droid-control-datapad-faction-allow = Target all {$faction}
sw-droid-control-datapad-faction-disallow = Ignore all {$faction}

ent-SWMobDroidBase = battle droid
    .desc = A compact combat droid with modular systems.

ent-SWMobDroid = battle droid
    .suffix = AI
    .desc = { ent-SWMobDroidBase.desc }

ent-ActionSWDroidTakeCommand = Take command
    .desc = Assume command of a nearby allied droid.

ent-ActionSWDroidOrderFollow = Follow
    .desc = Ordered droids follow you.

ent-ActionSWDroidOrderStay = Stay
    .desc = Ordered droids hold position.

ent-SWDroidTargetingTablet = droid targeting tablet
    .desc = A portable droid targeting configuration terminal.

ent-SWDroidTargetingTabletRepublic = Republic droid targeting tablet
    .suffix = Republic
    .desc = A portable droid targeting configuration terminal preset to target Imperial personnel.

ent-SWDroidTargetingTabletEmpire = Imperial droid targeting tablet
    .suffix = Empire
    .desc = A portable droid targeting configuration terminal preset to target Republic personnel.

ent-SWDroidChargingPad = droid charging pad
    .desc = A flat powered platform that recharges droids standing on it.
