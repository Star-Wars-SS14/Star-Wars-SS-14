using System.Linq;
using Content.Client.Lobby;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._SW.TaskSystem;
using Content.Shared._SW.UserInterface;
using Content.Shared.CCVar;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.Themes;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// Puts the SW rules after the Nanotrasen ones, and follows the <c>sw.ui_terminal</c> cvar without a restart.
/// </summary>
public sealed class SWTerminalStyleUIController : UIController
{
    private const string TerminalTheme = "SWTerminalTheme";

    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceCache _res = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly IStylesheetManager _sheets = default!;

    private readonly Dictionary<string, Stylesheet> _terminal = new();
    private Stylesheet? _plain;
    private bool _themeApplied;
    private string _playerAccent = SWTerminalStyle.NeutralAccent;

    public override void Initialize()
    {
        base.Initialize();

        // Initialize runs before the sheets exist, and the first state starts after them, before its first frame.
        _state.OnStateChanged += OnStateChanged;
        _cfg.OnValueChanged(SWVars.UITerminal, _ => Apply());
        _cfg.OnValueChanged(SWVars.UIPlayerAccent, _ => Apply());
    }

    /// <summary>
    /// Gives a console's window the accent of the console's faction, whoever opened it.
    /// </summary>
    public void ApplyOwnerAccent(Control window, EntityUid owner)
    {
        if (!_cfg.GetCVar(SWVars.UITerminal))
            return;

        if (!EntityManager.TryGetComponent<SWConsoleFactionComponent>(owner, out var console))
            return;

        // Some windows hide this property with one of their own, so go through the base type.
        window.Stylesheet = GetTerminal(SWTerminalStyle.AccentOf(console.Faction));

        // Some accents look alike with colour blindness, so the owner is also named.
        if (GetFactionName(console.Faction) is not { } name)
            return;

        if (window is DefaultWindow defaultWindow)
            defaultWindow.Title = Loc.GetString("sw-terminal-owner-title", ("title", defaultWindow.Title ?? string.Empty), ("faction", name));
        else if (window is FancyWindow fancyWindow)
            fancyWindow.Title = Loc.GetString("sw-terminal-owner-title", ("title", fancyWindow.Title ?? string.Empty), ("faction", name));
    }

    /// <summary>
    /// The name players know a faction by, or null if the ID is not a faction.
    /// </summary>
    public string? GetFactionName(string? faction)
    {
        if (faction == null
            || !_proto.TryIndex<EntityPrototype>(faction, out var proto)
            || !proto.TryGetComponent<SWFactionComponent>(out var comp, _factory))
        {
            return null;
        }

        return comp.Name;
    }

    /// <summary>
    /// Remembers the local player's accent. It only shows with <c>sw.ui_player_accent</c> on.
    /// </summary>
    public void SetPlayerAccent(string accent)
    {
        _playerAccent = accent;
        Apply();
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        // Menus and the lobby have no faction.
        if (args.NewState is LobbyState)
            _playerAccent = SWTerminalStyle.NeutralAccent;

        Apply();
    }

    private void Apply()
    {
        if (_sheets.SheetNanotrasen == null)
            return;

        var wanted = _cfg.GetCVar(SWVars.UITerminal)
            ? GetTerminal(_cfg.GetCVar(SWVars.UIPlayerAccent) ? _playerAccent : SWTerminalStyle.NeutralAccent)
            : _plain ??= Build(Array.Empty<StyleRule>());

        if (!ReferenceEquals(UIManager.Stylesheet, wanted))
            UIManager.Stylesheet = wanted;

        ApplyTheme();
    }

    /// <summary>
    /// The terminal HUD is the default, not a rule: a player who picked a theme in the options keeps it.
    /// </summary>
    private void ApplyTheme()
    {
        var chosen = _cfg.GetCVar(CVars.InterfaceTheme);
        var terminal = _cfg.GetCVar(SWVars.UITerminal);
        if (chosen.Length > 0 || _themeApplied == terminal)
            return;

        _themeApplied = terminal;
        UIManager.SetActiveTheme(terminal ? TerminalTheme : UITheme.DefaultName);
    }

    private Stylesheet GetTerminal(string accent)
    {
        if (_terminal.TryGetValue(accent, out var sheet))
            return sheet;

        var global = SWTerminalGlobalRules.GetRules(_res).Concat(SWTerminalGlobalRules.GetAccentRules(accent));
        sheet = Build(global);
        _terminal[accent] = sheet;
        return sheet;
    }

    /// <summary>
    /// Our own windows use classes the base sheet lacks, so they keep their rules with the look turned off.
    /// They tie with the global rules, so they go last.
    /// </summary>
    private Stylesheet Build(IEnumerable<StyleRule> rules)
    {
        return new Stylesheet(_sheets.SheetNanotrasen.Rules.Concat(rules).Concat(SWTerminalStyle.GetRules(_res)).ToArray());
    }
}
