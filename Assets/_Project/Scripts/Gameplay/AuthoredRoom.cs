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
        [SerializeField] private DamageableTarget[] targets;
        [SerializeField] private EnemySpawn[] enemySpawns = Array.Empty<EnemySpawn>();
        private IReadOnlyList<DamageableTarget> combatTargets;
        public RoomEncounter Encounter { get; private set; }
        public IReadOnlyList<DamageableTarget> CombatTargets => combatTargets ?? Targets;
        public Rect PlayableBounds => playableBounds;
        public Transform EntryAnchor => entryAnchor;
        public Transform PlayerSpawnAnchor => playerSpawnAnchor;
        public Transform PlayerOneSpawnAnchor => playerOneSpawnAnchor;
        public Transform PlayerTwoSpawnAnchor => playerTwoSpawnAnchor;
        public Transform ContentRoot => contentRoot;
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
        }

        public void InitializeEncounter(bool authoritative, uint incarnation, IReadOnlyList<EnemySpawn> spawns = null)
        {
            if (Encounter != null) throw new InvalidOperationException("Room encounter is already initialized.");
            Encounter = new RoomEncounter(contentRoot, spawns ?? enemySpawns, authoritative, incarnation);
            var combined = new List<DamageableTarget>(targets);
            foreach (var enemy in Encounter.Enemies) combined.Add(enemy.Target);
            combatTargets = combined.AsReadOnly();
        }

        public void RetireEncounter() => Encounter?.Retire();
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

        public void ConfigureSharedExits(IReadOnlyList<RoomConnection> choices, Action<RoomConnection, Collider2D, bool> presence)
        {
            singleExit.DisableExit(); leftExit.DisableExit(); rightExit.DisableExit();
            if (choices.Count == 1) singleExit.ConfigureReadiness(choices[0], presence);
            else if (choices.Count == 2)
            { leftExit.ConfigureReadiness(choices[0], presence); rightExit.ConfigureReadiness(choices[1], presence); }
            else if (choices.Count != 0) throw new InvalidOperationException("Unsupported forward exit count.");
        }

        public void ConfigureExits(IReadOnlyList<RoomConnection> choices, PlayerIdentity player, Rigidbody2D body, Func<ExitIntent, bool> request)
        {
            singleExit.DisableExit(); leftExit.DisableExit(); rightExit.DisableExit();
            if (choices.Count == 1) singleExit.Configure(player, body, choices[0], request);
            else if (choices.Count == 2)
            { leftExit.Configure(player, body, choices[0], request); rightExit.Configure(player, body, choices[1], request); }
            else if (choices.Count != 0) throw new InvalidOperationException("Unsupported forward exit count.");
        }
    }
}
