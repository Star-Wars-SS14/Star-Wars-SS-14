using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Work;

public sealed partial class SWSenateWorkConsoleMenu
{
    public SWRepublicMapBrowser? SectorMap { get; private set; }

    private void UpdateMap(SWSenateWorkConsoleState state)
    {
        var chancellor = state.Position == SWSenatePosition.Chancellor;
        var senator = state.Position == SWSenatePosition.Senator && state.SectorId != null;
        NavMap.Visible = state.HasCard && state.IsSenateCard && state.InOffice && (chancellor || senator);
        if (!NavMap.Visible)
        {
            MapTab.RemoveAllChildren();
            SectorMap = null;
            return;
        }
        if (SectorMap == null)
        {
            SectorMap = new SWRepublicMapBrowser();
            MapTab.AddChild(SectorMap);
        }
        NavMap.Text = chancellor ? "Карта галактики" : "Карта сектора";
        SectorMap.SetScope(chancellor ? null : state.SectorId);
        SectorMap.SetSituationalStats(state.SituationalStats);
        SectorMap.SetState(state.MapSectors, state.Globals);
        SectorMap.SetCrises(state.MapCrises);
    }
}
