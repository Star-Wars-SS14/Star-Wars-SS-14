using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>A persistent medical atlas. Selection does not execute any ministry action.</summary>
public sealed class SWHealthMapPanel : BoxContainer
{
    private readonly SWRepublicMapBrowser _browser = new();
    private readonly RichTextLabel _status = SWConsoleStyle.Wrap("Выберите мир", SWConsoleStyle.TextBright);
    private SWHealthView? _health;
    public event Action<string>? OnWorldSelected;
    public SWHealthMapPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        MinHeight = 400;
        AddChild(_browser);
        AddChild(_status);
        _browser.SetMedicalSupplyVisible(true);
        _browser.SelectionChanged += () =>
        {
            RefreshSelection();
            if (_browser.SelectedWorldId is { } id) OnWorldSelected?.Invoke(id);
        };
    }
    public void SetState(SWHealthView health, SWEconomyView? economy, IReadOnlyList<SWMapCrisisEntry>? crises = null)
    {
        _health = health;
        _browser.SetEconomy(economy);
        _browser.SetCrises(crises ?? Array.Empty<SWMapCrisisEntry>());
        RefreshSelection();
    }
    private void RefreshSelection()
    {
        var w = _health?.Worlds.FirstOrDefault(w => w.Id == _browser.SelectedWorldId);
        _status.SetMessage(w == null ? "Выберите мир для медицинских действий" :
            $"{w.Name}: заражение {w.Infection:0}%, иммунитет {w.Immunity:0}%, клиники {w.ClinicCoverage:0}%; " +
            $"запас помощи {SWSenateDuration.FormatHours(w.ReserveHours)}");
    }
}



