using System;
using SpaceCrawler.Core;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Sender ownership/freshness adapter. Existing Core systems still decide gameplay rules.</summary>
    public sealed class SessionAuthority
    {
        public const double InputTimeoutSeconds = 0.35;
        private readonly ulong hostSender;
        private ulong? clientSender;
        private readonly PlayerSimulation[] players;
        private readonly Func<int, AttackIntent, bool> attack;
        private readonly Action<int, bool> interact;
        private readonly ulong[] sequences = new ulong[2], attackSequences = new ulong[2], gateSequences = new ulong[2];
        private readonly double[] receivedAt = new double[2];
        private readonly bool[] input = new bool[2];
        public uint Epoch { get; private set; }
        public bool Running { get; private set; }
        public bool BothConnected => clientSender.HasValue;
        public int RejectedOwnership { get; private set; }
        public int RejectedStale { get; private set; }
        public int TimedOut { get; private set; }

        public SessionAuthority(ulong hostSender, PlayerSimulation one, PlayerSimulation two, Func<int, AttackIntent, bool> attack, Action<int, bool> interact = null)
        {
            if (one.State.Player.Value != 1 || two.State.Player.Value != 2) throw new ArgumentException("Explicit player 1/2 simulations required.");
            this.hostSender = hostSender; players = new[] { one, two }; this.attack = attack; this.interact = interact;
        }
        public bool Connect(ulong sender)
        {
            if (sender == hostSender || clientSender.HasValue) return false;
            clientSender = sender; return true;
        }
        public bool TryIdentity(ulong sender, out PlayerIdentity player)
        {
            player = sender == hostSender ? new PlayerIdentity(1)
                : clientSender.HasValue && sender == clientSender.Value ? new PlayerIdentity(2) : default;
            return player.Value != 0;
        }
        public bool InputEnabled(int index) => input[index] && players[index].CanAct;
        public void SetRunning(bool running, uint epoch)
        {
            Running = running && BothConnected; Epoch = epoch;
            for (var i = 0; i < 2; i++) { input[i] = false; players[i].SetActive(Running); Neutral(i); }
        }
        public bool Submit(ulong actualSender, PlayerInputMessage command, double now)
        {
            if (!TryIdentity(actualSender, out var identity) || identity.Value != command.ClaimedPlayer)
            { RejectedOwnership++; return false; }
            var i = identity.Value - 1;
            if (!Running || command.Epoch != Epoch || command.Sequence == 0
                || (command.Sequence <= sequences[i] && command.AttackSequence == 0))
            { RejectedStale++; return false; }
            var player = players[i];
            if (command.Sequence > sequences[i])
            {
                sequences[i] = command.Sequence; receivedAt[i] = now;
                if (input[i] != command.Active) gateSequences[i] = command.Sequence;
                input[i] = command.Active;
                interact?.Invoke(i, input[i] && player.CanAct && command.InteractHeld);
                if (!player.CanAct) { Neutral(i); return false; }
                if (input[i])
                {
                    if (!player.RequestMove(new MoveIntent(identity, new CoreVector(command.Move.x, command.Move.y), command.Sprint)))
                    { Neutral(i); return false; }
                    player.RequestAim(new AimIntent(identity, new CoreVector(command.Aim.x, command.Aim.y)));
                }
                else Neutral(i);
            }
            if (command.AttackSequence == 0) return true;
            if (command.AttackSequence <= attackSequences[i]) return false;
            attackSequences[i] = command.AttackSequence;
            if (!InputEnabled(i) || !command.Active || command.Sequence < gateSequences[i]) return false;
            // Reliable attack carries its sampled aim. Core must accept it before the ordinary attack rule.
            if (!player.RequestAim(new AimIntent(identity, new CoreVector(command.Aim.x, command.Aim.y)))) return false;
            return attack(i, new AttackIntent(identity, player.State.AimDirection));
        }
        public void Advance(double now)
        {
            if (!Running) return;
            for (var i = 0; i < 2; i++)
                if (input[i] && now - receivedAt[i] > InputTimeoutSeconds)
                { input[i] = false; Neutral(i); TimedOut++; }
        }
        private void Neutral(int i)
        { players[i].RequestMove(new MoveIntent(players[i].State.Player, CoreVector.Zero)); interact?.Invoke(i, false); }
        public void Disconnect()
        { clientSender = null; SetRunning(false, Epoch); }
    }
}
