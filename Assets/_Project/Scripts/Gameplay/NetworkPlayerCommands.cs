using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed class NetworkPlayerCommands : IPlayerCommands
    {
        private readonly Func<PlayerState> state;
        private readonly Func<uint> epoch;
        private readonly Action<PlayerInputMessage, bool> submit;
        private Vector2 move, aim = Vector2.right;
        private ulong sequence, attackSequence;
        private bool interactHeld;
        public PlayerState State => state();
        public bool InputActive { get; private set; }
        public NetworkPlayerCommands(Func<PlayerState> state, Func<uint> epoch, Action<PlayerInputMessage, bool> submit)
        { this.state = state; this.epoch = epoch; this.submit = submit; }
        public void SetInputActive(bool active)
        {
            InputActive = active;
            if (!active) { move = Vector2.zero; interactHeld = false; }
            Send(true, false);
        }
        public bool Move(MoveIntent intent)
        {
            if (!InputActive || !State.Player.Equals(intent.Player)) return false;
            move = new Vector2(intent.Direction.X, intent.Direction.Y); return true;
        }
        public bool Aim(AimIntent intent)
        {
            if (!InputActive || !State.Player.Equals(intent.Player)) return false;
            if (intent.WorldDirection.LengthSquared() >= 0.000001f) aim = new Vector2(intent.WorldDirection.X, intent.WorldDirection.Y);
            return true;
        }
        public bool Attack(AttackIntent intent)
        {
            if (!InputActive || !State.Player.Equals(intent.Player)) return false;
            Send(true, true); return true;
        }
        public void SendMovement() => Send(false, false);
        public bool Interact(PlayerIdentity player, bool held)
        {
            if (!InputActive || !State.Player.Equals(player)) return false;
            if (interactHeld == held) return true;
            interactHeld = held; Send(true, false); return true;
        }
        private void Send(bool reliable, bool attack)
        {
            submit(new PlayerInputMessage { Epoch = epoch(), Sequence = ++sequence, AttackSequence = attack ? ++attackSequence : 0,
                ClaimedPlayer = State.Player.Value, Active = InputActive, InteractHeld = InputActive && interactHeld,
                Move = InputActive ? move : Vector2.zero, Aim = aim }, reliable);
        }
    }
}
