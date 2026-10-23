using System;
using System.IO;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public enum SoundCue { KnifeSwing,BlasterShot,Hit,EnemyDown,EnemyWarning,PlayerHit,PlayerDown,Revive,RoomTransition,LevelTransition,RunComplete,RunOver,UiFocus,UiActivate }

    /// <summary>One application-owned, bounded pool for local presentation. No network or gameplay decisions.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public const int VoiceLimit=12;
        public static GameAudio Current { get; private set; }
        public AudioPreferences Preferences { get; private set; }=AudioPreferences.Defaults;
        public string Notice { get; private set; }
        public int VoiceCount=>voices?.Length ?? 0;
        private AudioPreferencesStore store;
        private AudioSource[] voices;
        private SoundCue[] playing;
        private AudioClip[] clips;
        private int[] emitted;
        private int nextVoice;
        private bool dirty;
        private float saveAt,lastUiFocus=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()=>Current=null;

        public void Bind(string directory)
        {
            if(voices!=null || (Current!=null && Current!=this))throw new InvalidOperationException("Only one application audio owner is allowed.");
            store=new AudioPreferencesStore(directory);Preferences=store.Load(out var notice);Notice=notice;
            clips=new AudioClip[Enum.GetValues(typeof(SoundCue)).Length];emitted=new int[clips.Length];
            for(int i=0;i<clips.Length;i++)
            {
                clips[i]=Resources.Load<AudioClip>("Audio/"+(SoundCue)i);
                if(clips[i]==null)throw new InvalidOperationException("Missing sound cue: "+(SoundCue)i);
            }
            voices=new AudioSource[VoiceLimit];playing=new SoundCue[VoiceLimit];
            for(int i=0;i<voices.Length;i++)
            {
                var child=new GameObject("SoundVoice");child.transform.SetParent(transform,false);
                var voice=child.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=0;voice.ignoreListenerPause=true;
                voices[i]=voice;
            }
            Current=this;
        }
        private float Gain(SoundCue cue)=>Preferences.Muted ? 0 : .3f*Preferences.Master*(cue>=SoundCue.UiFocus?Preferences.Ui:Preferences.Effects);
        public int EmittedCount(SoundCue cue)=>emitted?[(int)cue] ?? 0;
        public static void Emit(SoundCue cue){if(Current!=null)Current.Play(cue);}
        private void Play(SoundCue cue)
        {
            if(voices==null || Gain(cue)<=0)return;
            if(cue==SoundCue.UiFocus && Time.unscaledTime-lastUiFocus<.04f)return;
            int slot=-1;
            for(int i=0;i<voices.Length;i++)if(!voices[i].isPlaying){slot=i;break;}
            if(slot<0){if(cue==SoundCue.UiFocus)return;slot=nextVoice++%voices.Length;}
            if(cue==SoundCue.UiFocus)lastUiFocus=Time.unscaledTime;
            var voice=voices[slot];voice.Stop();playing[slot]=cue;voice.clip=clips[(int)cue];voice.volume=Gain(cue);voice.Play();emitted[(int)cue]++;
        }
        public void SetPreferences(AudioPreferences preferences)
        {
            Preferences=preferences ?? throw new ArgumentNullException(nameof(preferences));dirty=true;saveAt=Time.unscaledTime+.4f;Notice="Audio changes are active. Saving on this computer...";
            for(int i=0;i<(voices?.Length ?? 0);i++)voices[i].volume=Gain(playing[i]);
        }
        public bool SavePreferences()
        {
            try{store.Save(Preferences);dirty=false;Notice="Audio settings saved on this computer.";return true;}
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {Notice="Audio settings could not be saved. They remain active for this session. Check storage and try again.";return false;}
        }
        private void Update(){if(dirty && Time.unscaledTime>=saveAt){saveAt=Time.unscaledTime+5;SavePreferences();}}
        private void OnApplicationQuit(){if(dirty)SavePreferences();}
        private void OnDestroy(){if(Current==this)Current=null;}
    }

    internal sealed class PlayerSoundState
    {
        private PlayerVitals? previous;
        public void Observe(PlayerVitals vitals)
        {
            if(previous.HasValue)
            {
                var old=previous.Value;
                if(old.State==PlayerLifeState.Downed && vitals.State==PlayerLifeState.Alive)GameAudio.Emit(SoundCue.Revive);
                else if(old.State!=PlayerLifeState.Downed && vitals.State==PlayerLifeState.Downed)GameAudio.Emit(SoundCue.PlayerDown);
                else if(vitals.Current<old.Current)GameAudio.Emit(SoundCue.PlayerHit);
            }
            previous=vitals;
        }
    }
}
