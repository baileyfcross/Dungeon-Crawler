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
        {var button=root.CurrentScreen.View.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,name+" available");using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
        public void Bind(ApplicationRoot application){root=application;StartCoroutine(Run());}
        private IEnumerator Run()
        {
            string mode=SessionDiagnostics.Option("--steam-session-validation");
            Check(root.Profile.Experience==15,"fixture profile XP preserved");Guid profile=root.Profile.Id;
            if(mode=="menu")
            {
                Check(root.SteamReady && root.SteamProfileAssociated,"Steam profile on graphical startup");
                string capture=SessionDiagnostics.Option("--steam-screenshot");
                if(capture!=null)
                {
                    // Render the application panel offscreen: hidden validation windows may have a black swapchain.
                    Application.runInBackground=true;
                    var settings=root.CurrentScreen.GetComponent<UIDocument>().panelSettings;
                    var previous=settings.targetTexture;
                    var target=RenderTexture.GetTemporary(1920,1080,0,RenderTextureFormat.ARGB32);
                    settings.targetTexture=target;
                    for(int frame=0;frame<12;frame++){root.CurrentScreen.View.MarkDirtyRepaint();yield return null;}
                    var active=RenderTexture.active;RenderTexture.active=target;
                    var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                    pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                    File.WriteAllBytes(capture,pixels.EncodeToPNG());
                    RenderTexture.active=active;settings.targetTexture=previous;RenderTexture.ReleaseTemporary(target);Destroy(pixels);
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
