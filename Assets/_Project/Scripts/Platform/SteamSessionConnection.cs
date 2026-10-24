using System;
using System.Collections.Generic;
using System.Text;
using Netcode.Transports;
using SpaceCrawler.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Platform
{
    /// <summary>Steam transport identity and lobby membership authorize admission, never a claimed account in a packet.</summary>
    public sealed class SteamSessionConnection
    {
        public const string Protocol = "session13-generation2-sequence2";
        [Serializable] private sealed class Hello { public string protocol,build,profile; }
        private readonly Func<ulong,bool> member;
        private readonly Dictionary<ulong,Guid> profiles=new Dictionary<ulong,Guid>();
        public readonly ulong LocalAccount,HostAccount;
        public readonly bool IsHost;
        public readonly string Build;
        public ulong ExpectedPeer { get; private set; }
        public ulong AdmittedPeer { get; private set; }
        public Action<Guid,Guid,ulong,Guid> RecordParticipants;
        public SteamSessionConnection(ulong local,ulong host,bool isHost,string build,Func<ulong,bool> member,ulong expectedPeer=0)
        {
            if(local==0 || host==0 || string.IsNullOrEmpty(build) || member==null || (isHost && local!=host))throw new ArgumentException("Invalid Steam session identity.");
            LocalAccount=local;HostAccount=host;IsHost=isHost;Build=build;this.member=member;ExpectedPeer=expectedPeer;
        }
        public bool Allows(ulong peer) => peer!=0 && peer!=LocalAccount && member(peer) && (IsHost ? ExpectedPeer==0 || ExpectedPeer==peer : peer==HostAccount);
        public NetworkTransport CreateTransport(GameObject target)
        {
            var transport=target.AddComponent<LobbySteamTransport>();transport.AllowPeer=Allows;transport.IncomingConnectionAllowed=Allows;transport.ConnectToSteamID=HostAccount;return transport;
        }
        public byte[] Payload(Guid profile) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Hello{protocol=Protocol,build=Build,profile=profile.ToString("N")}));
        public bool Approve(ulong peer,byte[] payload,out string reason)
        {
            reason="Steam session membership, profile, or build is incompatible.";
            if(!IsHost || !Allows(peer) || payload==null || payload.Length>1024 || (AdmittedPeer!=0 && AdmittedPeer!=peer))return false;
            try
            {
                var hello=JsonUtility.FromJson<Hello>(Encoding.UTF8.GetString(payload));
                if(hello==null || hello.protocol!=Protocol || hello.build!=Build || !Guid.TryParseExact(hello.profile,"N",out var profile) || profile==Guid.Empty)return false;
                if(profiles.TryGetValue(peer,out var previous) && previous!=profile)return false;
                profiles[peer]=profile;AdmittedPeer=peer;reason=null;return true;
            }
            catch(ArgumentException){return false;}
        }
        public void ValidateProfile(ulong peer,Guid profile)
        { if(!Allows(peer) || !profiles.TryGetValue(peer,out var approved) || approved!=profile)throw new InvalidOperationException("Profile does not match the authenticated Steam connection."); }
        public void BindParticipants(Guid run,Guid hostProfile,ulong peer,Guid peerProfile)
        { ValidateProfile(peer,peerProfile);RecordParticipants?.Invoke(run,hostProfile,peer,peerProfile);ExpectedPeer=peer; }
    }
    public sealed class LobbySteamTransport : SteamNetworkingSocketsTransport
    {
        public Func<ulong,bool> AllowPeer;
        private bool closed;
        public override bool StartServer(){closed=false;return base.StartServer();}
        public override bool StartClient(){closed=false;return base.StartClient();}
        public override void Shutdown(){if(closed)return;closed=true;base.Shutdown();}
        public override NetworkEvent PollEvent(out ulong clientId,out ArraySegment<byte> payload,out float receiveTime)
        {
            var value=base.PollEvent(out clientId,out payload,out receiveTime);
            if((value==NetworkEvent.Connect || value==NetworkEvent.Data) && (AllowPeer==null || !AllowPeer(clientId)))
            { base.DisconnectRemoteClient(clientId);payload=default;return NetworkEvent.Nothing; }
            return value;
        }
    }
}
