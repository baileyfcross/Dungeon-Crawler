using System;
using System.Numerics;

namespace SpaceCrawler.Core
{
    public static class MeleeRules
    {
        // The fixture uses target centers for both range and cone tests.
        public static bool Contains(AttackExecution execution, Vector2 targetPosition)
        {
            if (execution == null || execution.Weapon.Kind != AttackKind.Melee) return false;
            var offset = targetPosition - execution.Origin;
            var distanceSquared = offset.LengthSquared();
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared)
                || distanceSquared > execution.Weapon.MeleeRange * execution.Weapon.MeleeRange) return false;
            if (distanceSquared < 0.000001f) return true;
            var minimumDot = Math.Cos(execution.Weapon.MeleeArcDegrees * Math.PI / 360.0);
            return Vector2.Dot(Vector2.Normalize(offset), execution.AimDirection) >= minimumDot - 0.000001;
        }
    }
}
