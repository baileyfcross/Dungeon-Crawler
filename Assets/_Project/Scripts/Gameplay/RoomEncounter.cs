using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [Serializable]
    public sealed class EnemySpawn
    {
        [SerializeField] private int identity;
        [SerializeField] private DamageableTarget prefab;
        [SerializeField] private Transform anchor;
        [SerializeField] private MeleeEnemyDefinition behaviour;
        public int Identity => identity;
        public DamageableTarget Prefab => prefab;
        public Transform Anchor => anchor;
        public MeleeEnemyDefinition Behaviour => behaviour;
        public EnemySpawn(int identity, DamageableTarget prefab, Transform anchor, MeleeEnemyDefinition behaviour = null)
        { this.identity = identity; this.prefab = prefab; this.anchor = anchor; this.behaviour = behaviour; }
    }

    /// <summary>Bounded authored spawn slots; orchestration restores their saved state.</summary>
    public sealed class RoomEncounter
    {
        private readonly List<EnemyActor> enemies = new List<EnemyActor>();
        private readonly IReadOnlyList<EnemyActor> view;
        public IReadOnlyList<EnemyActor> Enemies => view;
        public RoomEncounter(Transform root, IReadOnlyList<EnemySpawn> spawns, bool authoritative, uint incarnation, RunDifficulty difficulty = RunDifficulty.Legacy)
        {
            if (root == null || spawns == null || spawns.Count > 32 || incarnation == 0)
                throw new ArgumentException("Invalid encounter authoring.");
            var ids = new HashSet<int>();
            foreach (var spawn in spawns)
            {
                if (spawn == null || spawn.Identity < 1 || spawn.Identity > 32 || !ids.Add(spawn.Identity)
                    || spawn.Prefab == null || spawn.Prefab.Health != null || spawn.Anchor == null
                    || !spawn.Anchor.IsChildOf(root) || !spawn.Prefab.gameObject.activeSelf
                    || spawn.Prefab.HitColliders == null || spawn.Prefab.HitColliders.Length == 0)
                    throw new ArgumentException("Invalid or duplicate enemy spawn slot.");
                spawn.Prefab.ValidateEnemyAuthoring();
                if (spawn.Behaviour != null) spawn.Behaviour.CreateConfiguration();
                if (float.IsNaN(spawn.Anchor.position.sqrMagnitude) || float.IsInfinity(spawn.Anchor.position.sqrMagnitude))
                    throw new ArgumentException("Enemy spawn coordinates must be finite.");
            }
            view = enemies.AsReadOnly();
            try
            {
                foreach (var spawn in spawns)
                {
                    var target = UnityEngine.Object.Instantiate(spawn.Prefab, spawn.Anchor.position, spawn.Anchor.rotation, root);
                    target.ConfigureEnemyHealth(DifficultyRules.Scale(target.InitialHealth,difficulty));
                    var actor = target.gameObject.AddComponent<EnemyActor>();
                    enemies.Add(actor);
                    actor.Bind(target, new EnemyIdentity(incarnation, spawn.Identity), authoritative);
                    if (spawn.Behaviour != null) actor.ConfigureMelee(spawn.Behaviour,difficulty);
                }
            }
            catch { Retire(); foreach (var actor in enemies) if (actor != null) UnityEngine.Object.Destroy(actor.gameObject); throw; }
        }
        public void Retire() { foreach (var enemy in enemies) if (enemy != null) enemy.Retire(); }
    }
}
