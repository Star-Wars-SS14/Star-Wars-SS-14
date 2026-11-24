using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Tutorial;

/// <summary>
/// A friend in the tutorial: wears the badge the student has to learn to read, and takes no damage from the student.
/// </summary>
[RegisterComponent]
public sealed partial class SWTutorialAllyComponent : Component
{
    /// <summary>
    /// The badge over the head.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<JobIconPrototype> Icon;
}
