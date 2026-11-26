using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private SWHealthResearchPanel? _healthResearch;
    private SWHealthMapPanel? _healthMap;
    private SWHealthNetworkPanel? _healthNetwork;
    private SWHealthQuarantinePanel? _healthQuarantine;
    private string _healthWorld = "";

    private void BuildEpidemic(SWMinistryWorkConsoleState state)
    {
        if (!state.IsHealth) return;
        if (_healthResearch == null)
        {
            _healthResearch = new SWHealthResearchPanel();
            _healthResearch.ActionRequested += _owner.HealthAction;
            EpidemicContent.AddChild(_healthResearch);
        }
        _healthResearch.SetState(state.Health);
    }
    private void BuildOutbreak(SWMinistryWorkConsoleState state)
    {
        if (!state.IsHealth) return;
        if (_healthMap == null)
        {
            _healthMap = new SWHealthMapPanel();
            _healthMap.OnWorldSelected += SelectHealthWorld;
            OutbreakContent.AddChild(_healthMap);
            var care = SWConsoleStyle.Action("Медицинская сеть выбранного мира", SWConsoleStyle.Accent);
            care.OnPressed += _ => ShowTab(HealthProgramsTab);
            OutbreakContent.AddChild(care);
        }
        _healthMap.SetState(state.Health, state.HealthEconomy, state.HealthCrises);
    }
    private void SelectHealthWorld(string id)
    {
        _healthWorld = id;
        if (_state == null) return;
        _healthNetwork?.SetState(_state.Health, id);
        _healthQuarantine?.SetState(_state.Health, id);
    }
    private void BuildHealthPrograms(SWMinistryWorkConsoleState state)
    {
        if (!state.IsHealth) return;
        if (_healthNetwork == null)
        {
            _healthNetwork = new SWHealthNetworkPanel();
            _healthNetwork.ActionRequested += _owner.HealthAction;
            _healthNetwork.ResearchRequested += () => ShowTab(EpidemicTab);
            _healthNetwork.WorldSelected += SelectHealthWorld;
            HealthProgramsContent.AddChild(_healthNetwork);
        }
        _healthNetwork.SetState(state.Health, _healthWorld);
        _healthWorld = _healthNetwork.SelectedWorldId;
        if (_healthQuarantine == null)
        {
            _healthQuarantine = new SWHealthQuarantinePanel();
            _healthQuarantine.ActionRequested += _owner.HealthAction;
            _healthQuarantine.WorldSelected += SelectHealthWorld;
            HealthQuarantinePanel.AddChild(_healthQuarantine);
        }
        _healthQuarantine.SetState(state.Health, _healthWorld);
    }
}







