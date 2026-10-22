using SpaceCrawler.Core;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Small command endpoint shared by solo and network input adapters.</summary>
    public interface IPlayerCommands
    {
        PlayerState State { get; }
        bool InputActive { get; }
        void SetInputActive(bool active);
        bool Move(MoveIntent intent);
        bool Aim(AimIntent intent);
        bool Attack(AttackIntent intent);
        bool Interact(PlayerIdentity player, bool held);
    }

    public sealed class LocalPlayerCommands : IPlayerCommands
    {
        private readonly PlayerSimulation player;
        private readonly CombatResolver combat;
        private readonly System.Func<PlayerIdentity,bool,bool> interact;
        public LocalPlayerCommands(PlayerSimulation player, CombatResolver combat, System.Func<PlayerIdentity,bool,bool> interact = null) { this.player = player; this.combat = combat; this.interact = interact; }
        public PlayerState State => player.State;
        public bool InputActive => player.IsActive;
        public void SetInputActive(bool active) => player.SetActive(active);
        public bool Move(MoveIntent intent) => player.RequestMove(intent);
        public bool Aim(AimIntent intent) => player.RequestAim(intent);
        public bool Attack(AttackIntent intent) => combat != null && combat.RequestAttack(intent);
        public bool Interact(PlayerIdentity identity, bool held) => InputActive && State.Player.Equals(identity) && (interact?.Invoke(identity,held) ?? false);
    }
}
