namespace Content.Shared._SW.Tutorial;

/// <summary>
/// A named place on the tutorial map. Scenario steps find everything by these names, so the scenario runs on any map
/// that has them. In Shared only so the client does not need an entry in the ignored components list.
/// </summary>
[RegisterComponent]
public sealed partial class SWTutorialPointComponent : Component
{
    [DataField(required: true)]
    public string Point = string.Empty;
}
