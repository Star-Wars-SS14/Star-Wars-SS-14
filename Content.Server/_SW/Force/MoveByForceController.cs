//using Content.Server.Database;
//using Content.Shared._SW;
//using Content.Shared.Input;
//using Content.Shared.Movement.Pulling.Systems;
//using Robust.Server.GameObjects;
//using Robust.Shared.GameObjects;
//using Robust.Shared.Input.Binding;
//using Robust.Shared.Map;
//using Robust.Shared.Physics.Controllers;
//using Robust.Shared.Player;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Numerics;
//using System.Text;
//using System.Threading.Tasks;

//namespace Content.Server._SW.Force;

//public sealed partial class MoveByForceController : VirtualController
//{
//    [Dependency] SharedTransformSystem _transformSystem = default!;
//    [Dependency] PhysicsSystem _physicsSystem = default!;
//    public override void Initialize()
//    {
//        base.Initialize();
//        CommandBinds.Builder
//            .Bind(ContentKeyFunctions.MovePulledObject, new PointerInputCmdHandler(OnRequestMovePulledObject))
//            .Register<MoveByForceController>();
//    }
//    private EntityQuery<BeingMovedByForceComponent> _query;
//    private bool OnRequestMovePulledObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
//    {
//        if (session?.AttachedEntity is not { } player ||
//           !player.IsValid())
//        {
//            return false;
//        }
        
//        _query = GetEntityQuery<BeingMovedByForceComponent>();
        
//        var fromUserCoords = _transformSystem.WithEntityId(coords, player);
//        var userCoords = new EntityCoordinates(player, Vector2.Zero);
//        var direction = (fromUserCoords.Position - userCoords.Position).Normalized();
//        PhysicsSystem.ApplyLinearImpulse(uid, direction);

//        return false;
//    }
//}
