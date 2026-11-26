using System;
using System.Linq;
using System.Collections.Generic;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.Client._SW._Senate;

/// <summary>Presentation of a published breakdown; the law editor continues to use the base value.</summary>
public static class SWSituationalStatDisplay
{
    public static Control Make(SWSituationalStatView stat, HashSet<string> expanded)
    {
        var panel = SWConsoleStyle.Row(SWConsoleStyle.BgPanel);
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        box.AddChild(SWConsoleStyle.Text(stat.SubstatName, SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        var summary = SWConsoleStyle.Wrap(Loc.GetString("sw-situation-summary",
            ("base", stat.BaseValue.ToString("0.#")), ("applied", Signed(stat.AppliedTotal)), ("effective", stat.EffectiveValue.ToString("0.#"))), SWConsoleStyle.TextBright);
        summary.Name = "SituationalSummary";
        var sources = stat.Sources.Where(s => !string.IsNullOrWhiteSpace(s.SourceName)).ToArray();
        var grouped = sources.GroupBy(s => s.SourceName).Select(g => Loc.GetString("sw-situation-group",
            ("name", g.Key), ("count", g.Count()), ("value", Signed(g.Sum(x => (double) x.RawValue))))).ToArray();
        var explanation = sources.Length == 0 ? Loc.GetString("sw-situation-none") : string.Join("\n", grouped);
        if (stat.SectorId == null) explanation = Loc.GetString("sw-situation-republic-average") + "\n" + explanation;
        if (Math.Abs(stat.RawTotal - stat.AppliedTotal) > 0.001)
            explanation += "\n" + Loc.GetString(stat.SectorId == null ? "sw-situation-average-limited" : "sw-situation-limited",
                ("raw", Signed(stat.RawTotal)), ("applied", Signed(stat.AppliedTotal)));
        summary.ToolTip = explanation;
        panel.ToolTip = explanation;
        box.AddChild(summary);
        if (sources.Length > 0)
        {
            var key = $"{stat.SectorId ?? "republic"}/{stat.SubstatId}";
            var details = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Visible = expanded.Contains(key) };
            foreach (var source in sources)
            {
                var name = stat.SectorId == null && !string.IsNullOrWhiteSpace(source.SectorName)
                    ? Loc.GetString("sw-situation-source-sector", ("source", source.SourceName), ("sector", source.SectorName))
                    : source.SourceName;
                details.AddChild(SWConsoleStyle.Wrap(Loc.GetString("sw-situation-source", ("name", name), ("value", Signed(source.RawValue))), SWConsoleStyle.TextDim));
            }
            var button = SWConsoleStyle.TabButton(Loc.GetString(details.Visible ? "sw-situation-hide-sources" : "sw-situation-show-sources"));
            button.OnPressed += _ =>
            {
                details.Visible = !details.Visible;
                if (details.Visible) expanded.Add(key); else expanded.Remove(key);
                button.Text = Loc.GetString(details.Visible ? "sw-situation-hide-sources" : "sw-situation-show-sources");
            };
            box.AddChild(button);
            box.AddChild(details);
        }
        panel.AddChild(box);
        return panel;
    }

    private static string Signed(double value) => value.ToString("+0.#;-0.#;0");
}
