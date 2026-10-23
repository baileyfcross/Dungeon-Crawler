using System;
using System.IO;
using System.Linq;

namespace SpaceCrawler.Core
{
    /// <summary>Account ownership lives beside profiles; existing profile and checkpoint encodings remain unchanged.</summary>
    public sealed class AccountProfileStore
    {
        private readonly string root;
        public AccountProfileStore(string directory) { root = Path.GetFullPath(directory); Directory.CreateDirectory(root); }
        private static string Key(string account)
        {
            if (string.IsNullOrEmpty(account) || account.Length > 64 || account.Any(c => !char.IsDigit(c) || c > 127))
                throw new ArgumentException("Invalid account identifier.");
            return account;
        }
        private static Tuple<string,Guid> Owner(string directory)
        {
            string path = Path.Combine(directory,"account.owner");
            if (!File.Exists(path))
            { if (File.Exists(path+".backup")) throw new InvalidDataException("Account ownership primary is missing."); return null; }
            return ProgressRecord.Read(path,r =>
            {
                if (r.ReadInt32()!=1) throw new InvalidDataException("Unsupported account ownership.");
                var account=Key(r.ReadString());var profile=new Guid(r.ReadBytes(16));
                if(profile==Guid.Empty)throw new InvalidDataException("Invalid account profile.");
                return Tuple.Create(account,profile);
            });
        }
        public string DirectoryFor(string account)
        {
            Key(account);var original=Owner(root);
            return original==null || original.Item1==account ? root : Path.Combine(root,"Accounts",account);
        }
        public bool IsAssociated(string account,Guid profile)
        { var owner=Owner(DirectoryFor(account));return owner!=null && owner.Item1==account && owner.Item2==profile; }
        public string OfflineDirectory()
        {
            string path=Path.Combine(root,"active.account");
            if(!File.Exists(path)) { if(File.Exists(path+".backup"))throw new InvalidDataException("Active profile primary is missing.");return root; }
            string account=ProgressRecord.Read(path,r=>Key(r.ReadString()));
            string directory=DirectoryFor(account);var owner=Owner(directory);
            if(owner==null || owner.Item1!=account)throw new InvalidDataException("Active profile has no matching account association.");
            return directory;
        }
        public void Select(string account,Guid profile)
        {
            if(!IsAssociated(account,profile))throw new InvalidOperationException("Profile is not associated with this account.");
            using(var ownership=new FileStream(Path.Combine(root,"account.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                ProgressRecord.Write(Path.Combine(root,"active.account"),w=>w.Write(account));
        }
        public string Associate(string account,Guid profile)
        {
            Key(account);string directory=DirectoryFor(account);var progress=new ProfileProgressStore(directory);
            string backup=null;
            using(var ownership=new FileStream(Path.Combine(root,"account.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
            using(var profileLock=new FileStream(Path.Combine(directory,"progress.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
            using(var runLock=new FileStream(Path.Combine(directory,"run.save.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
            {
                if(progress.ReadProfile().Id!=profile)throw new InvalidOperationException("Profile changed before association.");
                var existing=Owner(directory);
                if(existing!=null && (existing.Item1!=account || existing.Item2!=profile))throw new InvalidOperationException("Profile already belongs to another account.");
                if(existing==null)
                {
                    backup=Path.Combine(directory,"AccountBackups",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
                    foreach(var file in Directory.GetFiles(directory).Where(f=>Path.GetFileName(f).StartsWith("profile.save",StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("run.save",StringComparison.Ordinal)))
                        if(!file.EndsWith(".lock",StringComparison.Ordinal) && !file.EndsWith(".pending",StringComparison.Ordinal))
                        {if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot back up linked profile data.");File.Copy(file,Path.Combine(backup,Path.GetFileName(file)));}
                    foreach(string folder in new[]{"RunEnds","Sessions"})CopyOwned(Path.Combine(directory,folder),Path.Combine(backup,folder));
                    ProgressRecord.Write(Path.Combine(directory,"account.owner"),w=>{w.Write(1);w.Write(account);w.Write(profile.ToByteArray());});
                }
                ProgressRecord.Write(Path.Combine(root,"active.account"),w=>w.Write(account));
            }
            return backup;
        }
        private static void CopyOwned(string source,string destination)
        {
            if(!Directory.Exists(source))return;
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot back up linked profile data.");
            Directory.CreateDirectory(destination);
            foreach(string file in Directory.GetFiles(source))
            { if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot back up linked profile data.");File.Copy(file,Path.Combine(destination,Path.GetFileName(file))); }
            foreach(string folder in Directory.GetDirectories(source))CopyOwned(folder,Path.Combine(destination,Path.GetFileName(folder)));
        }
    }
    /// <summary>Durable binding of a run's original player slots to platform accounts.</summary>
    public sealed class SessionParticipantsStore
    {
        private readonly string directory;
        public SessionParticipantsStore(string directory){this.directory=Path.GetFullPath(directory);}
        private string PathFor(Guid run)=>Path.Combine(directory,"Sessions",run.ToString("N")+".members");
        private string[] Read(Guid run)
        {
            string path=PathFor(run);
            if(!File.Exists(path)){if(File.Exists(path+".backup"))throw new InvalidDataException("Run membership primary is missing.");return null;}
            return ProgressRecord.Read(path,r=>{if(r.ReadInt32()!=1)throw new InvalidDataException("Invalid run membership.");return new[]{r.ReadString(),r.ReadString(),r.ReadString(),r.ReadString()};});
        }
        public string ExpectedPeer(Guid run,string host,Guid hostProfile,Guid peerProfile)
        {
            var members=Read(run);if(members==null)return null;
            if(members[0]!=host || members[1]!=hostProfile.ToString("N") || members[3]!=peerProfile.ToString("N"))throw new InvalidOperationException("Saved run membership does not match this profile.");
            return members[2];
        }
        public void Bind(Guid run,string host,Guid hostProfile,string peer,Guid peerProfile)
        {
            if(run==Guid.Empty || hostProfile==Guid.Empty || peerProfile==Guid.Empty || host==peer || string.IsNullOrEmpty(host) || string.IsNullOrEmpty(peer))throw new ArgumentException("Invalid session participants.");
            Directory.CreateDirectory(Path.Combine(directory,"Sessions"));
            using(var ownership=new FileStream(PathFor(run)+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
            {
                var old=Read(run);var next=new[]{host,hostProfile.ToString("N"),peer,peerProfile.ToString("N")};
                if(old!=null){if(!old.SequenceEqual(next))throw new InvalidOperationException("The saved run requires its original host and partner.");return;}
                ProgressRecord.Write(PathFor(run),w=>{w.Write(1);foreach(var value in next)w.Write(value);});
            }
        }
    }
}
