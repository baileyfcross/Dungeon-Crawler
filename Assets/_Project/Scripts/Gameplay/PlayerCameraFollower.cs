using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraFollower : MonoBehaviour
    {
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private float depth = -10;
        private Transform target;
        public Camera GameplayCamera => gameplayCamera;
        public void Bind(Transform controlledPlayer) => target = controlledPlayer;

        public void Follow()
        {
            if (target == null) return;
            transform.SetPositionAndRotation(new Vector3(target.position.x, target.position.y, depth), Quaternion.identity);
        }
    }
}
