using Content.Client.Pinpointer.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._SW.Droids.UI;

public sealed partial class SWDroidControlDatapadNavMapControl : NavMapControl
{
    public NetEntity? Focus;
    public Dictionary<NetEntity, string> LocalizedNames = new();

    private readonly Label _trackedEntityLabel;
    private readonly PanelContainer _trackedEntityPanel;

    public SWDroidControlDatapadNavMapControl()
    {
        WallColor = new Color(115, 177, 210);
        TileColor = new Color(36, 55, 68);
        BackgroundColor = Color.FromSrgb(TileColor.WithAlpha(BackgroundOpacity));

        _trackedEntityLabel = new Label
        {
            Margin = new Thickness(10f, 8f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Modulate = Color.White,
        };

        _trackedEntityPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = BackgroundColor,
            },
            Margin = new Thickness(5f, 10f),
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Bottom,
            Visible = false,
        };

        _trackedEntityPanel.AddChild(_trackedEntityLabel);
        AddChild(_trackedEntityPanel);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (Focus == null)
        {
            _trackedEntityLabel.Text = string.Empty;
            _trackedEntityPanel.Visible = false;
            return;
        }

        foreach (var (netEntity, blip) in TrackedEntities)
        {
            if (netEntity != Focus)
                continue;

            if (!LocalizedNames.TryGetValue(netEntity, out var name))
                name = Loc.GetString("navmap-unknown-entity");

            _trackedEntityLabel.Text = name + "\n" + Loc.GetString("navmap-location",
                ("x", MathF.Round(blip.Coordinates.X)),
                ("y", MathF.Round(blip.Coordinates.Y)));
            _trackedEntityPanel.Visible = true;
            return;
        }

        _trackedEntityLabel.Text = string.Empty;
        _trackedEntityPanel.Visible = false;
    }
}
