using System.Collections.Generic;
using Content.Shared._SW.Vehicle;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._SW.Vehicle;

public sealed class SWTurretAimSystem : EntitySystem
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, float> _lastSent = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Только ЛОКАЛЬНЫЙ стрелок, не все подряд.
        if (_player.LocalEntity is not { } local)
            return;

        if (!TryComp<SWInVehicleComponent>(local, out var inVehicle))
            return;

        var turretUid = inVehicle.Vehicle;

        if (!TryComp<SWTurretAimComponent>(turretUid, out _))
            return;

        if (!TryComp<TransformComponent>(turretUid, out var turretXform))
            return;

        if (!_input.MouseScreenPosition.IsValid)
            return;

        var mapPos = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mapPos.MapId == MapId.Nullspace)
            return;

        var turretMap = _transform.GetMapCoordinates(turretUid, xform: turretXform);
        if (mapPos.MapId != turretMap.MapId)
            return;

        var angle = (mapPos.Position - turretMap.Position).ToWorldAngle();
        var theta = (float) angle.Theta;

        // Не спамим сеть: шлём только при заметном изменении угла.
        if (_lastSent.TryGetValue(turretUid, out var last) && MathF.Abs(theta - last) < 0.005f)
            return;

        _lastSent[turretUid] = theta;
        RaiseNetworkEvent(new SWTurretAimEvent { Angle = theta });
    }
}