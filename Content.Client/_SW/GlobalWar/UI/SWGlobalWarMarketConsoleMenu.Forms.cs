using System.Globalization;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// The look of the flow and growth forms: cards for "what, how much, by what right", the save button in the side's colour,
/// and the worker's assignment folded away until it is needed.
/// </summary>
public sealed partial class SWGlobalWarMarketConsoleMenu
{
    private const int GrowthStep = 5;
    private const int GrowthLimit = 25;

    private void InitializeForms()
    {
        foreach (var card in new[] { FlowRouteCard, FlowRateCard, FlowRightCard, GrowthWorldCard, GrowthValueCard })
            card.PanelOverride = SWGlobalWarConsoleStyle.RowBox(false);
        foreach (var title in new[] { FlowRouteTitle, FlowRateTitle, FlowRightTitle, GrowthWorldTitle, GrowthValueTitle })
        {
            title.FontOverride = SWGlobalWarConsoleStyle.Strong;
            title.FontColorOverride = SWGlobalWarConsoleStyle.TextBright;
        }
        foreach (var label in new[] { FlowSourceLabel, FlowDestinationLabel, FlowCommodityLabel, FlowRateLabel, FlowOrderLabel, FlowGrantLabel, FlowArrow })
        {
            label.FontOverride = SWGlobalWarConsoleStyle.Small;
            label.FontColorOverride = SWGlobalWarConsoleStyle.TextDim;
        }

        SaveFlow.SetAppearance(Color.White, primary: true);
        GrowthSave.SetAppearance(Color.White, primary: true);
        FlowDelegationToggle.OnPressed += _ => Fold(FlowDelegationToggle, FlowDelegationBody);
        GrowthDelegationToggle.OnPressed += _ => Fold(GrowthDelegationToggle, GrowthDelegationBody);
        GrowthLess.OnPressed += _ => StepGrowth(-GrowthStep);
        GrowthMore.OnPressed += _ => StepGrowth(GrowthStep);
    }

    private static void Fold(SWGlobalWarConsoleButton toggle, Control body)
    {
        body.Visible = !body.Visible;
        toggle.Text = Loc.GetString(body.Visible ? "sw-global-war-market-form-delegate-open" : "sw-global-war-market-form-delegate");
    }

    /// <summary>Moves the growth share by a step and lets the field's own handler check and redraw it.</summary>
    private void StepGrowth(int step)
    {
        var current = int.TryParse(GrowthPercent.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        GrowthPercent.SetText(Math.Clamp(current + step, 0, GrowthLimit).ToString(CultureInfo.InvariantCulture), invokeEvent: true);
    }
}
