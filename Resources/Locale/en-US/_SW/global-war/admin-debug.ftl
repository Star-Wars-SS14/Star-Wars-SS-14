sw-global-war-ghost-warp-surface = { $planet }: surface
sw-global-war-ghost-warp-orbit = { $planet }: orbit
sw-global-war-ghost-warp-unloaded = Nobody has been here yet. Only an admin can load a world first.
sw-global-war-ghost-warp-failed = This world is unavailable right now.
sw-global-war-admin-actor-no-owner = Admin: this place has no owner. Pick a side with gwadminside republic or gwadminside empire.
cmd-gwadminside-desc = Picks the side an admin ghost or a body outside the campaign acts for at consoles and in captures.
cmd-gwadminside-help = Usage: gwadminside <republic|empire|auto>. auto is the side that owns the location.
cmd-gwadminside-invalid = No such side: { $side }. Use republic, empire or auto.
cmd-gwadminside-set = You now act for the { $side ->
    [Republic] Republic
    [Empire] Empire
   *[other] { $side }
}.
cmd-gwadminside-auto = You now act for the owner of the location.
