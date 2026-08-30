using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed class PlayerPresentation : MonoBehaviour
    {
        [SerializeField] private Transform aimIndicator;
        public void Present(PlayerState state)
        {
            aimIndicator.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(state.AimDirection.Y, state.AimDirection.X) * Mathf.Rad2Deg);
        }
    }
}
