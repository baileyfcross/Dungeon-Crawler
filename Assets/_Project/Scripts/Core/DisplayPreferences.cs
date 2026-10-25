using System;
using System.Collections.Generic;
using System.IO;

namespace SpaceCrawler.Core
{
    public enum DisplayMode { Windowed=1, Borderless=2, Fullscreen=3 }
    public sealed class DisplayPreferences : IEquatable<DisplayPreferences>
    {
        public int Width { get; }
        public int Height { get; }
        public DisplayMode Mode { get; }
        public DisplayPreferences(int width,int height,DisplayMode mode)
        {
            if(width<320 || width>16384 || height<240 || height>16384 || mode<DisplayMode.Windowed || mode>DisplayMode.Fullscreen)throw new ArgumentOutOfRangeException();
            Width=width;Height=height;Mode=mode;
        }
        public bool Equals(DisplayPreferences other)=>other!=null && Width==other.Width && Height==other.Height && Mode==other.Mode;
        public override bool Equals(object other)=>Equals(other as DisplayPreferences);
        public override int GetHashCode()=>Width*397^Height*7^(int)Mode;
        public override string ToString()=>$"{Width} × {Height} / {Mode}";
    }
    public interface IDisplayDevice
    {
        bool CanApply { get; }
        DisplayPreferences Current { get; }
        DisplayPreferences Defaults { get; }
        IReadOnlyList<DisplayPreferences> SupportedSizes { get; }
        void Apply(DisplayPreferences settings);
    }
    /// <summary>Device-local record; no profile, Steam identity, or run snapshot access.</summary>
    public sealed class DisplayPreferencesStore
    {
        public string FilePath { get; }
        public DisplayPreferencesStore(string directory){FilePath=Path.Combine(Path.GetFullPath(directory),"display.settings");}
        private static DisplayPreferences Read(string path)=>ProgressRecord.Read(path,r=>
        {
            if(r.ReadInt32()!=1)throw new InvalidDataException("Unsupported display preferences.");
            return new DisplayPreferences(r.ReadInt32(),r.ReadInt32(),(DisplayMode)r.ReadInt32());
        });
        public DisplayPreferences Load(out string notice)
        {
            notice=null;
            try { if(File.Exists(FilePath))return Read(FilePath); }
            catch(Exception error) when(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {notice="Display settings could not be read. Safe defaults are active; existing files are preserved.";}
            try { if(File.Exists(FilePath+".backup")){var value=Read(FilePath+".backup");notice="Display settings recovered from a backup.";return value;} }
            catch(Exception error) when(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {notice="Display settings could not be read. Safe defaults are active; existing files are preserved.";}
            return null;
        }
        public void Save(DisplayPreferences value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using(var held=new FileStream(FilePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                ProgressRecord.Write(FilePath,w=>{w.Write(1);w.Write(value.Width);w.Write(value.Height);w.Write((int)value.Mode);});
        }
    }
    /// <summary>One reversible local display request. Time is monotonic and independent of gameplay pause.</summary>
    public sealed class DisplayChange
    {
        public const double ConfirmationSeconds=15;
        private readonly IDisplayDevice device;
        private readonly DisplayPreferencesStore store;
        private readonly Func<double> clock;
        private double deadline,readySince=-1,started,loadDeadline;
        private DisplayPreferences loadTarget;
        private DisplayPreferences previous;
        public DisplayPreferences Confirmed { get; private set; }
        public DisplayPreferences Requested { get; private set; }
        public bool Pending=>Requested!=null;
        public bool CanKeep=>Pending && Requested.Equals(device.Current) && readySince>=0 && clock()-readySince>=.5;
        public double SecondsLeft=>Pending?Math.Max(0,deadline-clock()):0;
        public string Notice { get; private set; }
        public DisplayChange(IDisplayDevice device,DisplayPreferencesStore store,Func<double> clock)
        {this.device=device;this.store=store;this.clock=clock;Confirmed=device.Current;}
        public bool Supported(DisplayPreferences value)
        {
            if(value.Mode==DisplayMode.Windowed && value.Width<=device.Defaults.Width && value.Height<=device.Defaults.Height)return true;
            foreach(var size in device.SupportedSizes)if(size.Width==value.Width && size.Height==value.Height)return true;
            return false;
        }
        public void Load()
        {
            var saved=store.Load(out var notice);Notice=notice;
            if(saved==null && notice==null)return; // Preserve the launch window on first use.
            Confirmed=saved!=null && Supported(saved)?saved:device.Defaults;
            if(saved!=null && !Supported(saved))Notice="Saved resolution is unavailable. Native Borderless settings are active.";
            if(device.CanApply)
            {
                try {device.Apply(Confirmed);loadTarget=Confirmed;loadDeadline=clock()+2;}
                catch(Exception)
                {
                    Confirmed=device.Defaults;
                    try {device.Apply(Confirmed);Notice="Saved display settings could not be applied. Safe defaults are active.";}
                    catch(Exception){Notice="Display settings could not be applied. The current window is unchanged.";}
                }
            }
        }
        public bool Begin(DisplayPreferences next)
        {
            if(Pending || !device.CanApply)return false;
            if(!Supported(next)){Notice="That resolution is no longer available. Reopen Options to refresh the display list.";return false;}
            previous=Supported(device.Current)?device.Current:device.Defaults;
            try { store.Save(previous); } // Persist the safe rollback target before touching the window, even on first use.
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {Notice="Display settings could not be saved. No change was applied.";return false;}
            Confirmed=previous;Requested=next;started=clock();deadline=started+ConfirmationSeconds;readySince=-1;loadTarget=null;
            try {device.Apply(next);if(next.Equals(device.Current))readySince=clock();Notice="Keep these display settings?";return true;}
            catch(Exception){Revert();Notice="The display change failed. Previous settings were restored.";return false;}
        }
        public bool Keep()
        {
            Tick();if(!CanKeep)return false;
            try {store.Save(Requested);Confirmed=Requested;Requested=null;Notice="Display settings saved on this computer.";return true;}
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {Notice="Could not save display settings. Retry Keep, or the previous settings will return.";return false;}
        }
        public void Revert()
        {
            if(!Pending)return;
            Requested=null;Confirmed=Supported(previous)?previous:device.Defaults;
            try {device.Apply(Confirmed);Notice="Previous display settings restored.";}
            catch(Exception){Notice="The display could not be restored. Restart the game to load the last saved settings.";}
        }
        public void Tick()
        {
            if(!Pending)
            {
                if(loadTarget!=null && clock()>=loadDeadline)
                {
                    var expected=loadTarget;loadTarget=null;
                    if(!expected.Equals(device.Current))
                    {
                        Confirmed=device.Defaults;
                        try {device.Apply(Confirmed);Notice="Saved display settings could not be applied. Safe Borderless settings are active.";}
                        catch(Exception){Notice="Display settings could not be applied. The current window is unchanged.";}
                    }
                }
                return;
            }
            if(clock()>=deadline){Revert();if(Notice=="Previous display settings restored.")Notice="Confirmation timed out. Previous display settings restored.";return;}
            if(Requested.Equals(device.Current)){if(readySince<0)readySince=clock();}
            else
            {
                readySince=-1;
                if(clock()-started>=2)
                {
                    bool fullscreen=Requested.Mode==DisplayMode.Fullscreen;Revert();
                    if(Notice=="Previous display settings restored.")Notice=fullscreen?
                        "Fullscreen is unavailable on this display or driver. Previous settings restored; try Borderless.":
                        "The requested display settings could not be applied. Previous settings restored.";
                }
            }
        }
    }
}
