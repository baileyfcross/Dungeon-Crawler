using System;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class AudioPreferencesTests
    {
        private static string Folder()=>Path.GetFullPath(Path.Combine("Logs/Validation/Audio/Settings",Guid.NewGuid().ToString("N")));
        [Test] public void RoundTripAndDeviceSettingsNeverModifyProfileOrRun()
        {
            string path=Folder();Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"profile.save"),"profile");File.WriteAllText(Path.Combine(path,"run.save"),"run");
            var store=new AudioPreferencesStore(Path.Combine(path,"DeviceSettings"));store.Save(new AudioPreferences(.2f,.3f,.4f,true));
            var read=new AudioPreferencesStore(Path.Combine(path,"DeviceSettings")).Load(out var notice);
            Assert.That(read.Master,Is.EqualTo(.2f));Assert.That(read.Effects,Is.EqualTo(.3f));Assert.That(read.Ui,Is.EqualTo(.4f));Assert.That(read.Muted,Is.True);Assert.That(notice,Is.Null);
            Assert.That(File.ReadAllText(Path.Combine(path,"profile.save")),Is.EqualTo("profile"));Assert.That(File.ReadAllText(Path.Combine(path,"run.save")),Is.EqualTo("run"));
        }
        [Test] public void InvalidAndMissingRecordsRecoverExplicitlyWithoutDestroyingEvidence()
        {
            var store=new AudioPreferencesStore(Folder());var defaults=store.Load(out var notice);Assert.That(notice,Is.Null);Assert.That(defaults.Master,Is.EqualTo(.75f));
            store.Save(new AudioPreferences(.2f,.3f,.4f,true));store.Save(AudioPreferences.Defaults);
            File.WriteAllText(store.FilePath,"corrupt");var recovered=store.Load(out notice);Assert.That(recovered.Master,Is.EqualTo(.2f));Assert.That(notice,Does.Contain("backup"));
            Assert.That(File.ReadAllText(store.FilePath),Is.EqualTo("corrupt"));File.WriteAllText(store.FilePath+".backup","also corrupt");
            Assert.That(store.Load(out notice).Master,Is.EqualTo(.75f));Assert.That(notice,Does.Contain("Defaults"));
        }
        [Test] public void ConcurrentWriteIsRejectedAndCannotReplaceValidPreferences()
        {
            var store=new AudioPreferencesStore(Folder());store.Save(AudioPreferences.Defaults);var bytes=File.ReadAllBytes(store.FilePath);
            using(var held=new FileStream(store.FilePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                Assert.Throws<IOException>(()=>store.Save(new AudioPreferences(0,0,0,true)));
            Assert.That(File.ReadAllBytes(store.FilePath),Is.EqualTo(bytes));
        }
        [Test] public void NonfiniteOrOutOfRangeVolumesCannotEnterSettings()
        {
            foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-.1f,1.1f})
            {
                Assert.Throws<ArgumentOutOfRangeException>(()=>new AudioPreferences(bad,1,1,false));
                Assert.Throws<ArgumentOutOfRangeException>(()=>new AudioPreferences(1,bad,1,false));
                Assert.Throws<ArgumentOutOfRangeException>(()=>new AudioPreferences(1,1,bad,false));
            }
        }
    }
}
