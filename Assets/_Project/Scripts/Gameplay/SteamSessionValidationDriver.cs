#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using SpaceCrawler.Core;
using SpaceCrawler.Platform;
using UnityEngine;
using UnityEngine.UIElements;
namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in standalone exercise using an isolated save directory and a real private lobby, without sending invitations.</summary>
    public sealed class SteamSessionValidationDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableValidationFrames()
        {if(SessionDiagnostics.Option("--steam-session-validation")!=null)Application.runInBackground=true;}
        private ApplicationRoot root;
        private static void Check(bool value,string detail){if(!value)throw new InvalidOperationException("[SteamSessionValidation] FAILED "+detail);}
        private static IEnumerator Until(Func<bool> condition,string detail)
        {double until=Time.realtimeSinceStartupAsDouble+35;while(!condition()){Check(Time.realtimeSinceStartupAsDouble<until,detail);yield return null;}yield return null;}
        private void Submit(string name)
        {var button=root.CurrentScreen.View.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,name+" available");using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);SessionDiagnostics.ConfirmNewRun(root.CurrentScreen);}}
        public void Bind(ApplicationRoot application){root=application;StartCoroutine(Run());}
        internal static IEnumerator CapturePanel(ApplicationScreen screen,string path,int width,int height)
        {
            var settings=screen.GetComponent<UIDocument>().panelSettings;
            var previous=settings.targetTexture;var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
            Texture2D pixels=null;var active=RenderTexture.active;
            try
            {
                settings.targetTexture=target;
                for(int frame=0;frame<12;frame++){screen.View.MarkDirtyRepaint();yield return null;}
                var bounds=screen.View.worldBound;
                screen.View.Query<Button>().ForEach(button=>
                {
                    var box=button.worldBound;
                    if(box.width<1 || box.height<1)return;
                    Check(box.xMin>=bounds.xMin-1 && box.yMin>=bounds.yMin-1 && box.xMax<=bounds.xMax+1 && box.yMax<=bounds.yMax+1,"button fits panel: "+button.name);
                });
                RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {RenderTexture.active=active;settings.targetTexture=previous;RenderTexture.ReleaseTemporary(target);if(pixels!=null)Destroy(pixels);}
        }
        private IEnumerator Run()
        {
            string mode=SessionDiagnostics.Option("--steam-session-validation");
            if(mode=="classes")
            {
                root.CurrentScreen.OpenRunSetup(()=>root.Flow.TryRequest(ApplicationCommand.NewGame),true,"New Game");
                Check(!root.CurrentScreen.View.Q<Button>("start-run").enabledInHierarchy,"class required before starting");
                root.CurrentScreen.View.Q<DropdownField>("player-class").value="Logistics Manager";
                root.CurrentScreen.View.Q<DropdownField>("run-difficulty").value="Hard";
                string capture=SessionDiagnostics.Option("--steam-screenshot");
                if(capture!=null)
                    foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768),new Vector2Int(1280,800)})
                        yield return CapturePanel(root.CurrentScreen,capture+$".{size.x}x{size.y}.png",size.x,size.y);
                Submit("start-run");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"class-selected run starts");
                Check(root.CurrentGameplay.Life.Capture().Classes[0]=="logistics-manager" && root.CurrentGameplay.Difficulty==RunDifficulty.Hard,"selected class and difficulty applied");
                Check(root.CurrentGameplay.Life[0].Maximum==100 && root.CurrentGameplay.Life.RunXp==0,"no class stat or XP changes");
                root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"saved class run");
                Submit("continue");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"class Continue");
                Check(root.CurrentGameplay.Life.Capture().Classes[0]=="logistics-manager" && root.CurrentGameplay.Difficulty==RunDifficulty.Hard,"Continue preserves options");
                Debug.Log("[SteamSessionValidation] CLASSES-PASS");Application.Quit(0);yield break;
            }
            Check(root.Profile.Experience==15,"fixture profile XP preserved");Guid profile=root.Profile.Id;
            if(mode=="menu" || mode=="presentation")
            {
                Check(root.SteamReady && root.SteamProfileAssociated,"Steam profile on graphical startup");
                string capture=SessionDiagnostics.Option("--steam-screenshot");
                if(capture!=null)
                {
                    // Offscreen panels let hidden validation windows produce inspectable UI evidence.
                    foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768),new Vector2Int(1280,800)})
                        yield return CapturePanel(root.CurrentScreen,capture+$".{size.x}x{size.y}.png",size.x,size.y);
                }
                Check(root.CurrentScreen.View.Q("local-session-controls").resolvedStyle.display==DisplayStyle.None,"localhost controls hidden without opt-in");
                if(mode=="presentation")
                {
                    Submit("continue");yield return Until(()=>root.CurrentGameplay!=null && (root.Flow.State==ApplicationState.Gameplay || root.Flow.State==ApplicationState.Paused),"Continue for HUD capture");
                    root.CurrentGameplay.PollLocalDevices=false;root.CurrentGameplay.Input.HandleFocus(true);root.Flow.TryRequest(ApplicationCommand.Resume);
                    yield return null;
                    Check(!root.CurrentScreen.View.Q<Label>("run-readout").text.Contains("Seed"),"normal HUD omits generation diagnostics");
                    if(capture!=null)yield return CapturePanel(root.CurrentScreen,capture+".gameplay.png",1280,720);
                    root.Flow.TryRequest(ApplicationCommand.Pause);yield return null;
                    if(capture!=null)yield return CapturePanel(root.CurrentScreen,capture+".pause.png",1280,720);
                    root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"return from presentation checks");
                    Debug.Log("[SteamSessionValidation] PRESENTATION-PASS");Application.Quit(0);yield break;
                }
                Debug.Log("[SteamSessionValidation] MENU-PASS");Application.Quit(0);yield break;
            }
            if(mode=="reload" || mode=="offline")
            {
                Check(mode=="offline" ? !root.SteamReady : root.SteamReady && root.SteamProfileAssociated,"account association after restart");
                Check(root.Flow.ContinueAvailable,"solo Continue retained");Submit("continue");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"Continue");
                Check(root.CurrentGameplay.Life.Capture().Profiles[0]==profile,"profile ownership on restored run");
                Debug.Log("[SteamSessionValidation] RELOAD-PASS");Application.Quit(0);yield break;
            }
            Check(root.SteamReady,"Steam initialization");Check(!root.SteamProfileAssociated,"association is not automatic");
            Submit("steam-associate");yield return null;Check(root.SteamProfileAssociated && root.Profile.Id==profile,"explicit profile link");
            Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"solo game");
            var run=root.CurrentGameplay.Life.RunId;root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
            yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"solo suspension");
            Check(root.Flow.ContinueAvailable,"solo checkpoint exists before hosting");
            for(int attempt=0;attempt<2;attempt++)
            {
                Submit("steam-host");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay && root.CurrentMultiplayer!=null,"Steam host start");
                var session=root.CurrentMultiplayer;
                Check(session.Network.NetworkConfig.NetworkTransport is LobbySteamTransport,"real Steam transport selected");
                Check(root.SteamSessionActive && !session.RoomReady && !session.Authority.BothConnected,"host waits for a real partner");
                yield return new WaitForSeconds(.5f);Check(session.Life[0].Current==100,"waiting host simulation is gated");
                root.CancelSteamSession();yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"host cancellation");
                Check(!root.SteamSessionActive && root.SteamReady,"lobby cleanup preserves application Steam lifetime");
                Check(root.SavedCheckpoint?.Life.RunId==run && root.SavedCheckpoint.Mode==SavedSessionMode.Solo,"waiting host did not replace the existing save");
            }
            Submit("continue");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"preserved solo Continue");
            Check(root.CurrentGameplay.Life.RunId==run && root.Profile.Experience==15,"run and permanent XP retained");
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"final menu");
            Debug.Log("[SteamSessionValidation] HOST-PASS");Application.Quit(0);
        }
    }
}
#endif
