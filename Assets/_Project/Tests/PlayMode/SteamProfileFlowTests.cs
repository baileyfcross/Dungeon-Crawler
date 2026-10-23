#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using SpaceCrawler.Platform;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class SteamProfileFlowTests
    {
        private sealed class Api : ISteamClientApi
        {
            public uint AppId=>SteamClientService.ExpectedAppId;
            public ulong AccountId{get;set;}=111;
            public bool LoggedOn{get;set;}=true;
            public bool Initialize(out string error){error=null;return true;}
            public void Pump(){}public void Shutdown(){}
        }
        private ApplicationRoot root;private string previous,path;private Api api;
        private static IEnumerator Until(Func<bool> condition)
        {double until=Time.realtimeSinceStartupAsDouble+15;while(!condition()){Assert.That(Time.realtimeSinceStartupAsDouble,Is.LessThan(until));yield return null;}yield return null;}
        [UnitySetUp] public IEnumerator SetUp()
        {
            previous=ApplicationRoot.SaveDirectoryOverride;path=Path.GetFullPath(Path.Combine("Logs/Validation/Accounts/Flow",Guid.NewGuid().ToString("N")));ApplicationRoot.SaveDirectoryOverride=path;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity");
            yield return Until(()=>(root=Object.FindFirstObjectByType<ApplicationRoot>())!=null && root.Flow.State==ApplicationState.MainMenu);
            api=new Api();var steam=new SteamClientService(api);Assert.That(steam.TryStart(),Is.True);
            typeof(ApplicationRoot).GetField("steam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(root,steam);yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if(root!=null)Object.Destroy(root.gameObject);
            foreach(var session in Object.FindObjectsByType<GameplaySession>(FindObjectsSortMode.None))Object.Destroy(session.gameObject);
            foreach(var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None))Object.Destroy(screen.gameObject);
            ApplicationRoot.SaveDirectoryOverride=previous;yield return null;
        }
        private void Submit(string name)
        {var button=root.CurrentScreen.View.Q<Button>(name);Assert.That(button,Is.Not.Null);Assert.That(button.enabledInHierarchy,Is.True);using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
        [UnityTest] public IEnumerator LinkingRequiresAnExplicitMenuActionAndPreservesTheProfile()
        {
            Guid id=root.Profile.Id;Assert.That(root.SteamProfileAssociated,Is.False);
            Assert.That(root.CurrentScreen.View.Q<Button>("steam-host").enabledSelf,Is.False);
            Submit("steam-associate");yield return null;
            Assert.That(root.SteamProfileAssociated,Is.True);Assert.That(root.Profile.Id,Is.EqualTo(id));
            Assert.That(Directory.GetDirectories(Path.Combine(path,"AccountBackups")).Length,Is.EqualTo(1));
            Assert.That(root.CurrentScreen.View.Q<Button>("steam-host").enabledSelf,Is.True);
            Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay);
            Assert.That(root.CurrentGameplay,Is.Not.Null);Assert.That(root.CurrentMultiplayer,Is.Null);
        }
        [UnityTest] public IEnumerator ActiveRunInvitationNeverLeavesOrChangesThePlayersProfileAndAccountSwitchPausesSafely()
        {
            Assert.That(root.AssociateSteamProfile(),Is.True);Guid profile=root.Profile.Id;
            Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay);
            root.CurrentGameplay.PollLocalDevices=false;root.CurrentGameplay.Input.HandleFocus(true);root.Flow.TryRequest(ApplicationCommand.Resume);
            Guid run=root.CurrentGameplay.Life.RunId;var game=root.CurrentGameplay;
            typeof(ApplicationRoot).GetMethod("SteamInvitation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root,new object[]{7ul});yield return null;
            Assert.That(root.CurrentGameplay,Is.SameAs(game));Assert.That(root.CurrentGameplay.Life.RunId,Is.EqualTo(run));
            Assert.That(root.HasSteamInvitation,Is.True);StringAssert.Contains("Return to Main Menu",root.SteamStatus);
            api.LoggedOn=false;yield return new WaitForSeconds(1.1f);
            Assert.That(root.Profile.Id,Is.EqualTo(profile));Assert.That(root.CanResumeCurrentRun,Is.True,"Network offline does not switch the account profile.");
            Assert.That(root.CurrentGameplay,Is.SameAs(game));
            api.LoggedOn=true;api.AccountId=222;yield return Until(()=>!root.SteamReady);
            Assert.That(root.Flow.State,Is.EqualTo(ApplicationState.Paused));Assert.That(root.Profile.Id,Is.EqualTo(profile));
            Assert.That(root.CurrentGameplay,Is.SameAs(game));Assert.That(root.HostSteam(false),Is.False);
            Assert.That(root.CurrentScreen.View.Q<Button>("resume").enabledSelf,Is.False);
            root.Flow.TryRequest(ApplicationCommand.Resume);yield return null;
            Assert.That(game.Simulation.IsActive,Is.False,"Account identity gate survives a later flow Resume request.");
        }
    }
}
#endif
