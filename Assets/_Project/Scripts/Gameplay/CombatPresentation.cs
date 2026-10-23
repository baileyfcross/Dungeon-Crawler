using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>A brief primitive arc of an already accepted melee execution; no hit decisions.</summary>
    public sealed class CombatPresentation : MonoBehaviour
    {
        [SerializeField] private LineRenderer meleeArc;
        private float visibleFor;

        public void ClearRoom()
        {
            visibleFor = 0;
            if (meleeArc == null) return;
            meleeArc.enabled = false;
            meleeArc.positionCount = 0;
        }

        public void Present(AttackExecution execution)
            => Present(execution.Weapon, new Vector2(execution.Origin.X, execution.Origin.Y), new Vector2(execution.AimDirection.X, execution.AimDirection.Y));

        public void Present(WeaponConfiguration weapon, Vector2 acceptedOrigin, Vector2 acceptedAim,bool audible=true)
        {
            if(audible)GameAudio.Emit(weapon.Kind==AttackKind.Melee?SoundCue.KnifeSwing:SoundCue.BlasterShot);
            if (weapon.Kind != AttackKind.Melee) return;
            const int segments = 24;
            var origin = (Vector3)acceptedOrigin;
            var directionAngle = Mathf.Atan2(acceptedAim.y, acceptedAim.x) * Mathf.Rad2Deg;
            meleeArc.positionCount = segments + 3;
            meleeArc.SetPosition(0, origin);
            for (var i = 0; i <= segments; i++)
            {
                var angle = (directionAngle - weapon.MeleeArcDegrees / 2 + weapon.MeleeArcDegrees * i / segments) * Mathf.Deg2Rad;
                meleeArc.SetPosition(i + 1, origin + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * weapon.MeleeRange);
            }
            meleeArc.SetPosition(segments + 2, origin);
            meleeArc.enabled = true;
            visibleFor = 0.15f;
        }

        private void Update()
        {
            if (visibleFor <= 0) return;
            visibleFor -= Time.deltaTime;
            if (visibleFor <= 0) meleeArc.enabled = false;
        }
    }
}
