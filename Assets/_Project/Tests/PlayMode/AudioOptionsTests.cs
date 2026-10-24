#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class AudioOptionsTests
    {
        private ApplicationRoot root;private string path,previous;
        [UnitySetUp] public IEnumerator SetUp()
        {
            previous=ApplicationRoot.SaveDirectoryOverride;path=Path.GetFullPath(Path.Combine("Logs/Validation/Audio/Flow",Guid.NewGuid().ToString("N")));ApplicationRoot.SaveDirectoryOverride=path;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity");
            yield return Until(()=>(root=Object.FindFirstObjectByType<ApplicationRoot>())!=null && root.Flow.State==ApplicationState.MainMenu);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if(root!=null)Object.Destroy(root.gameObject);
            foreach(var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None))Object.Destroy(screen.gameObject);
            foreach(var game in Object.FindObjectsByType<GameplaySession>(FindObjectsSortMode.None))Object.Destroy(game.gameObject);
            ApplicationRoot.SaveDirectoryOverride=previous;yield return null;
        }
        private static IEnumerator Until(Func<bool> condition)
        {double until=Time.realtimeSinceStartupAsDouble+15;while(!condition()){Assert.That(Time.realtimeSinceStartupAsDouble,Is.LessThan(until));yield return null;}yield return null;}
        private void Submit(string name)
        {
            var button=root.CurrentScreen.View.Q<Button>(name);Assert.That(button,Is.Not.Null);Assert.That(button.enabledInHierarchy,Is.True,name);
            using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);SessionDiagnostics.ConfirmNewRun(root.CurrentScreen);}
        }
        [UnityTest] public IEnumerator MenuAndPauseOptionsPersistAndCannotResumeOrTriggerUnderlyingButtons()
        {
            var audio=GameAudio.Current;var profile=root.Profile.Id;Submit("options");yield return null;
            var panel=root.CurrentScreen.GetComponent<AudioOptionsPanel>();Assert.That(panel.IsOpen,Is.True);Assert.That(root.Flow.State,Is.EqualTo(ApplicationState.MainMenu));
            Assert.That(root.CurrentScreen.View.Q<Button>("new-game").enabledInHierarchy,Is.False);
            root.CurrentScreen.View.Q<SliderInt>("audio-master").value=27;root.CurrentScreen.View.Q<SliderInt>("audio-effects").value=43;
            root.CurrentScreen.View.Q<Toggle>("audio-mute").value=true;Submit("options-close");yield return null;
            Assert.That(new AudioPreferencesStore(Path.Combine(path,"DeviceSettings")).Load(out _).Master,Is.EqualTo(.27f));Assert.That(root.Profile.Id,Is.EqualTo(profile));
            Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay);
            root.CurrentGameplay.PollLocalDevices=false;root.CurrentGameplay.Input.HandleFocus(true);root.Flow.TryRequest(ApplicationCommand.Pause);
            Submit("options");yield return null;panel=root.CurrentScreen.GetComponent<AudioOptionsPanel>();Assert.That(panel.IsOpen,Is.True);
            using(var cancel=NavigationCancelEvent.GetPooled()){cancel.target=root.CurrentScreen.View.Q("options-overlay");root.CurrentScreen.View.Q("options-overlay").SendEvent(cancel);}
            Assert.That(panel.ConsumePause(),Is.True,"The same Escape cannot close Options and resume gameplay.");
            yield return null;Assert.That(root.Flow.State,Is.EqualTo(ApplicationState.Paused));Assert.That(root.CurrentGameplay.Simulation.IsActive,Is.False);
            Assert.That(root.CurrentScreen.View.Q<Button>("resume").enabledInHierarchy,Is.True);
            Submit("options");Submit("audio-defaults");Assert.That(audio.Preferences.Muted,Is.False);Assert.That(audio.Preferences.Master,Is.EqualTo(.75f));Submit("options-close");
        }
        [UnityTest] public IEnumerator ReplicatedCountersHealthAndRestoredEnemiesDoNotReplayHistoricalSounds()
        {
            var audio=GameAudio.Current;
            var prefab=AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab");
            var serialized=new SerializedObject(prefab);var rigPrefab=(NetworkPlayerRig)serialized.FindProperty("playerPrefab").objectReferenceValue;
            var rig=Object.Instantiate(rigPrefab);var definition=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Knife.asset");
            try
            {
                rig.Bind(new PlayerIdentity(1),false,false,definition.CreateConfiguration());
                var snapshot=new PlayerStateSnapshot{Player=1,Health=75,MaximumHealth=100,LifeState=(int)PlayerLifeState.Alive,Aim=Vector2.right,AttackAim=Vector2.right,Attacks=5};
                int swings=audio.EmittedCount(SoundCue.KnifeSwing),hits=audio.EmittedCount(SoundCue.PlayerHit);
                rig.Apply(snapshot);rig.Apply(snapshot);Assert.That(audio.EmittedCount(SoundCue.KnifeSwing),Is.EqualTo(swings));Assert.That(audio.EmittedCount(SoundCue.PlayerHit),Is.EqualTo(hits));
                snapshot.Attacks=6;snapshot.Health=50;rig.Apply(snapshot);rig.Apply(snapshot);
                Assert.That(audio.EmittedCount(SoundCue.KnifeSwing),Is.EqualTo(swings+1));Assert.That(audio.EmittedCount(SoundCue.PlayerHit),Is.EqualTo(hits+1));
                var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/DamageableTestTarget.prefab"));
                try
                {
                    int before=audio.EmittedCount(SoundCue.Hit);target.PresentAuthoritativeHealth(100,100);target.PresentAuthoritativeHealth(75,100,true);target.PresentAuthoritativeHealth(75,100);
                    Assert.That(audio.EmittedCount(SoundCue.Hit),Is.EqualTo(before));target.PresentAuthoritativeHealth(50,100);target.PresentAuthoritativeHealth(50,100);
                    Assert.That(audio.EmittedCount(SoundCue.Hit),Is.EqualTo(before+1));
                }
                finally{Object.Destroy(target.gameObject);}
            }
            finally{Object.Destroy(rig.gameObject);}
            yield return null;
        }
        [UnityTest] public IEnumerator MuteChannelGainsAndVoiceBudgetApplyWithoutAffectingGameplay()
        {
            var audio=GameAudio.Current;audio.SetPreferences(new AudioPreferences(1,0,1,false));
            int effect=audio.EmittedCount(SoundCue.BlasterShot),ui=audio.EmittedCount(SoundCue.UiActivate);
            GameAudio.Emit(SoundCue.BlasterShot);GameAudio.Emit(SoundCue.UiActivate);
            Assert.That(audio.EmittedCount(SoundCue.BlasterShot),Is.EqualTo(effect));Assert.That(audio.EmittedCount(SoundCue.UiActivate),Is.EqualTo(ui+1));
            audio.SetPreferences(new AudioPreferences(1,1,1,true));GameAudio.Emit(SoundCue.UiActivate);Assert.That(audio.EmittedCount(SoundCue.UiActivate),Is.EqualTo(ui+1));
            audio.SetPreferences(new AudioPreferences(1,1,1,false));for(int i=0;i<100;i++)GameAudio.Emit(SoundCue.BlasterShot);
            Assert.That(audio.VoiceCount,Is.EqualTo(GameAudio.VoiceLimit));Assert.That(audio.GetComponentsInChildren<AudioSource>().Length,Is.EqualTo(GameAudio.VoiceLimit));
            Assert.That(root.Flow.State,Is.EqualTo(ApplicationState.MainMenu));yield return null;
        }
    }
}
#endif
