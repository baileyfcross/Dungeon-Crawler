using System;
using System.IO;

namespace SpaceCrawler.Core
{
    public sealed class AudioPreferences
    {
        public float Master { get; }
        public float Effects { get; }
        public float Ui { get; }
        public bool Muted { get; }
        public static AudioPreferences Defaults => new AudioPreferences(.75f,1,.8f,false);
        public AudioPreferences(float master,float effects,float ui,bool muted)
        {
            foreach(float value in new[]{master,effects,ui})
                if(float.IsNaN(value) || float.IsInfinity(value) || value<0 || value>1)throw new ArgumentOutOfRangeException(nameof(master));
            Master=master;Effects=effects;Ui=ui;Muted=muted;
        }
    }

    /// <summary>Device-local preferences. Never reads or writes a profile or run checkpoint.</summary>
    public sealed class AudioPreferencesStore
    {
        public string FilePath { get; }
        public AudioPreferencesStore(string directory){FilePath=Path.Combine(Path.GetFullPath(directory),"audio.settings");}
        private static AudioPreferences Read(string path)=>ProgressRecord.Read(path,r=>
        {
            if(r.ReadInt32()!=1)throw new InvalidDataException("Unsupported audio preferences.");
            return new AudioPreferences(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadBoolean());
        });
        public AudioPreferences Load(out string notice)
        {
            notice=null;
            try{if(File.Exists(FilePath))return Read(FilePath);}
            catch(Exception error) when(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {notice="Audio settings could not be read. Defaults are active; existing files are preserved.";}
            try
            {
                if(File.Exists(FilePath+".backup"))
                {var recovered=Read(FilePath+".backup");notice="Audio settings recovered from a backup. Close Options to save them.";return recovered;}
            }
            catch(Exception error) when(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {notice="Audio settings could not be read. Defaults are active; existing files are preserved.";}
            return AudioPreferences.Defaults;
        }
        public void Save(AudioPreferences settings)
        {
            if(settings==null)throw new ArgumentNullException(nameof(settings));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using(var held=new FileStream(FilePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                ProgressRecord.Write(FilePath,w=>{w.Write(1);w.Write(settings.Master);w.Write(settings.Effects);w.Write(settings.Ui);w.Write(settings.Muted);});
        }
    }
}
