using Content.Shared._SW;
using Content.Shared.Communications;
using Robust.Shared.Audio;

namespace Content.Server._SW.Artillery
{
    [RegisterComponent]
    public sealed partial class ArtilleryConsoleComponent : SharedCommunicationsConsoleComponent
    {
        public float UIUpdateAccumulator = 0f;

        [ViewVariables]
        [DataField("artileryCalledProto")]
        public string? ArtilleryProto = default!;
    }
}
