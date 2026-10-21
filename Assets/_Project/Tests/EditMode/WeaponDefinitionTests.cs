using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class WeaponDefinitionTests
    {
        [TestCase("Knife", "weapon.knife", AttackKind.Melee)]
        [TestCase("Blaster", "weapon.blaster", AttackKind.Projectile)]
        public void StableIdentityAndStaticAssetAreIndependentOfRuntimeState(string name, string id, AttackKind kind)
        {
            var asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"Assets/_Project/Data/Weapons/{name}.asset");
            Assert.That(asset, Is.Not.Null);
            var before = EditorJsonUtility.ToJson(asset);
            var identity = new PlayerIdentity(1);
            var player = new PlayerSimulation(identity, 5);
            player.SetActive(true);
            var configuration = asset.CreateConfiguration();
            var combat = new CombatSimulation(player, configuration);
            combat.TryAttack(new AttackIntent(identity, player.State.AimDirection), out _);
            combat.AdvanceTime(0.1f);
            var health = new HealthSimulation(new DamageableIdentity(1), 100);
            health.TryApplyDamage(new DamageRequest(identity, 1, health.Identity, configuration.Damage), out _);
            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
            Assert.That(configuration.Id, Is.EqualTo(id));
            Assert.That(configuration.Kind, Is.EqualTo(kind));
            Assert.That(configuration.Cooldown, Is.GreaterThan(combat.CooldownRemaining));
            var renamed = Object.Instantiate(asset);
            try
            {
                renamed.name = "An unrelated asset name";
                var data = new SerializedObject(renamed);
                data.FindProperty("displayName").stringValue = "An unrelated display name";
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(renamed.CreateConfiguration().Id, Is.EqualTo(id));
            }
            finally { Object.DestroyImmediate(renamed); }
        }
    }
}
