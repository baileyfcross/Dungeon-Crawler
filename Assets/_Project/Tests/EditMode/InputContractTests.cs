using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class InputContractTests
    {
        [Test]
        public void CanonicalInputImportsWithUniqueActionAndBindingIdentitiesAndOneReviveAction()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            Assert.That(asset, Is.Not.Null, "The canonical asset must import before any player can bind input.");
            var actionIds = new HashSet<System.Guid>(); var bindingIds = new HashSet<System.Guid>();
            foreach (var map in asset.actionMaps)
            {
                Assert.That(map.actions.Select(a => a.name).Distinct().Count(), Is.EqualTo(map.actions.Count));
                foreach (var action in map.actions) Assert.That(actionIds.Add(action.id), Is.True);
                foreach (var binding in map.bindings) Assert.That(bindingIds.Add(binding.id), Is.True);
            }
            var interact = asset.FindActionMap("Player", true).FindAction("Interact", true);
            Assert.That(interact.type, Is.EqualTo(InputActionType.Button));
            Assert.That(interact.bindings.Select(b => b.path), Is.EquivalentTo(new[] { "<Keyboard>/e", "<Gamepad>/buttonSouth" }));
        }
    }
}
