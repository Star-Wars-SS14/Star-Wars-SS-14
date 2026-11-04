using System.Linq;
using System.Numerics;
using Content.Client._SW.Loadouts; //SW
using Content.Client._SW.LateJoin; //SW
using Content.Client._SW.GlobalWar.UI; //SW
using Content.Shared._SW.GlobalWar; //SW
using Robust.Shared.Timing; //SW
using Content.Client.CrewManifest;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using System.Collections.Generic;

namespace Content.Client.LateJoin
{
    public sealed class LateJoinGui : DefaultWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystem = default!;
        [Dependency] private readonly JobRequirementsManager _jobRequirements = default!;
        [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        public event Action<(NetEntity, string)> SelectedId;

        private readonly ClientGameTicker _gameTicker;
        private readonly SpriteSystem _sprites;
        private readonly CrewManifestSystem _crewManifest;
        private readonly ISawmill _sawmill;

        private readonly Dictionary<NetEntity, Dictionary<string, List<JobButton>>> _jobButtons = new();
        private readonly Dictionary<NetEntity, Dictionary<string, BoxContainer>> _jobCategories = new();
        private readonly Dictionary<NetEntity, SWLateJoinScrollContainer> _jobLists = new(); //SW

        private readonly Dictionary<(NetEntity Station, string Job), List<Button>> _swGearButtons = new(); //SW
        //SW start
        private const string SWCampaignJobPrefix = "SWGlobalWar";
        private const float SWWindowMargin = 24;
        private const float SWDescriptionHeight = 40;
        private static readonly TimeSpan SWJoinAnswerTimeout = TimeSpan.FromSeconds(6);
        private readonly SWGlobalWarEntrySystem _swEntry;
        private readonly Content.Client._SW.Lobby.SWLobbySideLine _swLine = new() { Visible = false };
        private readonly RichTextLabel _swNotice = new() { Visible = false };
        private readonly RichTextLabel _swStatus = new() { Visible = false };
        private readonly BoxContainer _swSides = new() { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8, Visible = false };
        private bool _swCampaign;
        private TimeSpan _swPendingFor;
        private bool _swPending;
        //SW end

        private readonly Control _base;

        public LateJoinGui()
        {
            //SW start
            var size = new Vector2(640, 560);
            var viewport = UserInterfaceManager.WindowRoot.Size;
            if (viewport.X > 0 && viewport.Y > 0)
                size = Vector2.Min(size, viewport - new Vector2(SWWindowMargin));
            MinSize = SetSize = size;
            //SW end
            IoCManager.InjectDependencies(this);
            _sprites = _entitySystem.GetEntitySystem<SpriteSystem>();
            _crewManifest = _entitySystem.GetEntitySystem<CrewManifestSystem>();
            _gameTicker = _entitySystem.GetEntitySystem<ClientGameTicker>();
            _sawmill = _logManager.GetSawmill("latejoin.panel");

            Title = Loc.GetString("late-join-gui-title");

            _base = new BoxContainer()
            {
                Orientation = LayoutOrientation.Vertical,
                VerticalExpand = true,
            };

            //SW start
            // Campaign entry reports the server's answer here instead of closing on the request.
            _swEntry = _entitySystem.GetEntitySystem<SWGlobalWarEntrySystem>();
            _swEntry.JoinResult += SWOnJoinResult;
            ContentsContainer.AddChild(new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                VerticalExpand = true,
                Children = { _swLine, _swNotice, _swSides, _base, _swStatus },
            });
            //SW end

            _jobRequirements.Updated += RebuildUI;
            RebuildUI();

            SelectedId += x =>
            {
                var (station, jobId) = x;
                _sawmill.Info($"Late joining as ID: {jobId}");
                _consoleHost.ExecuteCommand($"joingame {CommandParsing.Escape(jobId)} {station}");
                //SW start
                if (jobId.StartsWith(SWCampaignJobPrefix))
                {
                    SWBeginWaiting();
                    return;
                }
                //SW end
                Close();
            };

            _gameTicker.LobbyJobsAvailableUpdated += JobsAvailableUpdated;
        }

        //SW start
        private void SWBeginWaiting()
        {
            _swPending = true;
            _swPendingFor = TimeSpan.Zero;
            SWSetStatus(Loc.GetString("sw-global-war-latejoin-pending"));
            _base.Visible = false;
            _swSides.Visible = false;
        }

        private void SWEndWaiting(string message)
        {
            _swPending = false;
            _base.Visible = true;
            _swSides.Visible = _swCampaign;
            SWSetStatus(message);
        }

        private void SWSetStatus(string message)
        {
            _swStatus.SetMessage(message);
            _swStatus.Visible = true;
        }

        private void SWOnJoinResult(SWGlobalWarJoinResultEvent result)
        {
            if (result.Success)
            {
                _swPending = false;
                Close();
                return;
            }
            // The selection and the window stay: a refusal must be readable and answerable from here.
            SWEndWaiting(result.Message);
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (!_swPending)
                return;
            _swPendingFor += TimeSpan.FromSeconds(args.DeltaSeconds);
            if (_swPendingFor >= SWJoinAnswerTimeout)
                SWEndWaiting(Loc.GetString("sw-global-war-spawn-no-answer"));
        }

        /// <summary>Both sides are named equally at the top, so nobody misses the one whose list is not showing.</summary>
        private void SWBuildSideTabs()
        {
            _swSides.DisposeAllChildren();
            _swSides.Visible = _swCampaign && !_swPending;
            if (!_swCampaign)
                return;
            var group = new ButtonGroup();
            foreach (var (id, list) in _jobLists)
            {
                if (_jobButtons[id].Count == 0)
                    continue;
                var side = new SWGlobalWarConsoleButton
                {
                    Text = _gameTicker.StationNames[id],
                    HorizontalExpand = true,
                    ToggleMode = true,
                    Group = group,
                    Pressed = list.Visible,
                    MinHeight = 36,
                    Accent = _gameTicker.JobsAvailable[id].Keys.Any(job => job.Id.Contains("Empire")) ? "Empire" : "Republic",
                };
                side.SetAppearance(SWGlobalWarConsoleStyle.TextBright);
                side.OnPressed += _ =>
                {
                    foreach (var section in _jobLists.Values)
                        section.Visible = false;
                    list.Visible = true;
                };
                _swSides.AddChild(side);
            }
        }

        private void SWUpdateNotice(bool campaign)
        {
            _swCampaign = campaign;
            _swNotice.Visible = campaign;
            _swLine.Visible = campaign;
            Title = Loc.GetString(campaign ? "sw-global-war-latejoin-title" : "late-join-gui-title");
            if (!campaign)
                return;
            var profile = (HumanoidCharacterProfile?) _preferencesManager.Preferences?.SelectedCharacter;
            _swNotice.SetMessage(Loc.GetString(profile?.PreferenceUnavailable == PreferenceUnavailableMode.SpawnAsOverflow
                ? "sw-global-war-latejoin-notice-overflow"
                : "sw-global-war-latejoin-notice-stay"));
        }

        /// <summary>Availability in words and the role's function, because grey and a zero explain nothing.</summary>
        private void SWDescribeCampaignJob(JobPrototype job, Label label, BoxContainer selector, int? amount)
        {
            label.Text = amount switch
            {
                null => Loc.GetString("sw-global-war-latejoin-role-open", ("jobName", job.LocalizedName)),
                0 => Loc.GetString("sw-global-war-latejoin-role-full", ("jobName", job.LocalizedName)),
                _ => Loc.GetString("sw-global-war-latejoin-role-free", ("jobName", job.LocalizedName), ("amount", amount)),
            };
            if (job.LocalizedDescription is not { } description)
                return;
            // Up to two lines: the whole text is in the tooltip.
            var function = new RichTextLabel
            {
                HorizontalExpand = true,
                MaxHeight = SWDescriptionHeight,
                RectClipContent = true,
                MouseFilter = MouseFilterMode.Ignore,
                Margin = new Thickness(5f, 0, 0, 0),
            };
            function.SetMessage(FormattedMessage.FromUnformatted(description), defaultColor: SWGlobalWarConsoleStyle.TextDim);
            selector.RemoveChild(label);
            selector.AddChild(new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                Children = { label, function },
            });
            selector.ToolTip = description;
        }
        //SW end

        private void RebuildUI()
        {
            //SW start
            var selectedStation = _jobLists.Where(p => p.Value.Visible).Select(p => (NetEntity?) p.Key).FirstOrDefault();
            var scrollPositions = _jobLists.ToDictionary(p => p.Key, p => p.Value.SavedScroll);
            _base.DisposeAllChildren();
            //SW end
            _jobLists.Clear();
            _jobButtons.Clear();
            _jobCategories.Clear();
            _swGearButtons.Clear(); //SW
            SWUpdateNotice(_gameTicker.JobsAvailable.Values.Any(jobs => jobs.Keys.Any(job => job.Id.StartsWith(SWCampaignJobPrefix)))); //SW

            if (!_gameTicker.DisallowedLateJoin && _gameTicker.StationNames.Count == 0)
                _sawmill.Warning("No stations exist, nothing to display in late-join GUI");

            foreach (var (id, name) in _gameTicker.StationNames)
            {
                var jobList = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Margin = new Thickness(0, 0, 5f, 0),
                };

                var collapseButton = new ContainerButton()
                {
                    HorizontalAlignment = HAlignment.Right,
                    ToggleMode = true,
                    Children =
                    {
                        new TextureRect
                        {
                            StyleClasses = { OptionButton.StyleClassOptionTriangle },
                            Margin = new Thickness(8, 0),
                            HorizontalAlignment = HAlignment.Center,
                            VerticalAlignment = VAlignment.Center,
                        }
                    }
                };

                var stripe = new StripeBack() //SW
                {
                    Children =
                    {
                        new PanelContainer()
                        {
                            Children =
                            {
                                new Label()
                                {
                                    StyleClasses = { "LabelBig" },
                                    Text = name,
                                    Align = Label.AlignMode.Center,
                                },
                                collapseButton
                            }
                        }
                    }
                };
                _base.AddChild(stripe);
                //SW start
                var crewManifestButton = new Button()
                {
                    Text = Loc.GetString("crew-manifest-button-label")
                };
                //SW end
                if (_configManager.GetCVar(CCVars.CrewManifestWithoutEntity))
                {
                    crewManifestButton.OnPressed += _ => _crewManifest.RequestCrewManifest(id);

                    _base.AddChild(crewManifestButton);
                }

                var jobListScroll = new SWLateJoinScrollContainer() //SW
                {
                    VerticalExpand = true,
                    HScrollEnabled = false, //SW: so that a post's description wraps to its second line
                    Children = { jobList },
                    Visible = false,
                };

                //SW start
                if (scrollPositions.TryGetValue(id, out var scroll))
                    jobListScroll.RestoreScroll(scroll);
                _jobLists.Add(id, jobListScroll);
                //SW end

                _base.AddChild(jobListScroll);

                collapseButton.OnToggled += _ =>
                {
                    foreach (var section in _jobLists.Values) //SW
                    {
                        section.Visible = false;
                    }
                    jobListScroll.Visible = true;
                };

                var firstCategory = true;
                var departments = _prototypeManager.EnumeratePrototypes<DepartmentPrototype>().ToArray();
                Array.Sort(departments, DepartmentUIComparer.Instance);

                _jobButtons[id] = new Dictionary<string, List<JobButton>>();

                foreach (var department in departments)
                {
                    var departmentName = Loc.GetString(department.Name);
                    _jobCategories[id] = new Dictionary<string, BoxContainer>();
                    var stationAvailable = _gameTicker.JobsAvailable[id];
                    var jobsAvailable = new List<JobPrototype>();

                    foreach (var jobId in department.Roles)
                    {
                        if (!stationAvailable.ContainsKey(jobId))
                            continue;

                        jobsAvailable.Add(_prototypeManager.Index<JobPrototype>(jobId));
                    }

                    jobsAvailable.Sort(JobUIComparer.Instance);

                    // Do not display departments with no jobs available.
                    if (jobsAvailable.Count == 0)
                        continue;

                    var category = new BoxContainer
                    {
                        Orientation = LayoutOrientation.Vertical,
                        Name = department.ID,
                        ToolTip = Loc.GetString("late-join-gui-jobs-amount-in-department-tooltip",
                            ("departmentName", departmentName))
                    };

                    if (firstCategory)
                    {
                        firstCategory = false;
                    }
                    else
                    {
                        category.AddChild(new Control
                        {
                            MinSize = new Vector2(0, 23),
                        });
                    }

                    category.AddChild(new PanelContainer
                    {
                        Children =
                        {
                            new Label
                            {
                                StyleClasses = { "LabelBig" },
                                Text = Loc.GetString("late-join-gui-department-jobs-label", ("departmentName", departmentName))
                            }
                        }
                    });

                    _jobCategories[id][department.ID] = category;
                    jobList.AddChild(category);
                    foreach (var prototype in jobsAvailable)
                    {
                        var value = stationAvailable[prototype.ID];

                        var jobLabel = new Label
                        {
                            Margin = new Thickness(5f, 0, 0, 0)
                        };

                        var jobButton = new JobButton(jobLabel, prototype.ID, prototype.LocalizedName, value);

                        var jobSelector = new BoxContainer
                        {
                            Orientation = LayoutOrientation.Horizontal,
                            HorizontalExpand = true
                        };

                        var icon = new TextureRect
                        {
                            TextureScale = new Vector2(2, 2),
                            VerticalAlignment = VAlignment.Center
                        };

                        var jobIcon = _prototypeManager.Index(prototype.Icon);
                        icon.Texture = _sprites.Frame0(jobIcon.Icon);
                        jobSelector.AddChild(icon);

                        jobSelector.AddChild(jobLabel);
                        jobButton.AddChild(jobSelector);
                        //SW start
                        if (prototype.ID.StartsWith(SWCampaignJobPrefix))
                            SWDescribeCampaignJob(prototype, jobLabel, jobSelector, value);
                        // Gear can be changed from here, without leaving the join window for the character editor.
                        var swLoadouts = UserInterfaceManager.GetUIController<SWLoadoutUIController>();
                        // Its own button beside the job's, with a gap: a click on the job's button spawns at once.
                        var swRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
                        jobButton.HorizontalExpand = true;
                        swRow.AddChild(jobButton);
                        if (swLoadouts.CanOpen(prototype))
                        {
                            var swGear = new Button();
                            var gearKey = (id, prototype.ID);
                            if (!_swGearButtons.TryGetValue(gearKey, out var gearButtons))
                                _swGearButtons.Add(gearKey, gearButtons = new());
                            gearButtons.Add(swGear);
                            void SWUpdateGear()
                            {
                                // An open loadout editor can outlive a rebuilt row; update its current replacements.
                                if (Disposed || !_swGearButtons.TryGetValue(gearKey, out var currentButtons))
                                    return;
                                var text = swLoadouts.HasChoice(prototype)
                                    ? Loc.GetString("sw-loadout-tab-changed", ("name", Loc.GetString("sw-loadout-latejoin-button")))
                                    : Loc.GetString("sw-loadout-latejoin-button");
                                foreach (var button in currentButtons)
                                    button.Text = text;
                            }
                            SWUpdateGear();
                            swGear.OnPressed += _ => swLoadouts.Open(prototype, SWUpdateGear);
                            swRow.AddChild(swGear);
                        }

                        category.AddChild(swRow);
                        //SW end

                        jobButton.OnPressed += _ => SelectedId.Invoke((id, jobButton.JobId));

                        if (!_jobRequirements.IsAllowed(prototype, (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter, out var reason))
                        {
                            jobButton.Disabled = true;

                            if (!reason.IsEmpty)
                            {
                                var tooltip = new Tooltip();
                                tooltip.SetMessage(reason);
                                jobButton.TooltipSupplier = _ => tooltip;
                            }

                            jobSelector.AddChild(new TextureRect
                            {
                                TextureScale = new Vector2(0.4f, 0.4f),
                                Stretch = TextureRect.StretchMode.KeepCentered,
                                Texture = _sprites.Frame0(new SpriteSpecifier.Texture(new ("/Textures/Interface/Nano/lock.svg.192dpi.png"))),
                                HorizontalExpand = true,
                                HorizontalAlignment = HAlignment.Right,
                            });
                        }
                        else if (value == 0)
                        {
                            jobButton.Disabled = true;
                        }

                        if (!_jobButtons[id].ContainsKey(prototype.ID))
                        {
                            _jobButtons[id][prototype.ID] = new List<JobButton>();
                        }

                        _jobButtons[id][prototype.ID].Add(jobButton);
                    }
                }
                //SW start
                if (_jobButtons[id].Count == 0 || _swCampaign)
                {
                    stripe.Visible = false;
                    crewManifestButton.Visible = false;
                }
                //SW end
            }
            //SW start
            if (selectedStation is not { } selected || !_jobButtons.TryGetValue(selected, out var selectedJobs) || selectedJobs.Count == 0)
                selectedStation = _jobLists.Keys.Where(id => _jobButtons[id].Count > 0).Select(id => (NetEntity?) id).FirstOrDefault();
            if (selectedStation is { } visible)
                _jobLists[visible].Visible = true;
            SWBuildSideTabs();
            //SW end
        }

        private void JobsAvailableUpdated(IReadOnlyDictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>> updatedJobs)
        {
            RebuildUI(); //SW: the event is a full snapshot, including removals and renewed role restrictions.
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _jobRequirements.Updated -= RebuildUI;
                _gameTicker.LobbyJobsAvailableUpdated -= JobsAvailableUpdated;
                _jobButtons.Clear();
                _jobCategories.Clear();
                _swGearButtons.Clear(); //SW
                _swEntry.JoinResult -= SWOnJoinResult; //SW
            }
        }
    }

    sealed class JobButton : ContainerButton
    {
        public Label JobLabel { get; }
        public string JobId { get; }
        public string JobLocalisedName { get; }
        public int? Amount { get; private set; }
        private bool _initialised = false;

        public JobButton(Label jobLabel, ProtoId<JobPrototype> jobId, string jobLocalisedName, int? amount)
        {
            JobLabel = jobLabel;
            JobId = jobId;
            JobLocalisedName = jobLocalisedName;
            RefreshLabel(amount);
            AddStyleClass(StyleClassButton);
            _initialised = true;
        }

        public void RefreshLabel(int? amount)
        {
            if (Amount == amount && _initialised)
            {
                return;
            }
            Amount = amount;

            JobLabel.Text = Amount != null ?
                Loc.GetString("late-join-gui-job-slot-capped", ("jobName", JobLocalisedName), ("amount", Amount)) :
                Loc.GetString("late-join-gui-job-slot-uncapped", ("jobName", JobLocalisedName));
        }
    }
}
