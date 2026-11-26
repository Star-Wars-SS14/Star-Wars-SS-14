sw-global-war-ghost-warp-surface = { $planet }: поверхность
sw-global-war-ghost-warp-orbit = { $planet }: орбита
sw-global-war-ghost-warp-unloaded = Сюда ещё никто не приходил. Первым мир загружает только администратор.
sw-global-war-ghost-warp-failed = Этот мир сейчас недоступен.
sw-global-war-admin-actor-no-owner = Админ: у этого места нет владельца. Выберите сторону командой gwadminside republic или gwadminside empire.
cmd-gwadminside-desc = Выбирает сторону, за которую админ-призрак или тело вне кампании действует у консолей и в захвате.
cmd-gwadminside-help = Использование: gwadminside <republic|empire|auto>. auto — сторона владельца места.
cmd-gwadminside-invalid = Нет такой стороны: { $side }. Нужно republic, empire или auto.
cmd-gwadminside-set = Теперь вы действуете за { $side ->
    [Republic] Республику
    [Empire] Империю
   *[other] { $side }
}.
cmd-gwadminside-auto = Теперь вы действуете за владельца места.
