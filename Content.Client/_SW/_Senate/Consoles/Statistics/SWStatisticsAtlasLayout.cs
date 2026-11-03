using System;
using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Atlas layout for statistics: an independent drawer replaces the sidebar at narrow widths.</summary>
public sealed class SWStatisticsAtlasLayout : Container
{
    private const float BreakWidth = 680;
    private const float Gap = 16;
    private const float Duration = .16f;
    private readonly Control _map;
    private readonly PanelContainer _details;
    private readonly Button _toggle;
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private bool _narrow;
    private bool _drawerOpen;
    private float _drawerProgress;
    private float _measuredWidth;

    public SWStatisticsAtlasLayout(Control map, Control details)
    {
        _map = map;
        _map.MinHeight = 0;
        _details = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = SWConsoleStyle.BgDark,
                BorderColor = SWConsoleStyle.Edge,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
            },
        };
        _details.AddChild(details);
        _toggle = SWConsoleStyle.TabButton("Открыть досье");
        _toggle.MaxWidth = 156;
        _toggle.OnPressed += _ => SetDrawer(!_drawerOpen);
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        AddChild(_map);
        AddChild(_details);
        AddChild(_toggle);
    }

    public void ShowDetails()
    {
        if (_narrow) SetDrawer(true);
    }

    public bool CloseDrawer()
    {
        if (!_narrow || !_drawerOpen) return false;
        SetDrawer(false);
        return true;
    }

    private void SetDrawer(bool open)
    {
        if (!_narrow) return;
        _drawerOpen = open;
        _toggle.Text = open ? "Закрыть досье" : "Открыть досье";
        if (open) _details.Visible = true;
        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _drawerProgress = open ? 1 : 0;
            _details.Visible = open;
        }
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree || !_narrow) return;
        var target = _drawerOpen ? 1f : 0f;
        var step = _configuration.GetCVar(CCVars.ReducedMotion) ? 1 : Math.Max(0, args.DeltaSeconds) / Duration;
        var next = _drawerProgress < target ? Math.Min(target, _drawerProgress + step) : Math.Max(target, _drawerProgress - step);
        if (next == _drawerProgress) return;
        _drawerProgress = next;
        if (!_drawerOpen && next <= 0) _details.Visible = false;
        InvalidateArrange();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _measuredWidth = float.IsFinite(availableSize.X) ? Math.Max(0, availableSize.X) : 1000;
        var height = float.IsFinite(availableSize.Y) ? Math.Max(0, availableSize.Y) : 500;
        var narrow = _measuredWidth < BreakWidth;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            _drawerOpen = false;
            _drawerProgress = 0;
            _toggle.Text = "Открыть досье";
        }
        _toggle.Visible = narrow;
        _details.Visible = !narrow || _drawerOpen || _drawerProgress > 0;
        var detailsWidth = narrow ? Math.Min(_measuredWidth * .72f, 410) : Math.Clamp(_measuredWidth * .37f, 300, 410);
        var mapWidth = narrow ? _measuredWidth : Math.Max(0, _measuredWidth - Gap - detailsWidth);
        _map.Measure(new Vector2(mapWidth, height));
        _details.Measure(new Vector2(detailsWidth, height));
        _toggle.Measure(new Vector2(_measuredWidth, height));
        return new Vector2(_measuredWidth, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var width = Math.Min(finalSize.X, _measuredWidth);
        var detailsWidth = _narrow ? Math.Min(width * .72f, 410) : Math.Clamp(width * .37f, 300, 410);
        var mapWidth = _narrow ? width : Math.Max(0, width - Gap - detailsWidth);
        _map.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(mapWidth, finalSize.Y)));
        var eased = _drawerProgress * (2 - _drawerProgress);
        var x = _narrow ? width - detailsWidth * eased : mapWidth + Gap;
        _details.Arrange(UIBox2.FromDimensions(new Vector2(x, 0), new Vector2(detailsWidth, finalSize.Y)));
        if (_narrow)
            _toggle.Arrange(UIBox2.FromDimensions(new Vector2(6, 6), new Vector2(Math.Min(156, Math.Max(0, width - 12)), _toggle.DesiredSize.Y)));
        return finalSize;
    }
}
