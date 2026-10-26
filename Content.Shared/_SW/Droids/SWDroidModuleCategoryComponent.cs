namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidModuleCategoryComponent : Component
{
    [DataField(required: true)]
    public string Category = string.Empty;
}
