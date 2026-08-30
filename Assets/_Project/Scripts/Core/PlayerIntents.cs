using System.Numerics;

namespace SpaceCrawler.Core
{
    public readonly struct MoveIntent
    {
        public PlayerIdentity Player { get; }
        public Vector2 Direction { get; }
        public MoveIntent(PlayerIdentity player, Vector2 direction) { Player = player; Direction = direction; }
    }

    public readonly struct AimIntent
    {
        public PlayerIdentity Player { get; }
        public Vector2 WorldDirection { get; }
        public AimIntent(PlayerIdentity player, Vector2 worldDirection) { Player = player; WorldDirection = worldDirection; }
    }
}
