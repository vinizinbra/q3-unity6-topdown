namespace Quantum
{
    using QuantumUser.View.Util;
    using UnityEngine;

    public class AssignCamera : QuantumEntityViewComponent
    {
        public override void OnActivate(Frame frame)
        {
            base.OnActivate(frame);

            if (frame.Has<PlayerLink>(EntityRef) == false)
                return;

            var playerRef = frame.Get<PlayerLink>(EntityRef).Player;
            if (QuantumHelper.IsLocalPlayer(playerRef) == false)
                return;

            var camera = FindFirstObjectByType<FollowCamera>();
            camera.target = transform;
        }
    }
}
