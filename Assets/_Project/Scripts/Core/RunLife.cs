using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpaceCrawler.Core
{
    public enum PlayerLifeState { Alive = 1, Downed = 2, Dead = 3 }
    public enum RunEndReason { Loss = 1, Completed = 2 }

    public readonly struct PlayerVitals
    {
        public int Current { get; }
        public int Maximum { get; }
        public PlayerLifeState State { get; }
        public PlayerVitals(int current, int maximum, PlayerLifeState state)
        {
            if (maximum < 1 || maximum > 1000000 || current < 0 || current > maximum
                || state < PlayerLifeState.Alive || state > PlayerLifeState.Dead
                || (current > 0) != (state == PlayerLifeState.Alive)) throw new ArgumentException("Invalid player health.");
            Current = current; Maximum = maximum; State = state;
        }
        public static PlayerVitals Starting => new PlayerVitals(100, 100, PlayerLifeState.Alive);
    }

    /// <summary>Immutable persistence boundary. Class selection and XP sources are deliberately external.</summary>
    public sealed class RunLifeSnapshot
    {
        public Guid RunId { get; }
        public long RunXp { get; }
        public IReadOnlyList<PlayerVitals> Players { get; }
        public IReadOnlyList<Guid> Profiles { get; }
        public IReadOnlyList<string> Classes { get; }
        public RunLifeSnapshot(Guid runId, long runXp, IReadOnlyList<PlayerVitals> players,
            IReadOnlyList<Guid> profiles = null, IReadOnlyList<string> classes = null)
        {
            if (runId == Guid.Empty || runXp < 0 || runXp > long.MaxValue / 100 || players == null || players.Count < 1 || players.Count > 2
                || (profiles != null && profiles.Count != players.Count) || (classes != null && classes.Count != players.Count))
                throw new ArgumentException("Invalid run life snapshot.");
            RunId = runId; RunXp = runXp;
            var health = new PlayerVitals[players.Count]; var owners = new Guid[players.Count]; var identities = new string[players.Count];
            for (var i = 0; i < players.Count; i++)
            {
                health[i] = new PlayerVitals(players[i].Current, players[i].Maximum, players[i].State);
                owners[i] = profiles?[i] ?? Guid.Empty; identities[i] = classes?[i] ?? string.Empty;
                if (identities[i].Length > 128 || (identities[i].Length > 0 && string.IsNullOrWhiteSpace(identities[i]))
                    || (health[i].State == PlayerLifeState.Downed && players.Count == 1) || (health[i].State == PlayerLifeState.Dead && players.Count == 2))
                    throw new ArgumentException("Invalid class or life state.");
            }
            Players = Array.AsReadOnly(health); Profiles = Array.AsReadOnly(owners); Classes = Array.AsReadOnly(identities);
        }
        public static RunLifeSnapshot Fresh(int players)
        {
            if (players != 1 && players != 2) throw new ArgumentOutOfRangeException(nameof(players));
            return new RunLifeSnapshot(Guid.NewGuid(), 0,
                players == 1 ? new[] { PlayerVitals.Starting } : new[] { PlayerVitals.Starting, PlayerVitals.Starting });
        }
    }

    /// <summary>Authoritative health/revival rules. No Unity, networking, rewards or AI decisions.</summary>
    public sealed class RunLife
    {
        public const int BaseRunXpGain = 10;
        public const float ReviveSeconds = 3, ReviveRange = 1.5f;
        private readonly PlayerVitals[] players;
        private readonly Guid[] profiles;
        private readonly string[] classes;
        private readonly bool[] held, requireRelease;
        private readonly float[] progress;
        private readonly HashSet<(Guid, int)> damage = new HashSet<(Guid, int)>();
        public Guid RunId { get; }
        public long RunXp { get; private set; }
        public bool Ended { get; private set; }
        public RunEndReason? EndReason { get; private set; }
        public int Count => players.Length;
        public PlayerVitals this[int index] => players[index];
        public float ReviveProgress(int rescuer) => progress[rescuer];
        public event Action Changed;
        public RunLife(RunLifeSnapshot snapshot)
        {
            RunId = snapshot.RunId; RunXp = snapshot.RunXp;
            players = new List<PlayerVitals>(snapshot.Players).ToArray(); profiles = new List<Guid>(snapshot.Profiles).ToArray();
            classes = new List<string>(snapshot.Classes).ToArray(); held = new bool[Count]; requireRelease = new bool[Count]; progress = new float[Count];
            Ended = Array.TrueForAll(players, p => p.State != PlayerLifeState.Alive);
            if (Ended) EndReason = RunEndReason.Loss;
        }
        public void BindProfile(int slot, Guid profile)
        {
            if (slot < 0 || slot >= Count || profile == Guid.Empty || (profiles[slot] != Guid.Empty && profiles[slot] != profile))
                throw new InvalidOperationException("The saved player slot belongs to a different profile.");
            profiles[slot] = profile;
        }
        public void BindClass(int slot, string classId)
        {
            if (Ended || slot < 0 || slot >= Count || !PlayerClasses.Contains(classId)
                || (classes[slot].Length > 0 && classes[slot] != classId))
                throw new InvalidOperationException("Class selection must be valid and cannot change during a run.");
            classes[slot] = classId;
        }
        public bool TryDamage(PlayerIdentity target, Guid attackId, int amount)
        {
            var slot = target.Value - 1;
            if (Ended || slot < 0 || slot >= Count || players[slot].State != PlayerLifeState.Alive || attackId == Guid.Empty
                || amount <= 0 || damage.Contains((attackId, slot))) return false;
            damage.Add((attackId, slot)); var previous = players[slot]; var hp = Math.Max(0, previous.Current - amount);
            players[slot] = new PlayerVitals(hp, previous.Maximum, hp > 0 ? PlayerLifeState.Alive : Count == 1 ? PlayerLifeState.Dead : PlayerLifeState.Downed);
            CancelRevive(slot, true);
            if (hp == 0) CancelRevive(Count == 2 ? 1 - slot : slot, true);
            Ended = Array.TrueForAll(players, p => p.State != PlayerLifeState.Alive);
            if (Ended) EndReason = RunEndReason.Loss;
            if (Ended) CancelAllRevives();
            Changed?.Invoke(); return true;
        }
        public bool TrySetMaximum(PlayerIdentity player, int maximum)
        {
            var i = player.Value - 1;
            if (Ended || i < 0 || i >= Count || maximum < 1 || maximum > 1000000 || players[i].State != PlayerLifeState.Alive) return false;
            players[i] = new PlayerVitals(Math.Min(players[i].Current, maximum), maximum, PlayerLifeState.Alive);
            Changed?.Invoke(); return true;
        }
        public void SetReviveHeld(PlayerIdentity player, bool value)
        {
            var i = player.Value - 1; if (i < 0 || i >= Count) return;
            if (!value) { requireRelease[i] = false; CancelRevive(i, false); return; }
            if (!Ended && !requireRelease[i] && players[i].State == PlayerLifeState.Alive) held[i] = true;
        }
        public void AdvanceRevival(float delta, IReadOnlyList<Vector2> positions, IReadOnlyList<bool> available)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (Count != 2 || Ended) return;
            for (var i = 0; i < 2; i++)
            {
                var distance = Vector2.DistanceSquared(positions[i], positions[1 - i]);
                if (!held[i] || !available[i] || players[i].State != PlayerLifeState.Alive || players[1 - i].State != PlayerLifeState.Downed
                    || float.IsNaN(distance) || float.IsInfinity(distance) || distance > ReviveRange * ReviveRange)
                { CancelRevive(i, held[i]); continue; }
                progress[i] = Math.Min(ReviveSeconds, progress[i] + delta);
                if (progress[i] + .00001f < ReviveSeconds) continue;
                var target = players[1 - i]; players[1 - i] = new PlayerVitals(Math.Max(1, target.Maximum / 2), target.Maximum, PlayerLifeState.Alive);
                CancelRevive(i, true); Changed?.Invoke();
            }
        }
        private void CancelRevive(int i, bool release)
        {
            // Only a held/interrupted interaction needs a release. A fresh press after damage remains valid.
            if (release && (held[i] || progress[i] > 0)) requireRelease[i] = true;
            held[i] = false; progress[i] = 0;
        }
        public void CancelAllRevives() { for (var i = 0; i < Count; i++) CancelRevive(i, true); }
        // Trusted gameplay systems award the shared run pool; profile conversion occurs only at run end.
        public bool TryAwardRunXp(long amount)
        {
            if (Ended || amount <= 0 || amount > long.MaxValue / 100 - RunXp) return false;
            RunXp += amount; Changed?.Invoke(); return true;
        }
        public bool TryComplete()
        {
            if (Ended || !Array.TrueForAll(players,p => p.State == PlayerLifeState.Alive)) return false;
            Ended = true; EndReason = RunEndReason.Completed; CancelAllRevives(); Changed?.Invoke(); return true;
        }
        public RunLifeSnapshot Capture() => new RunLifeSnapshot(RunId, RunXp, players, profiles, classes);
        public RunEndReceipt CreateReceipt(RunEndReason reason)
        {
            if (!Ended || EndReason != reason) throw new InvalidOperationException("A receipt requires the matching authoritative run outcome.");
            return new RunEndReceipt(RunId, RunXp, reason, profiles);
        }
    }
}
