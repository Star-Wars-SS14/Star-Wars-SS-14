using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>Local, persistent help; opening it never submits a ministry action.</summary>
public sealed class SWHealthNetworkHelpPanel : BoxContainer
{
    private readonly RichTextLabel _limits = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);

    public SWHealthNetworkHelpPanel(bool standalone = false)
    {
        Name = "HealthNetworkHelp";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 6;
        var toggle = SWConsoleStyle.Action("Справка по медсети", SWConsoleStyle.Accent);
        toggle.Name = "HealthNetworkHelpToggle";
        toggle.Visible = !standalone;
        var panel = SWConsoleStyle.Row();
        panel.Name = "HealthNetworkHelpBody";
        panel.Visible = standalone;
        var body = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8,
        };
        panel.AddChild(body);
        AddChild(toggle);
        AddChild(panel);
        toggle.OnPressed += _ =>
        {
            panel.Visible = !panel.Visible;
            toggle.Text = panel.Visible ? "Скрыть справку" : "Справка по медсети";
        };

        Section("Разделы медсети",
            "«Лечение» — назначение бригад. «Развитие» — клиники, профилактика и запас помощи. " +
            "В «Снабжении» выберите заявку, чтобы увидеть её материалы и ход выполнения. " +
            "«Состояние» показывает эффект вакцинации и динамику заражения; там же находится журнал. Карантин вынесен в отдельную вкладку консоли.");
        Section("Очередь лечения",
            "Выберите мир и вид помощи. Очередь — объём ещё не завершённого лечения в курсах, включая уже назначенные. " +
            "Миры начинают с накопленными заявками; очередь растёт, когда обычное снабжение не покрывает потребности. «Назначено» показывает оставшуюся работу по текущей заявке; " +
            "«Новая заявка» — сколько курсов можно заказать сейчас. Плановая помощь доступна и без эпидемии.");
        body.AddChild(SWConsoleStyle.Wrap("Бригады и оплата", SWConsoleStyle.Brass));
        body.AddChild(_limits);
        body.AddChild(SWConsoleStyle.Wrap(
            "Стоимость списывается один раз при заказе. Бригада начинает лечение после доставки полного комплекта материалов. " +
            "Если все бригады заняты, готовая заявка ждёт освобождения одной из них. " +
            "Срок работы отсчитывается с начала лечения; доставка и ожидание бригады занимают дополнительное время. Ночью лечение и доставка приостанавливаются.",
            SWConsoleStyle.TextBright));
        Section("Материалы и доставка",
            "Регенеративная помощь расходует колто и медматериалы, протезирование — импланты и медматериалы. " +
            "«Склад» показывает общий запас Республики. Поставки учитывают обычное потребление, транспорт и доступность маршрута. " +
            "Полученные материалы, груз в пути и срок прибытия видны в заявке; движение медицинских конвоев — на карте.");
        Section("Результат лечения",
            "По мере лечения очередь сокращается, а её штраф к здравоохранению сектора уменьшается. " +
            "Этот эффект показан над карточками помощи. Иммунитет повышают отдельные меры: профилактика и применение доставленной вакцины.");
        Section("Клиники, профилактика и помощь",
            "Клиники улучшают местное обслуживание и ускоряют применение доз. Профилактическая вакцинация обеспечивает дозы, " +
            "которые местные службы используют постепенно. Медицинская помощь создаёт временный запас для снижения заражения. " +
            "Эти меры тоже требуют доставки материалов; одна оплата ещё не даёт результата.");
        Section("Показатели мира",
            "Заражение показывает масштаб вспышки, иммунитет — накопленную защиту, охват — обеспеченность обычной медицинской помощью. " +
            "Запасы пищи и лекарств указаны в часах базового потребления.");
        Section("Карантин",
            "Выберите мир, несколько миров одной звёздной системы или весь сектор. Внутренние перевозки зоны сохраняются. " +
            "Карантин вводится после одобрения канцлером, а в его отсутствие — спикером. " +
            "Если оба поста свободны, запрос обрабатывает автопилот. Карантин ограничивает обычный импорт и экспорт; " +
            "гуманитарный коридор разрешает входящие поставки пищи и лекарств. Адресные конвои Минздрава проходят любой карантин, " +
            "но военная блокада может остановить их.");

        void Section(string title, string text)
        {
            body.AddChild(SWConsoleStyle.Wrap(title, SWConsoleStyle.Brass));
            body.AddChild(SWConsoleStyle.Wrap(text, SWConsoleStyle.TextBright));
        }
    }

    public void SetState(SWHealthCareView care) => _limits.SetMessage(
        $"Одновременно работают до {care.TeamLimit} бригад. Всего можно держать до {care.OrderLimit} открытых заявок, включая ожидающие снабжения.");
}
