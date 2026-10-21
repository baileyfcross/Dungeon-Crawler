using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed class TargetPresentation : MonoBehaviour
    {
        [SerializeField] private Transform healthFill;
        [SerializeField] private SpriteRenderer targetSprite;
        private Vector3 fullScale;
        private Color aliveColor;
        private void Awake() { fullScale = healthFill.localScale; aliveColor = targetSprite.color; }

        public void Present(HealthSimulation health)
            => Present(health.Current, health.Maximum);

        public void Present(int current, int maximum)
        {
            healthFill.localScale = new Vector3(fullScale.x * current / maximum, fullScale.y, fullScale.z);
            targetSprite.color = current > 0 ? aliveColor : new Color(0.25f, 0.29f, 0.32f);
        }
    }
}
