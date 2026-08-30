using System;
using System.Numerics;

namespace SpaceCrawler.Core
{
    /// <summary>Validates player intentions. The physics adapter resolves permitted velocity against collisions.</summary>
    public sealed class PlayerSimulation
    {
        public PlayerState State { get; private set; }
        public float MovementSpeed { get; }
        public bool IsActive { get; private set; }
        public Vector2 DesiredVelocity { get; private set; }

        public PlayerSimulation(PlayerIdentity player, float movementSpeed)
        {
            if (player.Value <= 0) throw new ArgumentException("An explicit player identity is required.", nameof(player));
            if (float.IsNaN(movementSpeed) || float.IsInfinity(movementSpeed) || movementSpeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(movementSpeed));
            MovementSpeed = movementSpeed;
            State = new PlayerState(player, Vector2.Zero, Vector2.Zero, Vector2.UnitX);
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            if (!active) DesiredVelocity = Vector2.Zero;
        }

        public bool RequestMove(MoveIntent intent)
        {
            if (!IsActive || !State.Player.Equals(intent.Player) || !IsFinite(intent.Direction)) return false;
            var direction = intent.Direction;
            if (direction.LengthSquared() > 1) direction = Vector2.Normalize(direction);
            DesiredVelocity = direction * MovementSpeed;
            return true;
        }

        public bool RequestAim(AimIntent intent)
        {
            if (!IsActive || !State.Player.Equals(intent.Player) || !IsFinite(intent.WorldDirection)
                || intent.WorldDirection.LengthSquared() < 0.000001f) return false;
            State = new PlayerState(State.Player, State.Position, State.Velocity, Vector2.Normalize(intent.WorldDirection));
            return true;
        }

        // Called only by the simulation's physics adapter, never by input or presentation.
        public void RecordPhysics(PlayerIdentity player, Vector2 position, Vector2 velocity)
        {
            if (!State.Player.Equals(player) || !IsFinite(position) || !IsFinite(velocity))
                throw new ArgumentException("Invalid physics result for this player.");
            State = new PlayerState(player, position, velocity, State.AimDirection);
        }

        private static bool IsFinite(Vector2 value) => !float.IsNaN(value.LengthSquared()) && !float.IsInfinity(value.LengthSquared());
    }
}
