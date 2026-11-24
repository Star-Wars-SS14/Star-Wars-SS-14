using System.Linq;
using System.Numerics;
using Content.Client.Message;
using Content.Client._SW._Senate;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.RoundEnd
{
    public sealed class RoundEndSummaryWindow : DefaultWindow
    {
        private readonly IEntityManager _entityManager;
        public int RoundId;
        public bool IsSenate { get; private set; }
        private RichTextLabel? _personalHistory;
        private RichTextLabel? _personalIdentity;
        private Button? _replayFinale;

        /// <summary>Hides the outro replay once its round is over, so an old chronicle never plays in a new round.</summary>
        public void SetFinaleReplayAvailable(bool available)
        {
            if (_replayFinale != null)
                _replayFinale.Visible = available;
        }

        public RoundEndSummaryWindow(string gm, string roundEnd, TimeSpan roundTimeSpan, int roundId,
            RoundEndMessageEvent.RoundEndPlayerInfo[] info, IEntityManager entityManager, SWSenateRoundHistory? history = null, Action? replayFinale = null, Action? requestPersonal = null)
        {
            _entityManager = entityManager;
            IsSenate = history != null;

            MinSize = history == null ? new Vector2(520, 580) : new Vector2(720, 500);
            SetSize = history == null ? new Vector2(520, 580) : new Vector2(1000, 620);

            Title = Loc.GetString("round-end-summary-window-title");

            // The round end window is split into two tabs, one about the round stats
            // and the other is a list of RoundEndPlayerInfo for each player.
            // This tab would be a good place for things like: "x many people died.",
            // "clown slipped the crew x times.", "x shots were fired this round.", etc.
            // Also good for serious info.

            RoundId = roundId;
            var roundEndTabs = new TabContainer();
            if (history != null)
            {
                roundEndTabs.AddChild(MakePersonalTab(replayFinale, requestPersonal));
                roundEndTabs.AddChild(MakeSenateHistoryTab(history, roundTimeSpan));
            }
            roundEndTabs.AddChild(MakeRoundEndSummaryTab(gm, roundEnd, roundTimeSpan, roundId));
            roundEndTabs.AddChild(MakePlayerManifestTab(info));

            ContentsContainer.AddChild(roundEndTabs);

            OpenCenteredRight();
            MoveToFront();
        }

        private BoxContainer MakePersonalTab(Action? replayFinale, Action? requestPersonal)
        {
            var tab = new BoxContainer { Orientation = LayoutOrientation.Vertical, Name = "Мои итоги" };
            if (replayFinale != null)
            {
                _replayFinale = new Button { Text = "Повторить аутро", ToolTip = "Хроника созыва", HorizontalAlignment = HAlignment.Left, Margin = new Thickness(20, 10) };
                _replayFinale.OnPressed += _ => replayFinale();
                tab.AddChild(_replayFinale);
            }
            var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Margin = new Thickness(20) };
            var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 12 };
            content.AddChild(SWConsoleStyle.Text("МОИ ИТОГИ", SWConsoleStyle.Brass, SWConsoleStyle.Title));
            content.AddChild(SWConsoleStyle.Wrap("Сведения отправляются только вам после завершения созыва.", SWConsoleStyle.TextDim));
            content.AddChild(SWConsoleStyle.Section("ИГРОК И ПЕРСОНАЖИ"));
            _personalIdentity = SWConsoleStyle.Wrap("Загрузка личных сведений…", SWConsoleStyle.TextBright);
            content.AddChild(_personalIdentity);
            content.AddChild(SWConsoleStyle.Section("ЛИЧНЫЙ РЕЗУЛЬТАТ"));
            _personalHistory = SWConsoleStyle.Wrap("Загрузка личных результатов…", SWConsoleStyle.TextBright);
            content.AddChild(_personalHistory);
            if (requestPersonal != null)
            {
                var retry = new Button { Text = "Обновить личные итоги", HorizontalAlignment = HAlignment.Left };
                retry.OnPressed += _ => requestPersonal();
                content.AddChild(retry);
            }
            scroll.AddChild(content);
            tab.AddChild(scroll);
            return tab;
        }

        private BoxContainer MakeSenateHistoryTab(SWSenateRoundHistory history, TimeSpan duration)
        {
            var tab = new BoxContainer { Orientation = LayoutOrientation.Vertical, Name = "История Сената" };
            var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Margin = new Thickness(20) };
            var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 12 };

            void Add(string text)
            {
                content.AddChild(SWConsoleStyle.Wrap(text, SWConsoleStyle.TextBright));
            }
            void Section(string text) => content.AddChild(SWConsoleStyle.Section(text));

            content.AddChild(SWConsoleStyle.Text("ИСТОРИЯ СЕНАТА", SWConsoleStyle.Brass, SWConsoleStyle.Title));
            Add($"История Республики • игровых дней: {history.Days} • длительность раунда: {(int) duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");
            Section("УЧАСТНИКИ");
            if (history.Participants.Length == 0) Add("Участники не зафиксированы.");
            foreach (var participant in history.Participants) Add(participant);
            if (history.CompletedAmbitions.Length > 0)
            {
                Section("ВСЕ АМБИЦИИ ВЫПОЛНЕНЫ");
                foreach (var name in history.CompletedAmbitions) Add(name);
            }
            Section("ПОКАЗАТЕЛИ РЕСПУБЛИКИ");
            Add("Начало → конец (изменение за созыв)");
            if (history.Stats.Length == 0)
                Add("Данные о показателях недоступны.");
            foreach (var stat in history.Stats)
                Add($"{stat.Name}: {stat.Start:0.0} → {stat.End:0.0} ({stat.End - stat.Start:+0.0;-0.0;0.0})");

            Section("ПОКАЗАТЕЛИ СЕКТОРОВ");
            if (history.Sectors.Length == 0)
                Add("Данные о секторах недоступны.");
            foreach (var sector in history.Sectors)
            {
                Add(sector.Name);
                foreach (var stat in sector.Stats)
                    Add($"{stat.Name}: {stat.Start:0.0} → {stat.End:0.0} ({stat.End - stat.Start:+0.0;-0.0;0.0})");
            }
            Section("ПРИНЯТЫЕ ЗАКОНЫ И ПОПРАВКИ");
            if (history.Laws.Length == 0)
                Add("Принятых законов не зафиксировано.");
            foreach (var law in history.Laws)
                Add($"• {law.Name} — автор: {law.Author}{(law.Amendment ? " (поправка)" : "")}{(law.Repealed ? " (впоследствии отменён)" : "")}");
            if (history.AdditionalLaws > 0)
                Add($"Ещё решений: {history.AdditionalLaws}.");
            Section("ЗАВЕРШЁННЫЕ ПРОЕКТЫ");
            if (history.Projects.Length == 0) Add("Завершённые проекты не зафиксированы в опубликованных данных.");
            foreach (var project in history.Projects) Add($"• {project}");
            Section("ЭКОНОМИЧЕСКИЕ СОБЫТИЯ · ПОСЛЕДНИЕ 20");
            if (history.Events.Length == 0) Add("Экономических событий не зафиксировано.");
            foreach (var ev in history.Events) Add($"• {ev}");
            Section("ВЕДОМСТВА И СУД");
            foreach (var line in history.DepartmentSummary) Add(line);
            Add("Подтверждённая история остальных ведомств недоступна.");
            if (history.CloseVotes.Length > 0)
            {
                Section("ЗАЛ РАЗДЕЛИЛСЯ");
                foreach (var vote in history.CloseVotes)
                    Add($"{vote.Name}: за {vote.For}, против {vote.Against}; решающий голос не включён.");
            }
            if (history.Awards.Length > 0)
            {
                Section("ТИТУЛЫ СОЗЫВА");
                foreach (var award in history.Awards)
                {
                    Add(award.Title);
                    Add(award.Fact);
                    foreach (var recipient in award.Recipients) Add(recipient);
                }
                if (history.AdditionalAwards > 0) Add($"Ещё категорий: {history.AdditionalAwards}.");
            }
            scroll.AddChild(content);
            tab.AddChild(scroll);
            return tab;
        }

        public void SetPersonalHistory(SWSenatePersonalHistoryResponse response)
        {
            if (response.RoundId != RoundId || _personalHistory == null || _personalIdentity == null)
                return;
            var identity = new FormattedMessage();
            identity.AddText($"Игрок: {response.Player}\n");
            if (response.Characters.Length == 0)
                identity.AddText("Подтверждённые сведения о персонаже недоступны.");
            foreach (var character in response.Characters)
            {
                var name = string.IsNullOrWhiteSpace(character.Name) ? "Имя не указано" : character.Name;
                var role = character.Observer ? "Наблюдатель" : Loc.GetString(character.Role);
                identity.AddText($"{name} — {role}\n");
            }
            _personalIdentity.SetMessage(identity);
            var message = new FormattedMessage();
            message.AddText(response.Lines.Length == 0
                ? "Нет подтверждённого личного результата. Для должностей без надёжной привязки или при смене владельца оценка недоступна."
                : string.Join("\n", response.Lines));
            _personalHistory.SetMessage(message);
        }

        private BoxContainer MakeRoundEndSummaryTab(string gamemode, string roundEnd, TimeSpan roundDuration, int roundId)
        {
            var roundEndSummaryTab = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Name = Loc.GetString("round-end-summary-window-round-end-summary-tab-title")
            };

            var roundEndSummaryContainerScrollbox = new ScrollContainer
            {
                HScrollEnabled = false, //SW long task lines wrap instead of running off the window
                VerticalExpand = true,
                Margin = new Thickness(10)
            };
            var roundEndSummaryContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical
            };

            //Gamemode Name
            var gamemodeLabel = new RichTextLabel();
            var gamemodeMessage = new FormattedMessage();
            gamemodeMessage.AddMarkupOrThrow(Loc.GetString("round-end-summary-window-round-id-label", ("roundId", roundId)));
            gamemodeMessage.AddText(" ");
            gamemodeMessage.AddMarkupOrThrow(Loc.GetString("round-end-summary-window-gamemode-name-label", ("gamemode", gamemode)));
            gamemodeLabel.SetMessage(gamemodeMessage);
            roundEndSummaryContainer.AddChild(gamemodeLabel);

            //Duration
            var roundTimeLabel = new RichTextLabel();
            roundTimeLabel.SetMarkup(Loc.GetString("round-end-summary-window-duration-label",
                                                   ("hours", roundDuration.Hours),
                                                   ("minutes", roundDuration.Minutes),
                                                   ("seconds", roundDuration.Seconds)));
            roundEndSummaryContainer.AddChild(roundTimeLabel);

            //Round end text
            if (!string.IsNullOrEmpty(roundEnd))
            {
                var roundEndLabel = new RichTextLabel();
                roundEndLabel.SetMarkup(roundEnd);
                roundEndSummaryContainer.AddChild(roundEndLabel);
            }

            roundEndSummaryContainerScrollbox.AddChild(roundEndSummaryContainer);
            roundEndSummaryTab.AddChild(roundEndSummaryContainerScrollbox);

            return roundEndSummaryTab;
        }

        private BoxContainer MakePlayerManifestTab(RoundEndMessageEvent.RoundEndPlayerInfo[] playersInfo)
        {
            var playerManifestTab = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Name = Loc.GetString("round-end-summary-window-player-manifest-tab-title")
            };

            var playerInfoContainerScrollbox = new ScrollContainer
            {
                HScrollEnabled = false, //SW
                VerticalExpand = true,
                Margin = new Thickness(10)
            };
            var playerInfoContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical
            };

            //Put observers at the bottom of the list. Put antags on top.
            var sortedPlayersInfo = playersInfo.OrderBy(p => p.Observer).ThenBy(p => !p.Antag);

            //Create labels for each player info.
            foreach (var playerInfo in sortedPlayersInfo)
            {
                var hBox = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                };

                var playerInfoText = new RichTextLabel
                {
                    VerticalAlignment = VAlignment.Center,
                    VerticalExpand = true,
                };

                if (playerInfo.PlayerNetEntity != null)
                {
                    hBox.AddChild(new SpriteView(playerInfo.PlayerNetEntity.Value, _entityManager)
                        {
                            OverrideDirection = Direction.South,
                            VerticalAlignment = VAlignment.Center,
                            SetSize = new Vector2(32, 32),
                            VerticalExpand = true,
                        });
                }

                if (playerInfo.PlayerICName != null)
                {
                    if (playerInfo.Observer)
                    {
                        playerInfoText.SetMarkup(
                            Loc.GetString("round-end-summary-window-player-info-if-observer-text",
                                          ("playerOOCName", playerInfo.PlayerOOCName),
                                          ("playerICName", playerInfo.PlayerICName)));
                    }
                    else
                    {
                        //TODO: On Hover display a popup detailing more play info.
                        //For example: their antag goals and if they completed them sucessfully.
                        var icNameColor = playerInfo.Antag ? "red" : "white";
                        playerInfoText.SetMarkup(
                            Loc.GetString("round-end-summary-window-player-info-if-not-observer-text",
                                ("playerOOCName", playerInfo.PlayerOOCName),
                                ("icNameColor", icNameColor),
                                ("playerICName", playerInfo.PlayerICName),
                                ("playerRole", Loc.GetString(playerInfo.Role))));
                    }
                }
                hBox.AddChild(playerInfoText);
                playerInfoContainer.AddChild(hBox);
            }

            playerInfoContainerScrollbox.AddChild(playerInfoContainer);
            playerManifestTab.AddChild(playerInfoContainerScrollbox);

            return playerManifestTab;
        }
    }

}
