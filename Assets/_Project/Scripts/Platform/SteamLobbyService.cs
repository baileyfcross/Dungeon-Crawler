using System;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace SpaceCrawler.Platform
{
    public interface ISteamLobbyApi : IDisposable
    {
        ulong LocalAccount { get; }
        event Action<int,ulong,bool> Created;
        event Action<int,ulong,bool> Entered;
        event Action<ulong> Changed;
        event Action<ulong> Invited;
        void Create(int request);void Join(ulong lobby,int request);void Leave(ulong lobby);
        bool Configure(ulong lobby);bool Set(ulong lobby,string key,string value);
        string Get(ulong lobby,string key);ulong Owner(ulong lobby);ulong[] Members(ulong lobby);
        void Invite(ulong lobby);
    }
    public sealed class SteamLobbyApi : ISteamLobbyApi
    {
        private readonly System.Collections.Generic.List<IDisposable> pending=new System.Collections.Generic.List<IDisposable>();
        private readonly Callback<LobbyChatUpdate_t> changed;
        private readonly Callback<LobbyDataUpdate_t> data;
        private readonly Callback<GameLobbyJoinRequested_t> invited;
        public ulong LocalAccount=>SteamUser.GetSteamID().m_SteamID;
        public event Action<int,ulong,bool> Created,Entered;
        public event Action<ulong> Changed,Invited;
        public SteamLobbyApi()
        {
            changed=Callback<LobbyChatUpdate_t>.Create(r=>Changed?.Invoke(r.m_ulSteamIDLobby));
            data=Callback<LobbyDataUpdate_t>.Create(r=>Changed?.Invoke(r.m_ulSteamIDLobby));
            invited=Callback<GameLobbyJoinRequested_t>.Create(r=>Invited?.Invoke(r.m_steamIDLobby.m_SteamID));
        }
        public static ulong ParseInvitation(string commandLine)
        {
            var args=(commandLine ?? string.Empty).Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            for(int i=0;i+1<args.Length;i++)if(args[i]=="+connect_lobby" && ulong.TryParse(args[i+1].Trim('"'),out var lobby))return lobby;
            return 0;
        }
        public static ulong LaunchInvitation()
        {
            SteamApps.GetLaunchCommandLine(out string commandLine,4096);
            ulong lobby=ParseInvitation(commandLine);
            return lobby!=0?lobby:ParseInvitation(string.Join(" ",Environment.GetCommandLineArgs().Skip(1)));
        }
        public void Create(int request)
        {
            CallResult<LobbyCreated_t> call=null;
            call=CallResult<LobbyCreated_t>.Create((r,failed)=>{try{Created?.Invoke(request,r.m_ulSteamIDLobby,!failed && r.m_eResult==EResult.k_EResultOK);}finally{pending.Remove(call);call.Dispose();}});
            pending.Add(call);call.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate,2));
        }
        public void Join(ulong lobby,int request)
        {
            CallResult<LobbyEnter_t> call=null;
            call=CallResult<LobbyEnter_t>.Create((r,failed)=>{try{Entered?.Invoke(request,r.m_ulSteamIDLobby,!failed && r.m_EChatRoomEnterResponse==(uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess);}finally{pending.Remove(call);call.Dispose();}});
            pending.Add(call);call.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
        }
        public void Leave(ulong lobby)=>SteamMatchmaking.LeaveLobby(new CSteamID(lobby));
        public bool Configure(ulong lobby)=>SteamMatchmaking.SetLobbyMemberLimit(new CSteamID(lobby),2) && SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobby),true);
        public bool Set(ulong lobby,string key,string value)=>SteamMatchmaking.SetLobbyData(new CSteamID(lobby),key,value);
        public string Get(ulong lobby,string key)=>SteamMatchmaking.GetLobbyData(new CSteamID(lobby),key);
        public ulong Owner(ulong lobby)=>SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby)).m_SteamID;
        public ulong[] Members(ulong lobby)=>Enumerable.Range(0,SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobby))).Select(i=>SteamMatchmaking.GetLobbyMemberByIndex(new CSteamID(lobby),i).m_SteamID).ToArray();
        public void Invite(ulong lobby) {if(!SteamUtils.IsOverlayEnabled())throw new InvalidOperationException("Steam overlay is unavailable.");SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));}
        public void Dispose(){foreach(var call in pending)call.Dispose();pending.Clear();changed.Dispose();data.Dispose();invited.Dispose();}
    }
    /// <summary>Lobby discovery and lifecycle. Gameplay still belongs to the existing host simulation.</summary>
    public sealed class SteamLobbyService : IDisposable
    {
        private readonly ISteamLobbyApi api;
        private readonly string build;
        private ulong joining,expectedPeer;
        private bool creating,disposed;
        private double deadline;
        private int request;
        public ulong Lobby { get; private set; }
        public bool IsHost { get; private set; }
        public bool Busy=>creating || joining!=0;
        public string Status { get; private set; }="Steam ready.";
        public SteamSessionConnection Connection { get; private set; }
        public event Action Ready,Failed,Changed;
        public event Action<ulong> InviteReceived;
        public SteamLobbyService(ISteamLobbyApi api,string build)
        {this.api=api;this.build=build;api.Created+=Created;api.Entered+=Entered;api.Changed+=LobbyChanged;api.Invited+=Invited;}
        public bool Host(double now,ulong originalPartner=0)
        {
            if(disposed || Busy || Lobby!=0)return false;
            creating=true;expectedPeer=originalPartner;deadline=now+25;Status="Creating a private co-op session...";Changed?.Invoke();api.Create(++request);return true;
        }
        public bool Join(ulong lobby,double now)
        {
            if(disposed || Busy || Lobby!=0 || lobby==0)return false;
            joining=lobby;deadline=now+25;Status="Joining the invited session...";Changed?.Invoke();api.Join(lobby,++request);return true;
        }
        private void Created(int token,ulong lobby,bool success)
        {
            if(token!=request || !creating || disposed){if(success && lobby!=Lobby)api.Leave(lobby);return;}
            creating=false;if(!success){Failure("Steam could not create the session.");return;}
            Lobby=lobby;IsHost=true;
            if(!api.Configure(lobby) || !api.Set(lobby,"protocol",SteamSessionConnection.Protocol) || !api.Set(lobby,"build",build)
                || !api.Set(lobby,"host",api.LocalAccount.ToString()) || !api.Set(lobby,"partner",expectedPeer.ToString()))
            {Failure("Steam could not publish session compatibility information.");return;}
            Connection=new SteamSessionConnection(api.LocalAccount,api.LocalAccount,true,build,IsMember,expectedPeer);
            Status="Waiting for your teammate. Use Invite Friend.";Changed?.Invoke();Ready?.Invoke();
        }
        private void Entered(int token,ulong lobby,bool success)
        {
            if(token!=request || joining==0 || disposed || lobby!=joining){if(success && lobby!=Lobby && lobby!=joining)api.Leave(lobby);return;}
            joining=0;if(!success){Failure("Could not join: the session may be full, closed, or unavailable.");return;}
            Lobby=lobby;
            if(api.Get(lobby,"protocol")!=SteamSessionConnection.Protocol || api.Get(lobby,"build")!=build
                || !ulong.TryParse(api.Get(lobby,"host"),out var host) || host==0 || host==api.LocalAccount || api.Owner(lobby)!=host
                || !ulong.TryParse(api.Get(lobby,"partner"),out var partner) || (partner!=0 && partner!=api.LocalAccount)
                || api.Members(lobby).Length!=2 || !IsMember(host) || !IsMember(api.LocalAccount))
            {Failure("This invitation requires a compatible build and the run's original players.");return;}
            IsHost=false;Connection=new SteamSessionConnection(api.LocalAccount,host,false,build,IsMember);
            Status="Connecting to the host...";Changed?.Invoke();Ready?.Invoke();
        }
        private bool IsMember(ulong peer)=>Lobby!=0 && api.Members(Lobby).Contains(peer);
        private void Invited(ulong lobby){if(!disposed)InviteReceived?.Invoke(lobby);}
        private void LobbyChanged(ulong lobby)
        {
            if(lobby!=Lobby || Connection==null || disposed)return;
            if(api.Owner(lobby)!=Connection.HostAccount || !IsMember(Connection.HostAccount)
                || (Connection.AdmittedPeer!=0 && !IsMember(Connection.AdmittedPeer)))Failure("Your teammate left the session. The unfinished run is retained by the host.");
        }
        public void Tick(double now){if(Busy && now>=deadline)Failure("Steam session connection timed out. Please try again.");}
        public void Invite(){if(IsHost && Lobby!=0 && !Busy)api.Invite(Lobby);}
        public void Cancel()
        {request++;ulong old=Lobby;Lobby=0;joining=0;creating=false;Connection=null;IsHost=false;if(old!=0)api.Leave(old);Status="Session closed.";Changed?.Invoke();}
        private void Failure(string reason){Cancel();Status=reason;Changed?.Invoke();Failed?.Invoke();}
        public void Dispose(){if(disposed)return;Cancel();disposed=true;api.Created-=Created;api.Entered-=Entered;api.Changed-=LobbyChanged;api.Invited-=Invited;api.Dispose();}
    }
}
