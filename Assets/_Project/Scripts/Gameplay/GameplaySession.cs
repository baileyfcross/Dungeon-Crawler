using System;
using System.IO;
using System.Security.Cryptography;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Wires one local player explicitly and orders presentation after physics.</summary>
    public sealed class GameplaySession : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float movementSpeed = 5;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInputAdapter input;
        [SerializeField] private PlayerPresentation presentation;
        [SerializeField] private PlayerCameraFollower follower;
        [SerializeField] private WeaponDefinition startingWeapon;
        [SerializeField] private CombatResolver combat;
        [SerializeField] private RunGenerationDefinition generation;
        [SerializeField] private RoomLoader rooms;
        [UnityEngine.Serialization.FormerlySerializedAs("useDevelopmentSeed")]
        [SerializeField] private bool useFixedSeed;
        [UnityEngine.Serialization.FormerlySerializedAs("developmentSeed")]
        [SerializeField] private uint fixedSeed = 42;
        public PlayerSimulation Simulation { get; private set; }
        public DungeonGraph Run { get; private set; }
        public RunSequence Sequence { get; private set; }
        [SerializeField] private RunRewardDefinition rewardDefinition;
        private RunKillRewards killRewards;
        public RunRewardPolicy RewardPolicy { get; private set; }
        internal void RewardEnemy(int level,RunRoomId room,EnemyActor actor)
        {if(actor.Simulation!=null && actor.Simulation.Lifecycle==EnemyLifecycle.Dead)killRewards?.TryCredit(level,room,actor.Identity.Spawn,actor.Archetype);}
        public int EncounterVersion { get; private set; }
        public RunDifficulty Difficulty { get; private set; }
        public int LevelNumber => Sequence?.LevelNumber ?? 1;
        public PlayerMotor Motor => motor;
        public PlayerInputAdapter Input => input;
        public PlayerCameraFollower Follower => follower;
        public CombatResolver Combat => combat;
        public RoomLoader Rooms => rooms;
        public string WeaponDisplayName => startingWeapon != null ? startingWeapon.DisplayName : "Unarmed";
        private Action<RunCheckpoint> commitCheckpoint;
        private Action<RunEndReceipt> endRun;
        private ApplicationFlow flow;
        private bool runFinished;
        private EnemyCombatSystem enemies;
        public RunWorldPersistence Persistence { get; private set; }
        private long savedAttackCount;
        public RunLife Life { get; private set; }
        public RunCompletion Completion { get; private set; }
        public RunEndReceipt Outcome { get; private set; }
        public string CompletionHint => Completion?.Available == true ? "RUN EXIT — approach the gold marker and press E / gamepad South to finish. You can still backtrack." : string.Empty;
        public string HealthFailure { get; private set; }
        public event Action HealthChanged;
        private readonly PlayerSoundState soundState=new PlayerSoundState();
        public bool PollLocalDevices { get; set; } = true;

        public void Bind(PlayerIdentity identity, ApplicationFlow flow, ulong? runSeed = null, bool useLevelSequence = false,
            RunCheckpoint checkpoint = null, Action<RunCheckpoint> save = null, WeaponDefinition restoredWeapon = null,
            Guid? profileId = null, Action<RunEndReceipt> finish = null, RunDifficulty difficulty = RunDifficulty.Legacy, bool enableKillRewards = false)
        {
            if (Simulation != null) throw new InvalidOperationException("Session is already bound.");
            if (generation == null) throw new InvalidOperationException("Missing run generation definition.");
            // The local session selects a seed once; a future authority can explicitly supply it.
            // Neither seed selection nor Unity state is consulted inside Core generation.
            var seed = runSeed ?? (Debug.isDebugBuild && useFixedSeed ? fixedSeed : CreateRunSeed());
            commitCheckpoint = save; this.flow = flow; endRun = finish;
            Persistence = new RunWorldPersistence(checkpoint?.World);
            DifficultyRules.Factor(difficulty); Difficulty=checkpoint?.Difficulty ?? difficulty;
            EncounterVersion=checkpoint?.EncounterVersion ?? (Difficulty==RunDifficulty.Legacy?0:EncounterComposition.CurrentVersion);
            RewardPolicy=checkpoint!=null ? checkpoint.Rewards : enableKillRewards ? (rewardDefinition!=null?rewardDefinition.CreatePolicy():RunRewardPolicy.Default()) : null;
            Life = new RunLife(checkpoint?.Life ?? RunLifeSnapshot.Fresh(1));
            killRewards=new RunKillRewards(Life,RewardPolicy,Difficulty,checkpoint?.World);
            soundState.Observe(Life[0]);
            Life.BindProfile(0, profileId ?? (Life.Capture().Profiles[0] != Guid.Empty ? Life.Capture().Profiles[0] : Guid.NewGuid()));
            Sequence = checkpoint != null ? RunSequence.Restore(checkpoint, generation.CreateConfiguration())
                : useLevelSequence ? new RunSequence(seed, generation.CreateConfiguration()) : null;
            if (checkpoint != null)
            {
                if (checkpoint.Mode != SavedSessionMode.Solo || restoredWeapon == null || restoredWeapon.CreateConfiguration().Id != checkpoint.EquipmentIds[0])
                    throw new InvalidOperationException("Saved solo equipment or session mode is incompatible.");
                startingWeapon = restoredWeapon;
            }
            Run = Sequence?.CurrentGraph ?? new RunGenerator().Generate(seed, generation.CreateConfiguration());
            Simulation = new PlayerSimulation(identity, movementSpeed);
            Simulation.SetLifeActive(Life[0].State == PlayerLifeState.Alive);
            Completion = new RunCompletion(Life);
            Life.Changed += ApplyLife;
            motor.Bind(Simulation);
            follower.Bind(motor.transform);
            combat.Bind(Simulation, startingWeapon != null ? startingWeapon.CreateConfiguration() : null);
            input.Bind(new LocalPlayerCommands(Simulation,combat,Interact),flow);
            if (Debug.isDebugBuild)
                Debug.Log($"[RunGeneration] seed={Run.Seed} version={Run.GenerationVersion} nodes={Run.Nodes.Count} connections={Run.Connections.Count} start={Run.StartRoomId} terminal={Run.TerminalRoomId}", this);
            // The original isolated control/combat test prefab intentionally has no loader.
            // Normal New Game requires the room-enabled composition in ApplicationRoot.
            if (rooms != null) rooms.Bind(this, flow, generation, checkpoint);
            if (rooms != null) enemies = new EnemyCombatSystem(transform, _ => motor.Body.position, () => Life,
                (player, attack, amount) => DamagePlayer(attack, amount));
        }

        private static ulong CreateRunSeed()
        {
            var bytes = new byte[8];
            using (var source = RandomNumberGenerator.Create()) source.GetBytes(bytes);
            ulong seed = 0;
            for (var i = 0; i < bytes.Length; i++) seed |= (ulong)bytes[i] << (8 * i);
            return seed;
        }

        public bool TryAdvanceLevel(RunProgression progression)
        {
            if (Sequence == null || !Sequence.TryAdvance(progression)) return false;
            Run = Sequence.CurrentGraph;
            return true;
        }

        public void CommitCheckpoint(RunProgression progression)
        {
            if (Life.Ended || rooms?.CurrentRoom == null) return;
            var world = Persistence.Capture(LevelNumber,progression.CurrentRoomId,rooms.CurrentRoom,new[] {motor},new[] {Simulation},new[] {combat});
            if (commitCheckpoint != null && Sequence != null)
                Persistence.Commit(RunCheckpoint.Capture(Sequence,progression,SavedSessionMode.Solo,new[] {combat.Simulation.EquippedWeapon.Id},Life.Capture(),world,Difficulty,encounterVersion:EncounterVersion,rewards:RewardPolicy),commitCheckpoint);
        }

        public void SaveCurrent()
        { if (Rooms?.Progression != null && (Rooms.Progression.State == RoomFlowState.Playing || Rooms.Progression.State == RoomFlowState.Terminal)) CommitCheckpoint(Rooms.Progression); }
        public bool DamagePlayer(Guid attackId, int amount) => Life.TryDamage(Simulation.State.Player, attackId, amount);
        private void ApplyLife()
        {
            soundState.Observe(Life[0]);
            Simulation.SetLifeActive(Life[0].State == PlayerLifeState.Alive && !Life.Ended);
            if (!Simulation.CanAct) { Motor.Stop(); combat.ClearRoom(); }
            try { if (Life.Ended) FinishRun(); else SaveCurrent(); HealthFailure = null; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Progress could not be saved. Check storage availability. Retrying run completion."; }
            HealthChanged?.Invoke();
        }
        private void FinishRun()
        {
            if (runFinished) return;
            flow.BeginRunEnd();
            Outcome = Outcome ?? Life.CreateReceipt(Life.EndReason.Value);
            combat.ClearRoom(); endRun?.Invoke(Outcome);
            runFinished = true;
            flow.TryRequest(Outcome.Reason == RunEndReason.Completed ? ApplicationCommand.CompleteRun : ApplicationCommand.EndRun);
        }

        public bool Interact(PlayerIdentity player,bool held)
        { ObserveCompletion(); return Simulation.IsActive && Completion.Interact(player,held); }
        private void ObserveCompletion()
        {
            if (Completion == null || rooms?.CurrentRoom == null) return;
            var anchor = rooms.CurrentRoom.RunExitAnchor; var exit = anchor != null ? (Vector2)anchor.position : Vector2.zero;
            Completion.Observe(LevelNumber,Sequence != null,rooms.Progression,new System.Numerics.Vector2(exit.x,exit.y),
                new[] { new System.Numerics.Vector2(motor.Body.position.x,motor.Body.position.y) },new[] { Simulation.IsActive && !flow.RunEndPending });
        }
        private void Update()
        {
            if (Life?.Ended == true && !runFinished) ApplyLife();
            ObserveCompletion();
            if (PollLocalDevices) input.SampleMovement();
            if (Persistence != null && Persistence.AutosaveDue(Time.unscaledTime)) TrySave();
        }
        private void FixedUpdate()
        {
            if(enemies==null)return;
            enemies.RetainOccupiedRooms(new[]{rooms.CurrentRoom});
            enemies.Advance(rooms.CurrentRoom,flow.State==ApplicationState.Gameplay && !Life.Ended,Time.fixedDeltaTime);
        }
        internal bool TrySave()
        {
            try { SaveCurrent(); return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Progress could not be saved. Check storage availability."; HealthChanged?.Invoke(); return false; }
        }
        private void OnApplicationPause(bool paused) { if (paused && Life != null) TrySave(); }
        private void OnApplicationQuit() { if (Life != null) TrySave(); }
        private void OnDestroy() { enemies?.Dispose(); if (Life != null) Life.Changed -= ApplyLife; }
        private void LateUpdate()
        {
            if (Simulation == null) return;
            motor.CaptureState();
            follower.Follow();
            // Recalculate even when the pointer is stationary, using the camera's current presentation.
            if (PollLocalDevices)
            {
                input.SampleAim(follower.GameplayCamera, motor.transform.position);
                input.SampleAttack();
            }
            presentation.Present(Simulation.State);
            if (savedAttackCount != combat.Simulation.AcceptedAttackCount)
            { savedAttackCount = combat.Simulation.AcceptedAttackCount; TrySave(); }
        }
    }
}
