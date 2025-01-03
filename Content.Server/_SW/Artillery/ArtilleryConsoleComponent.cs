using Content.Shared._SW;
using Content.Shared.Communications;
using Robust.Shared.Audio;

namespace Content.Server._SW.Artillery
{
    [RegisterComponent]
    public sealed partial class ArtilleryConsoleComponent : SharedCommunicationsConsoleComponent
    {
        [ViewVariables]
        [DataField("artileryCalledProto")]
        public string? ArtilleryProto = default!;

        [ViewVariables]
        [DataField("delayBetweenCallsInSecons")]
        public float InitalDelayInSeconds = 600;

        public float CurrentDelay = 0f;
    }
}
