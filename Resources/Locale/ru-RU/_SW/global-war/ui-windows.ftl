sw-global-war-fleet-queue-title = Очередь
sw-global-war-fleet-queue-empty = Заказов нет.
sw-global-war-fleet-buy = Заказать
sw-global-war-round-end-stat-republic = Миры Республики
sw-global-war-round-end-stat-empire = Миры Империи
sw-global-war-round-end-stat-other = Спорные и нейтральные
sw-global-war-factory-cost = { $have } / { $need }
sw-global-war-capture-word-you = ВЫ
sw-global-war-capture-word-attacker = АТАКУЕТ
sw-global-war-capture-word-owner = ВЛАДЕЛЕЦ
sw-global-war-capture-word-capturing = ИДЁТ ЗАХВАТ
sw-global-war-capture-word-contested = ЗОНА ОСПАРИВАЕТСЯ
sw-global-war-capture-word-abandoned = НЕТ АТАКУЮЩИХ
sw-global-war-capture-word-unpowered = НЕТ ПИТАНИЯ
sw-global-war-capture-word-unavailable = ЦЕЛЬ НЕДОСТУПНА
sw-global-war-capture-word-completed = ВЛАДЕНИЕ ИЗМЕНЕНО
sw-global-war-capture-word-idle = ЗАХВАТ НЕ НАЧАТ
sw-global-war-capture-before-reset = ДО СБРОСА
sw-global-war-capture-line-capturing = Живой враг в зоне остановит отсчёт.
sw-global-war-capture-line-contested = Уберите врага из зоны.
sw-global-war-capture-line-abandoned = Вернитесь в зону, иначе удержание сбросится.
sw-global-war-capture-line-unpowered = Почините консоль и питание. Удержание на паузе.
sw-global-war-capture-line-completed = { $place ->
    [surface] Поверхность
    [ship] Корабль
   *[station] Станция
} теперь у { $owner ->
    [republic] Республики
    [empire] Империи
   *[other] нейтральной стороны
}.
sw-global-war-capture-need = Нужно удержать: { $total } с
sw-global-war-capture-unavailable-short = Нет цели или назначения.
sw-global-war-capture-denied-short = Проверьте назначение, орбиту, дипломатию и питание.
sw-global-war-capture-hold-short = Для военных. Поверхности нужна своя орбита.
sw-global-war-round-end-stat-other-count = Спорные и нейтральные: { $count }
sw-global-war-round-end-heading = ИТОГ КАМПАНИИ
sw-global-war-round-end-timeline = Ход войны
sw-global-war-round-end-minutes = { $minutes } мин
sw-global-war-round-end-changes = { $changes } { $changes ->
    [one] смена
    [few] смены
   *[other] смен
} владельца за кампанию
sw-global-war-round-end-stamp-won = ВАША СТОРОНА ПОБЕДИЛА
sw-global-war-round-end-stamp-lost = ВАША СТОРОНА ПРОИГРАЛА
sw-global-war-round-end-stamp-open = БЕЗ ПОБЕДИТЕЛЯ
sw-global-war-fleet-budget-word = ДОСТУПНО В ВОЕННОМ БЮДЖЕТЕ
sw-global-war-recon-age = { $age } с назад
sw-global-war-respawn-word-wait = ДО ВОЗВРАТА
sw-global-war-respawn-word-ready = СРОК ПРОШЁЛ

sw-global-war-respawn-citizen-title = Гражданин
sw-global-war-respawn-citizen-note = Новый персонаж той же стороны. Должность и снаряжение не сохранятся.
sw-global-war-respawn-citizen-note-force = Новый персонаж той же стороны. Должность и снаряжение не сохранятся. Место { $faction ->
    [empire] ситха
   *[republic] джедая
} освободится.
sw-global-war-respawn-force-title = { $faction ->
    [empire] Вы ситх. Можно вернуться им же
   *[republic] Вы джедай. Можно вернуться им же
}
sw-global-war-respawn-force-home = Появитесь на { $faction ->
    [empire] Коррибане
   *[republic] Тайтоне
}. Меч остался на теле.
sw-global-war-respawn-force-back = Вернуться { $faction ->
    [empire] ситхом
   *[republic] джедаем
}
sw-global-war-fleet-buy-closed = Недоступно здесь
sw-global-war-fleet-buy-poor = Не хватает денег
sw-global-war-hub-word = ОРБИТАЛЬНЫЙ ХАБ

sw-global-war-finance-treasury-free = КАЗНА, СВОБОДНО
sw-global-war-finance-totals = Всего { $balance } ГК, в резерве { $reserved } ГК
sw-global-war-finance-state-money = ДЕНЬГИ ГОСУДАРСТВА: { $sum } ГК
sw-global-war-finance-share = { $name } { $percent }%
sw-global-war-finance-short-treasury = Казна
sw-global-war-finance-short-war = Война
sw-global-war-finance-short-logistics = Логистика
sw-global-war-finance-short-industry = Промышленность
sw-global-war-finance-about-war = Флот, захваты, оружие
sw-global-war-finance-about-logistics = Хабы, поставки, потоки
sw-global-war-finance-about-industry = Заводы, сырьё, линии
sw-global-war-finance-total-short = Всего { $sum } ГК
sw-global-war-finance-reserved-short = В резерве { $sum } ГК
sw-global-war-finance-transfer-here = Перевести сюда
sw-global-war-finance-transfer-back = Вернуть в казну
sw-global-war-finance-new-order-plus = + Новое поручение
sw-global-war-finance-new-order = Новое поручение
sw-global-war-finance-new-treasury-order = + Поручение из казны
sw-global-war-finance-amount-all = Всё
sw-global-war-finance-release-short = Снять
sw-global-war-finance-order-who = ещё { $time }, { $employee }
sw-global-war-fleet-cancel-short = Отмена
sw-global-war-latejoin-title = Выбор стороны и должности
sw-global-war-round-end-summary = Подробности
sw-guide-entry-campaign = Глобальная война

## Карта галактики
sw-global-war-galaxy-button = Карта
sw-global-war-galaxy-title = Карта галактики
sw-global-war-galaxy-legend-republic = Синий: Республика
sw-global-war-galaxy-legend-empire = Красный: Империя
sw-global-war-galaxy-legend-split = Штриховка: поверхность у другой стороны
sw-global-war-galaxy-count = Республика { $republic } · Империя { $empire } · Разделены { $split }
sw-global-war-galaxy-station = Станция: { $side }
sw-global-war-galaxy-surface = Поверхность: { $side }
sw-global-war-galaxy-split = Контроль разделён
sw-global-war-galaxy-hint = Владельцы обновляются сами, пока окно открыто. Выберите планету, чтобы увидеть, кто держит станцию и поверхность.

## Рынок: формы потоков и роста
sw-global-war-market-form-route = Маршрут
sw-global-war-market-form-from = Откуда
sw-global-war-market-form-to = Куда
sw-global-war-market-form-rate = Темп
sw-global-war-market-form-rate-unit = Норма, ед./мин
sw-global-war-market-form-order = Приоритет
sw-global-war-market-form-right = Право на изменение
sw-global-war-market-form-enabled = Поток включён
sw-global-war-market-form-head = Решение главы
sw-global-war-market-form-growth = Пища на рост, % (0–25)
sw-global-war-market-form-ln = Резерв Ln
sw-global-war-market-form-world = Рост мира
sw-global-war-market-form-delegate = ▸ Поручить работнику
sw-global-war-market-form-delegate-open = ▾ Поручить работнику
