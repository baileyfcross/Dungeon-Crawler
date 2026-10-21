using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed class MeleeEnemyPresentation : MonoBehaviour
    {
        [SerializeField] private LineRenderer indicator;
        private EnemyActor actor;
        private long presentedStrike;
        private float strikeUntil;
        private void LateUpdate()
        {
            if (actor == null) actor = GetComponent<EnemyActor>();
            if (actor == null || actor.Configuration == null) return;
            if (actor.StrikeSequence > presentedStrike) { presentedStrike = actor.StrikeSequence; strikeUntil = Time.time + .12f; }
            var alive = actor.Lifecycle == EnemyLifecycle.Alive;
            indicator.enabled = alive;
            if (!alive) return;
            var state = actor.BehaviourState;
            var windup = state == EnemyBehaviourState.Windup || state == EnemyBehaviourState.Strike || Time.time < strikeUntil;
            indicator.startColor = indicator.endColor = windup
                ? new Color(1, .35f, .1f, .35f + .65f * actor.WindupProgress) : new Color(1, .8f, .4f);
            var origin = transform.position;
            if (!windup)
            { indicator.positionCount = 2; indicator.SetPosition(0, origin); indicator.SetPosition(1, origin + (Vector3)actor.Facing * .7f); return; }
            var angle = Mathf.Atan2(actor.Facing.y, actor.Facing.x) * Mathf.Rad2Deg;
            indicator.positionCount = 15; indicator.SetPosition(0, origin);
            for (var i = 0; i <= 12; i++)
            {
                var radians = (angle - 45 + 90f * i / 12) * Mathf.Deg2Rad;
                indicator.SetPosition(i + 1, origin + new Vector3(Mathf.Cos(radians), Mathf.Sin(radians)) * actor.Configuration.Reach);
            }
            indicator.SetPosition(14, origin);
        }
    }
}
