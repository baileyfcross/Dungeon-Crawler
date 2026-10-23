using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class AccountProfileStoreTests
    {
        private static string Folder()=>Path.GetFullPath(Path.Combine("Logs","Validation","Accounts",Guid.NewGuid().ToString("N")));
        [Test] public void ExplicitAssociationPreservesProgressAndBacksUpBeforeCommittingOwnership()
        {
            string path=Folder();var progress=new ProfileProgressStore(path);Guid id=progress.Profile.Id;
            progress.EndRun(new RunEndReceipt(Guid.NewGuid(),250,RunEndReason.Completed,new[]{id}));
            File.WriteAllText(Path.Combine(path,"run.save"),"checkpoint-preservation-fixture");
            var profileBytes=File.ReadAllBytes(progress.FilePath);var store=new AccountProfileStore(path);
            Assert.That(store.IsAssociated("111",id),Is.False);
            string backup=store.Associate("111",id);
            Assert.That(File.ReadAllBytes(progress.FilePath),Is.EqualTo(profileBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(backup,"profile.save")),Is.EqualTo(profileBytes));
            Assert.That(File.ReadAllText(Path.Combine(backup,"run.save")),Is.EqualTo("checkpoint-preservation-fixture"));
            Assert.That(Directory.GetFiles(Path.Combine(backup,"RunEnds")).Length,Is.GreaterThan(0));
            Assert.That(new ProfileProgressStore(path).Profile.Experience,Is.EqualTo(15));
            Assert.That(store.IsAssociated("111",id),Is.True);Assert.That(store.Associate("111",id),Is.Null);
            Assert.That(new AccountProfileStore(path).OfflineDirectory(),Is.EqualTo(path));
        }
        [Test] public void DifferentSteamAccountsNeverShareOrMergeTheOriginalProfile()
        {
            string path=Folder();var original=new ProfileProgressStore(path);var store=new AccountProfileStore(path);store.Associate("111",original.Profile.Id);
            string otherPath=store.DirectoryFor("222");var other=new ProfileProgressStore(otherPath);
            Assert.That(otherPath,Is.Not.EqualTo(path));Assert.That(other.Profile.Id,Is.Not.EqualTo(original.Profile.Id));
            Assert.Throws<InvalidOperationException>(()=>store.Associate("222",original.Profile.Id));
            store.Associate("222",other.Profile.Id);Assert.That(store.IsAssociated("111",original.Profile.Id),Is.True);
            Assert.That(store.OfflineDirectory(),Is.EqualTo(otherPath));Assert.That(store.IsAssociated("222",original.Profile.Id),Is.False);
            Assert.Throws<ArgumentException>(()=>store.DirectoryFor("../111"));
        }
        [Test] public void FailedBackupOrConcurrentSaveCannotCommitAnAssociation()
        {
            string path=Folder();var progress=new ProfileProgressStore(path);var store=new AccountProfileStore(path);
            using(var held=new FileStream(Path.Combine(path,"run.save.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                Assert.Throws<IOException>(()=>store.Associate("111",progress.Profile.Id));
            Assert.That(store.IsAssociated("111",progress.Profile.Id),Is.False);
            File.WriteAllText(Path.Combine(path,"AccountBackups"),"blocked backup directory");
            Assert.Throws<IOException>(()=>store.Associate("111",progress.Profile.Id));
            Assert.That(File.Exists(Path.Combine(path,"account.owner")),Is.False);
        }
        [Test] public void CorruptOwnershipNeverFallsBackToAnotherAccountsProfile()
        {
            string path=Folder();var profile=new ProfileProgressStore(path);var store=new AccountProfileStore(path);store.Associate("111",profile.Profile.Id);
            File.WriteAllText(Path.Combine(path,"account.owner"),"corrupt");
            Assert.Throws<InvalidDataException>(()=>store.DirectoryFor("222"));
            Assert.That(new ProfileProgressStore(path).Profile.Id,Is.EqualTo(profile.Profile.Id));
        }
        [Test] public void OriginalCooperativePlayersRemainBoundAcrossRestart()
        {
            string path=Folder();Guid run=Guid.NewGuid(),host=Guid.NewGuid(),peer=Guid.NewGuid();var store=new SessionParticipantsStore(path);
            Assert.That(store.ExpectedPeer(run,"111",host,peer),Is.Null);
            store.Bind(run,"111",host,"222",peer);store.Bind(run,"111",host,"222",peer);
            Assert.That(new SessionParticipantsStore(path).ExpectedPeer(run,"111",host,peer),Is.EqualTo("222"));
            Assert.Throws<InvalidOperationException>(()=>store.Bind(run,"111",host,"333",peer));
            Assert.Throws<InvalidOperationException>(()=>store.Bind(run,"111",host,"222",Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(()=>store.ExpectedPeer(run,"333",host,peer));
        }
    }
}
