using System;
using System.IO;
using System.Linq;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    /// <summary>Authors the approved primitive combat fixture without changing runtime equipment in play.</summary>
    public static class CombatAssetTools
    {
        private const string Root = ApplicationAssetTools.Root;
        public const string FixturePath = Root + "/Prefabs/Gameplay/PlayerTestEnvironment.prefab";
        public const string KnifePath = Root + "/Data/Weapons/Knife.asset";
        public const string BlasterPath = Root + "/Data/Weapons/Blaster.asset";
        private const string ProjectilePath = Root + "/Prefabs/Combat/BlasterProjectile.prefab";
        private const string TargetPath = Root + "/Prefabs/Combat/DamageableTestTarget.prefab";

        [MenuItem("Tools/Space Crawler/Combat/Create Combat Fixture")]
        public static void CreateCombatFixture()
        {
            RequireIdle();
            EnsureFolder(Root + "/Data/Weapons");
            EnsureFolder(Root + "/Prefabs/Combat");
            var knife = CreateWeapon(KnifePath, "weapon.knife", "Knife", AttackKind.Melee, 25, 0.5f);
            CreateWeapon(BlasterPath, "weapon.blaster", "Blaster", AttackKind.Projectile, 20, 0.3f);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Art/Materials/ControlsUnlit.mat");
            if (material == null) throw new InvalidOperationException("Preserve and create the player controls fixture first.");
            CreateProjectile(material);
            CreateTarget(material);
            var contents = PrefabUtility.LoadPrefabContents(FixturePath);
            try
            {
                var session = contents.GetComponent<GameplaySession>();
                if (session.Combat != null) return;
                var resolver = contents.AddComponent<CombatResolver>();
                var arcObject = new GameObject("Accepted melee arc");
                arcObject.transform.SetParent(contents.transform, false);
                var arc = arcObject.AddComponent<LineRenderer>();
                arc.sharedMaterial = material;
                arc.useWorldSpace = true;
                arc.widthMultiplier = 0.025f;
                arc.startColor = arc.endColor = new Color(1, 0.76f, 0.42f);
                arc.sortingOrder = 5;
                arc.positionCount = 0;
                arc.enabled = false;
                var presentation = arcObject.AddComponent<CombatPresentation>();
                SetReference(presentation, "meleeArc", arc);
                var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
                var targets = new DamageableTarget[3];
                var positions = new[] { new Vector2(1.6f, 0), new Vector2(-1.6f, 0), new Vector2(3.2f, 0) };
                for (var i = 0; i < targets.Length; i++)
                {
                    var targetObject = (GameObject)PrefabUtility.InstantiatePrefab(targetPrefab, contents.transform);
                    targetObject.name = "Test target " + (i + 1);
                    targetObject.transform.localPosition = positions[i];
                    targets[i] = targetObject.GetComponent<DamageableTarget>();
                    var targetData = new SerializedObject(targets[i]);
                    targetData.FindProperty("identity").intValue = i + 1;
                    targetData.ApplyModifiedPropertiesWithoutUndo();
                }
                SetReference(resolver, "projectilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath).GetComponent<CombatProjectile>());
                SetReference(resolver, "presentation", presentation);
                SetArray(resolver, "targets", targets);
                SetArray(resolver, "ownerColliders", session.Motor.GetComponentsInChildren<Collider2D>());
                SetReference(session, "startingWeapon", knife);
                SetReference(session, "combat", resolver);
                PrefabUtility.SaveAsPrefabAsset(contents, FixturePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); AssetDatabase.SaveAssets(); }
        }

        [MenuItem("Tools/Space Crawler/Combat/Use Knife Default")]
        public static void UseKnifeDefault() => SetDefault(KnifePath);
        [MenuItem("Tools/Space Crawler/Combat/Use Blaster Default")]
        public static void UseBlasterDefault() => SetDefault(BlasterPath);

        private static void SetDefault(string path)
        {
            RequireIdle();
            foreach (var fixture in new[] { FixturePath, RoomAssetTools.SessionPath })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(fixture) == null) continue;
                var contents = PrefabUtility.LoadPrefabContents(fixture);
                try
                {
                    SetReference(contents.GetComponent<GameplaySession>(), "startingWeapon", AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path));
                    PrefabUtility.SaveAsPrefabAsset(contents, fixture);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            AssetDatabase.SaveAssets();
        }

        private static WeaponDefinition CreateWeapon(string path, string id, string displayName, AttackKind kind, int damage, float cooldown)
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (definition != null) return definition;
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            var data = new SerializedObject(definition);
            data.FindProperty("stableId").stringValue = id;
            data.FindProperty("displayName").stringValue = displayName;
            data.FindProperty("attackKind").enumValueIndex = (int)kind;
            data.FindProperty("damage").intValue = damage;
            data.FindProperty("cooldown").floatValue = cooldown;
            data.FindProperty("meleeRange").floatValue = 2;
            data.FindProperty("meleeArcDegrees").floatValue = 90;
            data.FindProperty("projectileSpeed").floatValue = 9;
            data.FindProperty("projectileLifetime").floatValue = 2;
            data.ApplyModifiedPropertiesWithoutUndo();
            definition.CreateConfiguration();
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        private static void CreateProjectile(Material material)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath) != null) return;
            var temporary = new GameObject("Blaster Projectile", typeof(CombatProjectile));
            try
            {
                SpriteChild("Projectile primitive", temporary.transform, "Circle", Vector2.zero, Vector2.one * 0.16f, new Color(0.75f, 1, 0.5f), 6, material);
                PrefabUtility.SaveAsPrefabAsset(temporary, ProjectilePath);
            }
            finally { Object.DestroyImmediate(temporary); }
        }

        private static void CreateTarget(Material material)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath) != null) return;
            var temporary = new GameObject("Damageable Test Target");
            try
            {
                var circle = temporary.AddComponent<CircleCollider2D>();
                circle.radius = 0.4f;
                circle.isTrigger = true;
                // Two hit shapes deliberately exercise per-target melee de-duplication.
                var box = temporary.AddComponent<BoxCollider2D>();
                box.size = Vector2.one * 0.45f;
                box.isTrigger = true;
                var body = SpriteChild("Target primitive", temporary.transform, "Circle", Vector2.zero, Vector2.one * 0.8f, new Color(0.96f, 0.42f, 0.32f), 2, material);
                SpriteChild("Health background", temporary.transform, "Square", new Vector2(0, 0.65f), new Vector2(0.9f, 0.12f), new Color(0.2f, 0.26f, 0.32f), 3, material);
                var fill = SpriteChild("Health remaining", temporary.transform, "Square", new Vector2(0, 0.65f), new Vector2(0.8f, 0.08f), new Color(0.45f, 0.9f, 0.65f), 4, material);
                var presentation = temporary.AddComponent<TargetPresentation>();
                SetReference(presentation, "healthFill", fill.transform);
                SetReference(presentation, "targetSprite", body);
                var target = temporary.AddComponent<DamageableTarget>();
                SetReference(target, "presentation", presentation);
                SetArray(target, "hitColliders", new Collider2D[] { circle, box });
                PrefabUtility.SaveAsPrefabAsset(temporary, TargetPath);
            }
            finally { Object.DestroyImmediate(temporary); }
        }

        private static SpriteRenderer SpriteChild(string name, Transform parent, string shape, Vector2 position, Vector2 size, Color color, int order, Material material)
        {
            var sprite = AssetDatabase.LoadAllAssetsAtPath("Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/" + shape + ".png").OfType<Sprite>().First();
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order; renderer.sharedMaterial = material;
            return renderer;
        }

        private static void SetReference(Object target, string property, Object value)
        {
            if (value == null) throw new InvalidOperationException("Missing " + property);
            var data = new SerializedObject(target);
            data.FindProperty(property).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArray(Object target, string property, Object[] values)
        {
            var data = new SerializedObject(target);
            var array = data.FindProperty(property);
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Use fixture authoring only in idle Edit Mode.");
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
