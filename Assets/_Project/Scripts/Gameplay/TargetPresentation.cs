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
        private int previousHealth=-1;
        private bool alive;
        private float flashUntil;
        private void Awake() { fullScale = healthFill.localScale; aliveColor = targetSprite.color; }

        public void Present(HealthSimulation health)
            => Present(health.Current, health.Maximum);

        public void Present(int current, int maximum)
        {
            if(previousHealth>=0 && current<previousHealth && current>0)flashUntil=Time.unscaledTime+.16f;
            previousHealth=current;alive=current>0;
            healthFill.localScale = new Vector3(fullScale.x * current / maximum, fullScale.y, fullScale.z);
            RefreshColor();
        }
        private void LateUpdate()=>RefreshColor();
        private void RefreshColor()
        { targetSprite.color = alive ? Time.unscaledTime<flashUntil ? Color.white : aliveColor : new Color(0.25f, 0.29f, 0.32f); }
    }
}
