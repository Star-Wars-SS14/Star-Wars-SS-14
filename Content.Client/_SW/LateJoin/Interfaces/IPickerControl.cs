using Content.Shared.GameTicking;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.LateJoin.Interfaces;

public abstract class PickerControl: PanelContainer
{
    public abstract void UpdateUi(IReadOnlyDictionary<NetEntity, StationJobInformation> obj);
}
