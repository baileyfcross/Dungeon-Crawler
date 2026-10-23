#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in standalone checks using fixture storage; never runs in an ordinary launch.</summary>
    public sealed class AudioOptionsValidationDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableFrames(){if(SessionDiagnostics.Option("--audio-options-validation")!=null)Application.runInBackground=true;}
        private ApplicationRoot root;
        private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("[AudioOptionsValidation] FAILED "+message);}
        public void Bind(ApplicationRoot application){root=application;StartCoroutine(Run());}
        private void Submit(string name)
        {
            var button=root.CurrentScreen.View.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,name+" available");
            using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
        }
        private static IEnumerator Until(Func<bool> condition)
        {double until=Time.realtimeSinceStartupAsDouble+30;while(!condition()){Check(Time.realtimeSinceStartupAsDouble<until,"flow completed");yield return null;}yield return null;}
        private IEnumerator Run()
        {
            var audio=GameAudio.Current;string mode=SessionDiagnostics.Option("--audio-options-validation");
            string folder=SessionDiagnostics.Option("--audio-evidence");Check(!string.IsNullOrEmpty(folder),"isolated evidence path");Directory.CreateDirectory(folder);
            if(mode=="reload")
            {
                Check(Mathf.Abs(audio.Preferences.Master-.42f)<.001f && Mathf.Abs(audio.Preferences.Effects-.55f)<.001f && Mathf.Abs(audio.Preferences.Ui-.33f)<.001f && !audio.Preferences.Muted,"preferences survived process restart");
                Check(root.Profile.Id.ToString("N")==File.ReadAllText(Path.Combine(folder,"profile.txt")),"profile unchanged");
                Debug.Log("[AudioOptionsValidation] RELOAD-PASS");Application.Quit(0);yield break;
            }
            File.WriteAllText(Path.Combine(folder,"profile.txt"),root.Profile.Id.ToString("N"));Submit("options");yield return null;
            Check(root.CurrentScreen.GetComponent<AudioOptionsPanel>().IsOpen && root.Flow.State==ApplicationState.MainMenu,"menu overlay");
            Check(!root.CurrentScreen.View.Q<Button>("new-game").enabledInHierarchy,"background menu blocked");
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768),new Vector2Int(1280,800)})
                yield return SteamSessionValidationDriver.CapturePanel(root.CurrentScreen,Path.Combine(folder,$"options-{size.x}x{size.y}.png"),size.x,size.y);
            audio.SetPreferences(new AudioPreferences(1,1,1,false));var samples=new float[1024];AudioListener.GetOutputData(samples,0);float peak=0;
            foreach(SoundCue cue in Enum.GetValues(typeof(SoundCue)))
            {
                int before=audio.EmittedCount(cue);GameAudio.Emit(cue);Check(audio.EmittedCount(cue)==before+1,"cue admitted: "+cue);
                double end=Time.realtimeSinceStartupAsDouble+.16;
                while(Time.realtimeSinceStartupAsDouble<end)
                {AudioListener.GetOutputData(samples,0);foreach(float sample in samples)peak=Mathf.Max(peak,Mathf.Abs(sample));yield return null;}
            }
            File.WriteAllText(Path.Combine(folder,"AudioOutput.json"),"{\"peak\":"+peak.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"voices\":"+audio.VoiceCount+"}");
            Check(peak>.00001f,"nonzero mixed audio output");Check(audio.VoiceCount==GameAudio.VoiceLimit,"bounded voice pool");
            root.CurrentScreen.View.Q<SliderInt>("audio-master").value=42;root.CurrentScreen.View.Q<SliderInt>("audio-effects").value=55;
            root.CurrentScreen.View.Q<SliderInt>("audio-ui").value=33;Submit("options-close");
            Submit("new-game");yield return Until(()=>root.CurrentGameplay!=null && (root.Flow.State==ApplicationState.Gameplay || root.Flow.State==ApplicationState.Paused));
            root.CurrentGameplay.PollLocalDevices=false;root.CurrentGameplay.Input.HandleFocus(true);root.Flow.TryRequest(ApplicationCommand.Pause);
            Submit("options");yield return null;Check(root.Flow.State==ApplicationState.Paused,"options do not resume gameplay");
            yield return SteamSessionValidationDriver.CapturePanel(root.CurrentScreen,Path.Combine(folder,"pause-options.png"),1280,720);
            Check(root.CurrentScreen.GetComponent<AudioOptionsPanel>().ConsumePause(),"cancel consumes pause");yield return null;
            Check(root.Flow.State==ApplicationState.Paused && !root.CurrentGameplay.Simulation.IsActive,"closing returns to Pause");
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);yield return Until(()=>root.Flow.State==ApplicationState.MainMenu);
            Check(root.Profile.Id.ToString("N")==File.ReadAllText(Path.Combine(folder,"profile.txt")),"options did not replace profile");
            Debug.Log("[AudioOptionsValidation] EXERCISE-PASS");Application.Quit(0);
        }
    }
}
#endif
