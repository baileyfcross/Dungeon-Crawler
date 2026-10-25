using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class DisplayPreferencesTests
    {
        public sealed class Device : IDisplayDevice
        {
            public bool CanApply { get; set; }=true;
            public DisplayPreferences Current { get; set; }=new DisplayPreferences(1280,720,DisplayMode.Windowed);
            public DisplayPreferences Defaults=>new DisplayPreferences(1920,1080,DisplayMode.Borderless);
            public IReadOnlyList<DisplayPreferences> SupportedSizes=>new[]{Defaults,new DisplayPreferences(1280,720,DisplayMode.Borderless)};
            public bool Delayed,Throw,FallbackFullscreen;
            public DisplayPreferences Last;
            public void Apply(DisplayPreferences value){Last=value;if(Throw){Throw=false;throw new InvalidOperationException();}if(!Delayed)Current=FallbackFullscreen && value.Mode==DisplayMode.Fullscreen?new DisplayPreferences(value.Width,value.Height,DisplayMode.Borderless):value;}
        }
        private string folder;
        private DisplayPreferencesStore store;
        private Device device;
        private double time;
        private DisplayChange change;
        [SetUp]public void SetUp()
        {
            time=0;
            folder=Path.GetFullPath("Logs/Validation/Display/"+Guid.NewGuid().ToString("N"));store=new DisplayPreferencesStore(folder);device=new Device();
            change=new DisplayChange(device,store,()=>time);change.Load();
        }
        [TestCase(DisplayMode.Windowed)] [TestCase(DisplayMode.Borderless)] [TestCase(DisplayMode.Fullscreen)]
        public void OnlyExplicitKeptSettingsSurviveRestart(DisplayMode mode)
        {
            var old=device.Current;var next=new DisplayPreferences(1920,1080,mode);
            Assert.That(change.Begin(next),Is.True);Assert.That(device.Current,Is.EqualTo(next));
            Assert.That(store.Load(out _),Is.EqualTo(old),"Crash during preview restores the previous confirmed choice.");
            time=.5;Assert.That(change.Keep(),Is.True);Assert.That(change.Pending,Is.False);Assert.That(store.Load(out _),Is.EqualTo(next));
            device.Current=old;new DisplayChange(device,store,()=>time).Load();Assert.That(device.Current,Is.EqualTo(next));
        }
        [Test]public void TimeoutUsesRealTimeAndCannotBeConfirmedAfterDeadline()
        {
            var old=device.Current;change.Begin(device.Defaults);time=14.99;Assert.That(change.Pending,Is.True);
            time=15;Assert.That(change.Keep(),Is.False);Assert.That(device.Current,Is.EqualTo(old));Assert.That(change.Pending,Is.False);
            Assert.That(store.Load(out _),Is.EqualTo(old));
        }
        [Test]public void RevertAndRepeatedRequestsNeverReplaceRollbackTarget()
        {
            var old=device.Current;change.Begin(device.Defaults);Assert.That(change.Begin(new DisplayPreferences(1280,720,DisplayMode.Fullscreen)),Is.False);
            change.Revert();change.Revert();Assert.That(device.Current,Is.EqualTo(old));Assert.That(store.Load(out _),Is.EqualTo(old));
        }
        [Test]public void ReadbackMustSettleBeforeKeep()
        {
            device.Delayed=true;change.Begin(device.Defaults);Assert.That(change.Keep(),Is.False);
            device.Current=device.Last;change.Tick();time=.5;Assert.That(change.Keep(),Is.True);
        }
        [Test]public void WriteFailureBlocksPreviewAndConfirmationWithoutLosingPreviousSettings()
        {
            Directory.CreateDirectory(folder);
            using(var held=new FileStream(store.FilePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                Assert.That(change.Begin(device.Defaults),Is.False);
            var old=device.Current;Assert.That(change.Pending,Is.False);Assert.That(change.Begin(device.Defaults),Is.True);
            time=.5;using(var held=new FileStream(store.FilePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                Assert.That(change.Keep(),Is.False);
            Assert.That(change.Pending,Is.True);time=16;change.Tick();Assert.That(device.Current,Is.EqualTo(old));Assert.That(store.Load(out _),Is.EqualTo(old));
        }
        [Test]public void RemovedResolutionAndCorruptSettingsFallBackWithoutDeletingEvidence()
        {
            store.Save(new DisplayPreferences(3840,2160,DisplayMode.Fullscreen));change.Load();Assert.That(device.Current,Is.EqualTo(device.Defaults));
            Assert.That(change.Notice,Does.Contain("unavailable"));
            byte[] broken={1,2,3};File.WriteAllBytes(store.FilePath,broken);change.Load();Assert.That(device.Current,Is.EqualTo(device.Defaults));Assert.That(File.ReadAllBytes(store.FilePath),Is.EqualTo(broken));
        }
        [Test]public void BackupAndApplyFailureAreSafe()
        {
            var old=device.Current;store.Save(old);store.Save(device.Defaults);File.WriteAllBytes(store.FilePath,new byte[]{1});
            Assert.That(store.Load(out var notice),Is.EqualTo(old));Assert.That(notice,Does.Contain("backup"));
            device.Throw=true;Assert.That(change.Begin(device.Defaults),Is.False);Assert.That(change.Pending,Is.False);Assert.That(device.Current,Is.EqualTo(old));
        }
        [Test]public void DeviceFailuresFallBackAtStartupAndNeverClaimAFailedRollbackSucceeded()
        {
            var original=device.Current;store.Save(original);device.Throw=true;change.Load();
            Assert.That(device.Current,Is.EqualTo(device.Defaults));Assert.That(change.Notice,Does.Contain("Safe defaults"));
            change.Begin(original);device.Throw=true;time=16;change.Tick();
            Assert.That(change.Notice,Does.Contain("could not be restored"));Assert.That(store.Load(out _),Is.EqualTo(device.Defaults));
        }
        [Test]public void RejectedExclusiveModeCannotBeKeptAndRevertsWithExplanation()
        {
            var old=device.Current;device.FallbackFullscreen=true;
            change.Begin(new DisplayPreferences(1920,1080,DisplayMode.Fullscreen));time=.5;Assert.That(change.Keep(),Is.False);
            time=2;change.Tick();Assert.That(change.Pending,Is.False);Assert.That(device.Current,Is.EqualTo(old));
            Assert.That(change.Notice,Does.Contain("Fullscreen is unavailable"));Assert.That(store.Load(out _),Is.EqualTo(old));
        }
        [Test]public void KeepRequiresAStableReadback()
        {
            change.Begin(device.Defaults);time=.49;Assert.That(change.Keep(),Is.False);time=.5;Assert.That(change.Keep(),Is.True);
        }
        [TestCase(0,720,DisplayMode.Windowed)] [TestCase(1280,0,DisplayMode.Borderless)] [TestCase(1280,720,(DisplayMode)99)]
        public void InvalidPreferencesAreRejected(int width,int height,DisplayMode mode)
        {Assert.Throws<ArgumentOutOfRangeException>(()=>new DisplayPreferences(width,height,mode));}
        [Test]public void UnsupportedExclusiveResolutionNeverReachesDevice()
        {Assert.That(change.Begin(new DisplayPreferences(1234,777,DisplayMode.Fullscreen)),Is.False);Assert.That(device.Last,Is.Null);}
    }
}
