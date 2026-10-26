namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWStatisticsConsoleMenu
{
    public const int GalaxyTab = 5;
    private SWRepublicMapBrowser _galaxy = default!;
    public string? SelectedGalacticWorldId => _galaxy.SelectedWorldId;
    public SWRepublicMapBrowser GalacticMap => _galaxy;

    private void InitializeGalaxy()
    {
        _galaxy = new SWRepublicMapBrowser(showProductionRecipes: true);
        _galaxy.SelectionChanged += () => _sectorId = _galaxy.SelectedLiveSectorId;
        _galaxy.SectorSummaryRequested += OpenSectorSummary;
        GalaxyPanel.AddChild(_galaxy);
    }

    public void SelectGalaxyWorld(string id, bool focus = false) => _galaxy.SelectWorld(id, focus);

    private void RefreshGalaxy()
    {
        if (_state == null) return;
        _galaxy.SetState(_state.Sectors, _state.Globals);
        _galaxy.SetCrises(_state.MapCrises);
    }

    public void OpenGalacticSector()
    {
        if (_galaxy.SelectedLiveSectorId is { } id) OpenSectorSummary(id);
    }

    private void OpenSectorSummary(string id)
    {
        // A previous ranking search must not redirect a map selection to another sector.
        _sectorSearch.Text = string.Empty;
        SelectSector(id);
        ShowTab(1);
    }
}
