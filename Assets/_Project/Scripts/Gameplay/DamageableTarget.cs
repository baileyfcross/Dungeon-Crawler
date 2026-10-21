using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Authoritative adapter for a primitive test target; not enemy behavior.</summary>
    public sealed class DamageableTarget : MonoBehaviour
    {
        [SerializeField, Min(1)] private int identity = 1;
        [SerializeField, Min(1)] private int initialHealth = 100;
        [SerializeField] private Collider2D[] hitColliders;
        [SerializeField] private TargetPresentation presentation;
        public HealthSimulation Health { get; private set; }
        public DamageableIdentity Identity => new DamageableIdentity(identity);
        public Collider2D[] HitColliders => hitColliders;
        public int DisplayedHealth { get; private set; }
        public int InitialHealth => initialHealth;

        public void Initialize()
        {
            if (Health != null) throw new InvalidOperationException("Target is already initialized.");
            Health = new HealthSimulation(Identity, initialHealth);
            presentation.Present(Health);
            DisplayedHealth = Health.Current;
        }

        public bool ReceiveDamage(DamageRequest request, out DamageResult result)
        {
            if (Health == null) throw new InvalidOperationException("Initialize target health before resolving damage.");
            if (!Health.TryApplyDamage(request, out result)) return false;
            if (!Health.IsAlive)
                foreach (var collider in hitColliders) collider.enabled = false;
            presentation.Present(Health);
            DisplayedHealth = Health.Current;
            return true;
        }

        // A client owns presentation only: no HealthSimulation and no damage execution.
        public void PresentAuthoritativeHealth(int current, int maximum)
        {
            if (Health != null || maximum != initialHealth || current < 0 || current > maximum)
                throw new InvalidOperationException("Invalid replicated target presentation.");
            DisplayedHealth = current;
            foreach (var collider in hitColliders) collider.enabled = false;
            presentation.Present(current, maximum);
        }
    }
}
