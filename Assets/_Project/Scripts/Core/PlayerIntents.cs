using System.Numerics;

namespace SpaceCrawler.Core
{
    public readonly struct MoveIntent
    {
        public PlayerIdentity Player { get; }
        public Vector2 Direction { get; }
        public bool Sprint { get; }
        public MoveIntent(PlayerIdentity player, Vector2 direction, bool sprint = false)
        { Player = player; Direction = direction; Sprint = sprint; }
    }

    public readonly struct AimIntent
    {
        public PlayerIdentity Player { get; }
        public Vector2 WorldDirection { get; }
        public AimIntent(PlayerIdentity player, Vector2 worldDirection) { Player = player; WorldDirection = worldDirection; }
    }
}
