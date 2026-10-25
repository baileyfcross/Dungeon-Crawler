using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed class UnityDisplayDevice : IDisplayDevice
    {
        public bool CanApply=>!Application.isEditor && Application.platform==RuntimePlatform.WindowsPlayer;
        private static DisplayMode Mode=>Screen.fullScreenMode==FullScreenMode.Windowed?DisplayMode.Windowed:
            Screen.fullScreenMode==FullScreenMode.ExclusiveFullScreen?DisplayMode.Fullscreen:DisplayMode.Borderless;
        public DisplayPreferences Current=>new DisplayPreferences(Math.Max(320,Screen.width),Math.Max(240,Screen.height),Mode);
        public DisplayPreferences Defaults
        {
            get {var info=Screen.mainWindowDisplayInfo;return new DisplayPreferences(Math.Max(320,info.width),Math.Max(240,info.height),DisplayMode.Borderless);}
        }
        public IReadOnlyList<DisplayPreferences> SupportedSizes
        {
            get
            {
                var sizes=Screen.resolutions.Where(r=>r.width>=320 && r.height>=240).Select(r=>new DisplayPreferences(r.width,r.height,DisplayMode.Borderless)).ToList();
                if(!sizes.Any(s=>s.Width==Defaults.Width && s.Height==Defaults.Height))sizes.Add(Defaults);
                return sizes.GroupBy(s=>new {s.Width,s.Height}).Select(g=>g.First()).OrderBy(s=>s.Width).ThenBy(s=>s.Height).ToArray();
            }
        }
        public void Apply(DisplayPreferences value)
        {
            if(!CanApply)return;
            Screen.SetResolution(value.Width,value.Height,value.Mode==DisplayMode.Windowed?FullScreenMode.Windowed:
                value.Mode==DisplayMode.Fullscreen?FullScreenMode.ExclusiveFullScreen:FullScreenMode.FullScreenWindow);
        }
    }
    /// <summary>Application-owned local display adapter. Never sends gameplay or network commands.</summary>
    public sealed class GameDisplay : MonoBehaviour
    {
        public static GameDisplay Current { get; private set; }
        public IDisplayDevice Device { get; private set; }
        public DisplayChange Change { get; private set; }
        private bool heldBackground,previousBackground;
        public void Bind(string directory,IDisplayDevice device=null)
        {
            if(Current!=null && Current!=this)throw new InvalidOperationException("Only one local display owner is allowed.");
            Current=this;Device=device ?? new UnityDisplayDevice();Change=new DisplayChange(Device,new DisplayPreferencesStore(directory),()=>Time.realtimeSinceStartupAsDouble);Change.Load();
        }
        public bool Preview(DisplayPreferences value)
        {
            if(Change.Pending)return false;
            previousBackground=Application.runInBackground;Application.runInBackground=true;heldBackground=true;
            bool accepted=Change.Begin(value);if(!accepted)ReleaseBackground();return accepted;
        }
        public bool Keep(){bool kept=Change.Keep();if(!Change.Pending)ReleaseBackground();return kept;}
        public void Revert(){Change?.Revert();ReleaseBackground();}
        private void ReleaseBackground(){if(heldBackground){Application.runInBackground=previousBackground;heldBackground=false;}}
        private void Update(){if(Change==null)return;Change.Tick();if(!Change.Pending)ReleaseBackground();}
        private void OnApplicationQuit(){Revert();}
        private void OnDestroy(){Revert();if(Current==this)Current=null;}
    }
}
