using System;
using System.Collections.Generic;
using System.Numerics;
namespace SpaceCrawler.Core
{
    /// <summary>Fresh per-player confirmations at the final exit. Authority supplies observations, never clients.</summary>
    public sealed class RunCompletion
    {
        public const float InteractionRange = 2;
        private readonly RunLife life;
        private readonly bool[] confirmed, released, canInteract;
        private RunProgression context;
        public bool Available { get; private set; }
        public bool PartyReady { get; private set; }
        public int Confirmations { get { int count = 0; foreach (bool value in confirmed) if (value) count++; return count; } }
        public RunCompletion(RunLife life)
        { this.life = life ?? throw new ArgumentNullException(nameof(life)); confirmed = new bool[life.Count]; released = new bool[life.Count]; canInteract = new bool[life.Count]; }
        public void Observe(int level, bool hasSequence, RunProgression progression, Vector2 exit,
            IReadOnlyList<Vector2> positions, IReadOnlyList<bool> inputAvailable)
        {
            if (positions == null || inputAvailable == null || positions.Count != life.Count || inputAvailable.Count != life.Count)
                throw new ArgumentException("Completion observations must match the player slots.");
            if (!ReferenceEquals(context,progression)) { context = progression; Array.Clear(released,0,released.Length); }
            Available = hasSequence && level == RunSequence.LevelCount && progression != null && progression.IsTerminal
                && progression.State == RoomFlowState.Terminal && progression.GameplayActive && !life.Ended;
            PartyReady = Available && PlayerSimulation.IsFinite(exit);
            for (int i = 0; i < life.Count; i++)
            {
                canInteract[i] = inputAvailable[i] && life[i].State == PlayerLifeState.Alive;
                if (!canInteract[i]) released[i] = false;
                PartyReady &= canInteract[i] && PlayerSimulation.IsFinite(positions[i])
                    && Vector2.DistanceSquared(positions[i],exit) <= InteractionRange*InteractionRange;
            }
            if (!PartyReady) Array.Clear(confirmed,0,confirmed.Length);
        }
        public bool Interact(PlayerIdentity player, bool held)
        {
            int slot = player.Value-1;
            if (slot < 0 || slot >= life.Count || life.Ended || !canInteract[slot]) return false;
            if (!held) { released[slot] = true; return false; }
            bool fresh = released[slot]; released[slot] = false;
            if (!fresh || !PartyReady) return false;
            confirmed[slot] = true;
            if (Confirmations == life.Count) return life.TryComplete();
            return true;
        }
    }
    public sealed class RunRewardSummary
    {
        public RunEndReceipt Receipt { get; }
        public long ProfileXp { get; }
        public RunRewardSummary(RunEndReceipt receipt,long profileXp) { Receipt = receipt; ProfileXp = profileXp; }
    }
}
