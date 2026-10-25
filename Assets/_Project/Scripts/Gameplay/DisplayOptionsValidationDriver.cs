#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in Windows display exercise with isolated settings. Does not run on normal launches.</summary>
    public sealed class DisplayOptionsValidationDriver : MonoBehaviour
    {
        private ApplicationRoot root;
        private GameDisplay display;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableFrames(){if(SessionDiagnostics.Option("--display-validation")!=null)Application.runInBackground=true;}
        public void Bind(ApplicationRoot owner){root=owner;display=owner.GetComponent<GameDisplay>();StartCoroutine(Run());}
        private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[DisplayValidation] FAILED "+message);}
        private static IEnumerator Until(Func<bool> condition,string message)
        {double end=Time.realtimeSinceStartupAsDouble+30;while(!condition()){Check(Time.realtimeSinceStartupAsDouble<end,message);yield return null;}yield return null;}
        private void Submit(string name)
        {
            var button=root.CurrentScreen.View.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,name+" enabled");
            using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
        }
        private void Draft(DisplayPreferences value)
        {
            root.CurrentScreen.View.Q<DropdownField>("display-mode").value=value.Mode.ToString();
            root.CurrentScreen.View.Q<DropdownField>("display-resolution").value=$"{value.Width} × {value.Height}";
        }
        [Serializable]private sealed class WindowState {public int width,height,mode,requested,confirmed;public bool focused,pending,canKeep;public double time;}
        private IEnumerator Run()
        {
            string mode=SessionDiagnostics.Option("--display-validation"),folder=SessionDiagnostics.Option("--display-evidence");
            Directory.CreateDirectory(folder);Check(display.Device.CanApply,"real Windows display adapter");
            var store=new DisplayPreferencesStore(Path.Combine(SessionDiagnostics.Option("--save-directory"),"DeviceSettings"));
            if(mode=="focused-fullscreen")
            {
                yield return Until(()=>Application.isFocused,"foreground player has focus");
                var initial=display.Device.Current;Submit("options");Submit("options-display");yield return null;
                var focusedNative=display.Device.Defaults;Draft(new DisplayPreferences(focusedNative.Width,focusedNative.Height,DisplayMode.Fullscreen));Submit("display-apply");
                yield return Until(()=>Application.isFocused && display.Change.CanKeep || !display.Change.Pending,"focused exclusive mode readback or safe rejection");
                if(!display.Change.Pending)
                {
                    yield return Until(()=>display.Device.Current.Equals(initial),"rejected fullscreen restored original window");
                    Check(display.Change.Notice.Contains("Fullscreen is unavailable"),"unsupported fullscreen explained");
                    File.WriteAllText(Path.Combine(folder,"FullscreenState.json"),JsonUtility.ToJson(new WindowState{width=display.Device.Current.Width,height=display.Device.Current.Height,mode=(int)display.Device.Current.Mode,focused=Application.isFocused,pending=false,confirmed=(int)display.Change.Confirmed.Mode}));
                    Debug.Log("[DisplayValidation] FOREGROUND-FULLSCREEN-UNAVAILABLE-SAFE");Application.Quit(0);yield break;
                }
                double stableUntil=Time.realtimeSinceStartupAsDouble+2;
                while(Time.realtimeSinceStartupAsDouble<stableUntil)
                {Check(Application.isFocused && display.Change.CanKeep && display.Device.Current.Mode==DisplayMode.Fullscreen,"fullscreen stays active while focused");yield return null;}
                Submit("display-keep");yield return null;
                Check(display.Change.Confirmed.Mode==DisplayMode.Fullscreen && !display.Change.Pending,"fullscreen Keep saved");
                File.WriteAllText(Path.Combine(folder,"FullscreenState.json"),JsonUtility.ToJson(new WindowState{
                    width=display.Device.Current.Width,height=display.Device.Current.Height,mode=(int)display.Device.Current.Mode,
                    focused=Application.isFocused,pending=display.Change.Pending,confirmed=(int)display.Change.Confirmed.Mode,time=Time.realtimeSinceStartupAsDouble}));
                Check(display.Preview(initial),"restore original window after fullscreen exercise");yield return Until(()=>display.Change.CanKeep,"original window restored");Check(display.Keep(),"original window saved after exercise");
                Debug.Log("[DisplayValidation] FOREGROUND-FULLSCREEN-PASS");Application.Quit(0);yield break;
            }
            if(mode=="foreground")
            {
                double end=Time.realtimeSinceStartupAsDouble+240;
                while(Time.realtimeSinceStartupAsDouble<end)
                {
                    var value=display.Device.Current;
                    File.WriteAllText(Path.Combine(folder,"WindowState.json"),JsonUtility.ToJson(new WindowState{
                        width=value.Width,height=value.Height,mode=(int)value.Mode,focused=Application.isFocused,pending=display.Change.Pending,
                        canKeep=display.Change.CanKeep,requested=(int)(display.Change.Requested?.Mode ?? value.Mode),confirmed=(int)display.Change.Confirmed.Mode,time=Time.realtimeSinceStartupAsDouble}));
                    yield return new WaitForSecondsRealtime(.25f);
                }
                Application.Quit(0);yield break;
            }
            if(mode=="seed-unavailable")
            {store.Save(new DisplayPreferences(16384,16384,DisplayMode.Fullscreen));Debug.Log("[DisplayValidation] SEEDED-PASS");Application.Quit(0);yield break;}
            if(mode=="fallback")
            {
                yield return Until(()=>display.Device.Current.Equals(display.Device.Defaults),"unavailable mode safe fallback");
                Check(display.Change.Notice.Contains("unavailable"),"fallback explained");Debug.Log("[DisplayValidation] FALLBACK-PASS");Application.Quit(0);yield break;
            }
            if(mode=="reload" || mode=="after-crash")
            {
                var saved=store.Load(out _);yield return Until(()=>display.Device.Current.Equals(saved),"confirmed settings restored on restart");
                Check(root.Profile.Id.ToString("N")==File.ReadAllText(Path.Combine(folder,"profile.txt")),"profile unchanged by display preferences");
                Debug.Log("[DisplayValidation] RELOAD-PASS");Application.Quit(0);yield break;
            }
            if(mode=="crash-preview")
            {
                Check(display.Preview(display.Device.Defaults),"preview before forced termination");
                yield return Until(()=>display.Change.CanKeep,"preview readback before forced termination");Debug.Log("[DisplayValidation] PREVIEW-READY");yield break;
            }
            var original=display.Device.Current;File.WriteAllText(Path.Combine(folder,"profile.txt"),root.Profile.Id.ToString("N"));
            Submit("options");Submit("options-display");yield return null;
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768),new Vector2Int(1280,800)})
                yield return SteamSessionValidationDriver.CapturePanel(root.CurrentScreen,Path.Combine(folder,$"Display.{size.x}x{size.y}.png"),size.x,size.y);
            var target=display.Device.SupportedSizes.FirstOrDefault(s=>s.Width>=1024 && s.Width<=1600 && s.Height>=720 && (s.Width!=original.Width || s.Height!=original.Height)) ?? display.Device.Defaults;
            target=new DisplayPreferences(target.Width,target.Height,DisplayMode.Windowed);
            Draft(target);Check(display.Device.Current.Equals(original),"draft cannot resize window");Submit("display-apply");
            yield return Until(()=>display.Change.CanKeep,"windowed preview readback");Debug.Log("[DisplayValidation] WINDOWED-READBACK "+display.Device.Current);
            yield return SteamSessionValidationDriver.CapturePanel(root.CurrentScreen,Path.Combine(folder,"Confirmation.png"),1280,720);
            Submit("display-revert");yield return Until(()=>display.Device.Current.Equals(original),"explicit revert readback");
            Submit("display-defaults");Submit("display-apply");yield return Until(()=>display.Change.CanKeep,"borderless preview readback");Debug.Log("[DisplayValidation] BORDERLESS-READBACK "+display.Device.Current);
            yield return Until(()=>!display.Change.Pending,"15-second automatic rollback");yield return Until(()=>display.Device.Current.Equals(original),"timeout restored original window");
            Check(store.Load(out _).Equals(original),"timeout cannot save unconfirmed settings");Debug.Log("[DisplayValidation] TIMEOUT-ROLLBACK "+display.Device.Current);
            var native=display.Device.Defaults;Draft(new DisplayPreferences(native.Width,native.Height,DisplayMode.Fullscreen));Submit("display-apply");
            yield return Until(()=>display.Change.CanKeep || !display.Change.Pending,"fullscreen preview readback or safe rejection");
            if(display.Change.Pending){Debug.Log("[DisplayValidation] FULLSCREEN-READBACK "+display.Device.Current);Submit("display-revert");}
            else {Check(display.Change.Notice.Contains("Fullscreen is unavailable"),"unsupported fullscreen explained");Debug.Log("[DisplayValidation] FULLSCREEN-UNAVAILABLE-SAFE");}yield return Until(()=>display.Device.Current.Equals(original),"fullscreen revert");
            Draft(target);Submit("display-apply");yield return Until(()=>display.Change.CanKeep,"confirmed window readback");Submit("display-keep");yield return null;
            Check(store.Load(out _).Equals(target),"only Keep saves preview");Submit("options-close");
            Check(root.Flow.State==ApplicationState.MainMenu,"options retain menu flow");
            Debug.Log("[DisplayValidation] EXERCISE-PASS");Application.Quit(0);
        }
    }
}
#endif
