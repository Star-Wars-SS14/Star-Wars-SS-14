using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Pinpointer;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Content.Client._SW.ForceUsers;

public sealed partial class SWStrangulateOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public bool Enabled = false;
    private readonly ShaderInstance _oxygenShader;
    public SWStrangulateOverlay()
    {
        IoCManager.InjectDependencies(this);
        _oxygenShader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
    }
    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return;

        if (args.Viewport.Eye != eyeComp.Eye)
            return;

        if (!Enabled)
        {
            return;
        }

        var viewport = args.WorldAABB;
        var handle = args.WorldHandle;
        _oxygenShader.SetParameter("time", 0.0f);
        _oxygenShader.SetParameter("color", new Vector3(0f, 0f, 0f));
        _oxygenShader.SetParameter("darknessAlphaOuter", 2f);
        _oxygenShader.SetParameter("innerCircleRadius", 30f);
        _oxygenShader.SetParameter("innerCircleMaxRadius", 30f);
        _oxygenShader.SetParameter("outerCircleRadius", 700f);
        _oxygenShader.SetParameter("outerCircleMaxRadius", 950f);
        handle.UseShader(_oxygenShader);
        handle.DrawRect(viewport, Color.White);
        handle.UseShader(null);
    }
}
