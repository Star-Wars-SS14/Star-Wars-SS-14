using Content.Shared.Camera;

namespace Content.Shared._SW._Senate.Chamber;

public sealed class SWSenateChamberViewSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateChamberViewComponent, GetEyeOffsetEvent>(OnGetOffset);
        SubscribeLocalEvent<SWSenateChamberViewComponent, GetEyePvsScaleEvent>(OnGetPvsScale);
    }

    private void OnGetOffset(Entity<SWSenateChamberViewComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (ent.Comp.Applied)
            args.Offset += ent.Comp.Offset;
    }

    private void OnGetPvsScale(Entity<SWSenateChamberViewComponent> ent, ref GetEyePvsScaleEvent args)
    {
        if (ent.Comp.Applied)
            args.Scale += MathF.Max(0f, ent.Comp.PvsScale - 1f);
    }
}
