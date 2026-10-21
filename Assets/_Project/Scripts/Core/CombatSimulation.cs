using System;
using System.Numerics;

namespace SpaceCrawler.Core
{
    /// <summary>Owns this player's equipped configuration and cooldown. No devices or world queries.</summary>
    public sealed class CombatSimulation
    {
        private readonly PlayerSimulation player;
        public WeaponConfiguration EquippedWeapon { get; }
        public float CooldownRemaining { get; private set; }
        public long AcceptedAttackCount { get; private set; }
        public AttackExecution LastExecution { get; private set; }
        public bool IsActive => player.IsActive;

        // Equipment is chosen once for this slice; no runtime switching or inventory API.
        public CombatSimulation(PlayerSimulation playerSimulation, WeaponConfiguration startingWeapon)
        {
            player = playerSimulation ?? throw new ArgumentNullException(nameof(playerSimulation));
            EquippedWeapon = startingWeapon;
        }

        public void AdvanceTime(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (IsActive) CooldownRemaining = Math.Max(0, CooldownRemaining - deltaTime);
        }

        public bool TryAttack(AttackIntent intent, out AttackExecution execution)
        {
            execution = null;
            if (!IsActive || !player.State.Player.Equals(intent.Player) || EquippedWeapon == null || CooldownRemaining > 0)
                return false;
            var length = intent.AimDirection.LengthSquared();
            if (float.IsNaN(length) || float.IsInfinity(length) || Math.Abs(length - 1) > 0.0001f)
                return false;
            // Intent must refer to the accepted aim, not supply a competing device/world direction.
            if (Vector2.DistanceSquared(intent.AimDirection, player.State.AimDirection) > 0.000001f)
                return false;
            CooldownRemaining = EquippedWeapon.Cooldown;
            execution = new AttackExecution(player.State.Player, ++AcceptedAttackCount, EquippedWeapon,
                player.State.Position, player.State.AimDirection);
            LastExecution = execution;
            return true;
        }
    }
}
