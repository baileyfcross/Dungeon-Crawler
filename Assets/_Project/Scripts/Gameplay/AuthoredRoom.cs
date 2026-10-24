using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Explicit reusable room contract. All references are authored, never found by names.</summary>
    public sealed class AuthoredRoom : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer floor;
        [SerializeField] private Rect playableBounds = new Rect(-12, -2, 24, 16);
        [SerializeField] private Collider2D[] staticCollision;
        [SerializeField] private Transform entryAnchor, playerSpawnAnchor, contentRoot;
        [SerializeField] private Transform playerOneSpawnAnchor, playerTwoSpawnAnchor;
        [SerializeField] private RoomExitTrigger singleExit, leftExit, rightExit;
        [SerializeField] private RoomExitTrigger[] cardinalExits = Array.Empty<RoomExitTrigger>();
        public IReadOnlyList<RoomExitTrigger> CardinalExits => cardinalExits;
        [SerializeField] private DamageableTarget[] targets;
        [SerializeField] private EnemySpawn[] enemySpawns = Array.Empty<EnemySpawn>();
        [SerializeField] private EnemySpawn[] difficultySpawns = Array.Empty<EnemySpawn>();
        [SerializeField, Min(1)] private int baseEnemyCount = 3;
        public IReadOnlyList<EnemySpawn> SelectEncounter(RunDifficulty difficulty, bool startingRoom, bool terminal, ulong seed)
        {
            if (startingRoom || terminal) return Array.Empty<EnemySpawn>();
            if (difficulty == RunDifficulty.Legacy) return enemySpawns;
            var slots = new List<EnemySpawn>(difficultySpawns);
            var random = new DeterministicRandom(seed);
            for (int i=slots.Count-1;i>0;i--) { int j=random.NextInt(i+1); var old=slots[i];slots[i]=slots[j];slots[j]=old; }
            return slots.GetRange(0,DifficultyRules.EnemyCount(baseEnemyCount,slots.Count,difficulty,false));
        }
        [SerializeField] private Transform runExitAnchor;
        public Transform RunExitAnchor => runExitAnchor;
        public void ShowRunExit(bool available)
        {
            if (runExitAnchor == null) { if (available) throw new InvalidOperationException("An authored run exit is required."); return; }
            runExitAnchor.gameObject.SetActive(available);
        }
        private IReadOnlyList<DamageableTarget> combatTargets;
        public RoomEncounter Encounter { get; private set; }
        public IReadOnlyList<DamageableTarget> CombatTargets => combatTargets ?? Targets;
        public Rect PlayableBounds => playableBounds;
        public Transform EntryAnchor => entryAnchor;
        public Transform PlayerSpawnAnchor => playerSpawnAnchor;
        public Transform PlayerOneSpawnAnchor => playerOneSpawnAnchor;
        public Transform PlayerTwoSpawnAnchor => playerTwoSpawnAnchor;
        public Transform ContentRoot => contentRoot;
        public bool IsClearOfArrivals(Vector2 position)
        {
            foreach(var anchor in new[]{playerSpawnAnchor,playerOneSpawnAnchor,playerTwoSpawnAnchor})
                if(anchor!=null && Vector2.Distance(position,anchor.position)<3)return false;
            for(int port=0;port<cardinalExits.Length;port++)
            {
                Vector2 inward=port==0?Vector2.down:port==1?Vector2.left:port==2?Vector2.up:Vector2.right;
                Vector2 tangent=new Vector2(-inward.y,inward.x);
                Vector2 door=cardinalExits[port].transform.position;
                if(Vector2.Distance(position,door)<3)return false;
                for(int slot=0;slot<2;slot++)if(Vector2.Distance(position,door+inward*2+tangent*(slot==0?-.8f:.8f))<3)return false;
            }
            return true;
        }
        public RoomExitTrigger SingleExit => singleExit;
        public RoomExitTrigger LeftExit => leftExit;
        public RoomExitTrigger RightExit => rightExit;
        public IReadOnlyList<DamageableTarget> Targets => Array.AsReadOnly(targets);

        public void ValidateAuthoring()
        {
            RequireChild(floor); RequireChild(entryAnchor); RequireChild(playerSpawnAnchor); RequireChild(contentRoot);
            RequireChild(singleExit); RequireChild(leftExit); RequireChild(rightExit);
            if (entryAnchor == playerSpawnAnchor || singleExit == leftExit || leftExit == rightExit || singleExit == rightExit)
                throw new InvalidOperationException("Room anchors and exits must be distinct.");
            if (playableBounds.width <= 2 || playableBounds.height <= 2) throw new InvalidOperationException("Invalid playable bounds.");
            var entry = transform.InverseTransformPoint(entryAnchor.position);
            var spawn = transform.InverseTransformPoint(playerSpawnAnchor.position);
            if (!Finite(entry) || !Finite(spawn)) throw new InvalidOperationException("Room anchors must have finite coordinates.");
            var safeBounds = new Rect(playableBounds.xMin + 0.6f, playableBounds.yMin + 0.6f, playableBounds.width - 1.2f, playableBounds.height - 1.2f);
            if (Mathf.Abs(entry.y - playableBounds.yMin) > 0.1f || !safeBounds.Contains(spawn) || Vector2.Distance(entry, spawn) < 1)
                throw new InvalidOperationException("Require a south entry and a separate safe interior spawn.");
            foreach (var exit in new[] { singleExit, leftExit, rightExit })
            {
                RequireChild(exit.Trigger);
                if (exit.Trigger.transform != exit.transform || !exit.Trigger.isTrigger || exit.Trigger.attachedRigidbody != null || !exit.Trigger.enabled)
                    throw new InvalidOperationException("Exit must have an enabled static trigger collider.");
                var position = transform.InverseTransformPoint(exit.transform.position);
                if (!playableBounds.Contains(position) || position.y < playableBounds.yMax - 2)
                    throw new InvalidOperationException("Forward exits must be inside the north edge.");
                if (exit.Trigger.bounds.Contains(playerSpawnAnchor.position)) throw new InvalidOperationException("Spawn overlaps an exit.");
            }
            var single = transform.InverseTransformPoint(singleExit.transform.position);
            if (Mathf.Abs(single.x - playableBounds.center.x) > 0.1f
                || transform.InverseTransformPoint(leftExit.transform.position).x >= single.x
                || transform.InverseTransformPoint(rightExit.transform.position).x <= single.x)
                throw new InvalidOperationException("Require centered single exit and ordered left/right choice exits.");
            if (staticCollision == null || staticCollision.Length == 0 || targets == null) throw new InvalidOperationException("Missing static collision or target list.");
            foreach (var collision in staticCollision)
            {
                RequireChild(collision);
                if (collision.isTrigger || collision.attachedRigidbody != null || !collision.enabled)
                    throw new InvalidOperationException("Room collision must be enabled, solid, and static.");
            }
            if (cardinalExits.Length != 0 && cardinalExits.Length != 4) throw new InvalidOperationException("Invalid cardinal door list.");
            var doorIds = new HashSet<RoomExitTrigger>();
            foreach (var exit in cardinalExits)
            {
                RequireChild(exit); RequireChild(exit.Trigger);
                if (!doorIds.Add(exit) || !exit.Trigger.isTrigger || exit.Trigger.attachedRigidbody != null || !exit.Trigger.enabled)
                    throw new InvalidOperationException("Invalid cardinal door collider.");
            }
            var identities = new HashSet<DamageableIdentity>();
            var shapes = new HashSet<Collider2D>();
            foreach (var target in targets)
            {
                RequireChild(target);
                if (!target.transform.IsChildOf(contentRoot) || !identities.Add(target.Identity) || target.HitColliders == null || target.HitColliders.Length == 0)
                    throw new InvalidOperationException("Invalid room target identity/content reference.");
                foreach (var collider in target.HitColliders)
                { RequireChild(collider); if (!shapes.Add(collider)) throw new InvalidOperationException("Duplicate target collider registration."); }
                if (target.Identity.Value >= 0x40000000) throw new InvalidOperationException("Room targets use the non-enemy identity range.");
            }
            if (enemySpawns == null || enemySpawns.Length > 32) throw new InvalidOperationException("Invalid enemy spawn list.");
            var spawnIds = new HashSet<int>();
            foreach (var enemySpawn in enemySpawns)
            {
                if (enemySpawn == null || enemySpawn.Identity < 1 || enemySpawn.Identity > 32 || !spawnIds.Add(enemySpawn.Identity) || enemySpawn.Prefab == null)
                    throw new InvalidOperationException("Invalid enemy spawn slot.");
                enemySpawn.Prefab.ValidateEnemyAuthoring();
                if (enemySpawn.Behaviour != null) enemySpawn.Behaviour.CreateConfiguration();
                RequireChild(enemySpawn.Anchor);
                if (!enemySpawn.Anchor.IsChildOf(contentRoot) || !Finite(enemySpawn.Anchor.position)
                    || !safeBounds.Contains(transform.InverseTransformPoint(enemySpawn.Anchor.position)))
                    throw new InvalidOperationException("Enemy spawn must be inside the room content bounds.");
            }
            if(difficultySpawns==null || difficultySpawns.Length>32 || baseEnemyCount<1 || baseEnemyCount>32)
                throw new InvalidOperationException("Invalid difficulty encounter bounds.");
            spawnIds.Clear();var occupied=new List<Vector2>();
            foreach(var slot in difficultySpawns)
            {
                if(slot==null || slot.Identity<1 || slot.Identity>32 || !spawnIds.Add(slot.Identity) || slot.Prefab==null || slot.Behaviour==null)
                    throw new InvalidOperationException("Difficulty encounters require unique functional enemy slots.");
                RequireChild(slot.Anchor);slot.Prefab.ValidateEnemyAuthoring();slot.Behaviour.CreateConfiguration();
                var p=(Vector2)slot.Anchor.position;
                if(!slot.Anchor.IsChildOf(contentRoot) || !safeBounds.Contains(transform.InverseTransformPoint(p))
                    || (gameObject.scene.IsValid() && !IsSafePosition(p)) || !IsClearOfArrivals(p))
                    throw new InvalidOperationException("Enemy spawn overlaps geometry or the arrival area.");
                foreach(var exit in cardinalExits)if(Vector2.Distance(p,exit.transform.position)<3)
                    throw new InvalidOperationException("Enemy spawn obstructs an arrival doorway.");
                foreach(var other in occupied)if(Vector2.Distance(p,other)<1.2f)throw new InvalidOperationException("Enemy spawns overlap.");
                occupied.Add(p);
            }
        }

        public void InitializeEncounter(bool authoritative, uint incarnation, IReadOnlyList<EnemySpawn> spawns = null, RunDifficulty difficulty = RunDifficulty.Legacy)
        {
            if (Encounter != null) throw new InvalidOperationException("Room encounter is already initialized.");
            Encounter = new RoomEncounter(contentRoot, spawns ?? enemySpawns, authoritative, incarnation,difficulty);
            var combined = new List<DamageableTarget>(targets);
            foreach (var enemy in Encounter.Enemies) combined.Add(enemy.Target);
            combatTargets = combined.AsReadOnly();
        }

        public void RetireEncounter() => Encounter?.Retire();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void SetCombatFixtureTargets(DamageableTarget[] fixtureTargets)
        {
            if (targets.Length != 0 || fixtureTargets == null || fixtureTargets.Length == 0)
                throw new InvalidOperationException("A combat fixture may only populate an empty target list.");
            targets = (DamageableTarget[])fixtureTargets.Clone();
            ValidateAuthoring();
            var combined = new List<DamageableTarget>(targets);
            if (Encounter != null) foreach (var enemy in Encounter.Enemies) combined.Add(enemy.Target);
            combatTargets = combined.AsReadOnly();
        }
#endif
        private void OnDestroy() => RetireEncounter();

        private void RequireChild(Component component)
        {
            if (component == null || !component.transform.IsChildOf(transform))
                throw new InvalidOperationException("Missing or external authored room reference.");
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);

        public void ValidateMultiplayerAuthoring()
        {
            ValidateAuthoring();
            RequireChild(playerOneSpawnAnchor); RequireChild(playerTwoSpawnAnchor);
            if (playerOneSpawnAnchor == playerTwoSpawnAnchor || playerOneSpawnAnchor == playerSpawnAnchor || playerTwoSpawnAnchor == playerSpawnAnchor)
                throw new InvalidOperationException("Two explicit multiplayer spawn anchors must be distinct from each other and the solo anchor.");
            var one = transform.InverseTransformPoint(playerOneSpawnAnchor.position);
            var two = transform.InverseTransformPoint(playerTwoSpawnAnchor.position);
            var safe = new Rect(playableBounds.xMin + 0.6f, playableBounds.yMin + 0.6f, playableBounds.width - 1.2f, 3.4f);
            if (!Finite(one) || !Finite(two) || !safe.Contains(one) || !safe.Contains(two) || Vector2.Distance(one, two) < 1.2f
                || Mathf.Abs(one.z) > 0.01f || Mathf.Abs(two.z) > 0.01f)
                throw new InvalidOperationException("Multiplayer spawns must be separate, safe and near the south entry.");
        }

        public RoomExitTrigger ExitFor(RoomConnection edge, IReadOnlyList<RoomConnection> choices)
        {
            if (edge.FromPort >= 0)
            { if (cardinalExits.Length != 4) throw new InvalidOperationException("Four authored cardinal doors are required."); return cardinalExits[edge.FromPort]; }
            if (choices.Count == 1) return singleExit;
            return choices[0].To.Equals(edge.To) ? leftExit : rightExit;
        }
        public static string PortName(int port) => port == 0 ? "NORTH" : port == 1 ? "EAST" : port == 2 ? "SOUTH" : port == 3 ? "WEST" : "EXIT";
        public Vector2 Arrival(int port, int slot = 0, int count = 1)
        {
            if (port < 0) return count == 1 ? (Vector2)playerSpawnAnchor.position : slot == 0 ? (Vector2)playerOneSpawnAnchor.position : (Vector2)playerTwoSpawnAnchor.position;
            Vector2 position = cardinalExits[port].transform.position;
            Vector2 inward = port == 0 ? Vector2.down : port == 1 ? Vector2.left : port == 2 ? Vector2.up : Vector2.right;
            Vector2 tangent = new Vector2(-inward.y,inward.x);
            position += inward * 2 + tangent * (count == 2 ? (slot == 0 ? -.8f : .8f) : 0);
            if (!IsSafePosition(position)) throw new InvalidOperationException("Door arrival is obstructed.");
            return position;
        }
        public void SuppressOccupiedExits(PlayerMotor[] motors)
        {
            Physics2D.SyncTransforms();
            var exits = new List<RoomExitTrigger>(cardinalExits) { singleExit,leftExit,rightExit };
            foreach (var exit in exits) if (exit.gameObject.activeSelf)
                foreach (var motor in motors) foreach (var shape in motor.GetComponents<Collider2D>())
                    if (exit.Trigger.Distance(shape).isOverlapped) exit.SuppressUntilDeparture(motor.Body);
        }
        public bool IsSafePosition(Vector2 position)
        {
            var local = transform.InverseTransformPoint(position);
            var safe = new Rect(playableBounds.xMin+.6f,playableBounds.yMin+.6f,playableBounds.width-1.2f,playableBounds.height-1.2f);
            if (!Finite(position) || !safe.Contains(local)) return false;
            foreach (var collision in staticCollision)
                if (collision.OverlapPoint(position) || Vector2.Distance(collision.ClosestPoint(position),position) < .5f) return false;
            return true;
        }
        private void DisableExits()
        { singleExit.DisableExit(); leftExit.DisableExit(); rightExit.DisableExit(); foreach (var exit in cardinalExits) exit.DisableExit(); }

        public void ConfigureSharedExits(IReadOnlyList<RoomConnection> choices, Action<RoomConnection, Collider2D, bool> presence)
        {
            DisableExits();
            foreach (var edge in choices) ExitFor(edge,choices).ConfigureReadiness(edge,presence);
        }

        public void ConfigureExits(IReadOnlyList<RoomConnection> choices, PlayerIdentity player, Rigidbody2D body, Func<ExitIntent, bool> request)
        {
            DisableExits();
            foreach (var edge in choices) ExitFor(edge,choices).Configure(player,body,edge,request);
        }
    }
}
