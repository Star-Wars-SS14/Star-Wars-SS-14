using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWTurretAimComponent : Component
{
    /// <summary>
    /// Авторитетный угол наведения. Пишет только сервер.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle TargetAngle = Angle.Zero;

    /// <summary>
    /// Доп. поворот стрелка относительно турели.
    /// </summary>
    [DataField]
    public Angle GunnerOffset = Angle.Zero;

    /// <summary>
    /// Скорость доворота к курсору, град/с.
    /// 120 = 180 / 1.5 (базовая турель MouseRotator = 180).
    /// </summary>
    [DataField]
    public float RotationSpeed = 120f;
}