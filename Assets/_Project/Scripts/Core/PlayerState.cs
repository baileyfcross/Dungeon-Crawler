using System.Numerics;

namespace SpaceCrawler.Core
{
    public readonly struct PlayerState
    {
        public PlayerIdentity Player { get; }
        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
        public Vector2 AimDirection { get; }
        public PlayerState(PlayerIdentity player, Vector2 position, Vector2 velocity, Vector2 aimDirection)
        { Player = player; Position = position; Velocity = velocity; AimDirection = aimDirection; }
    }
}
