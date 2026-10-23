using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceCrawler.Platform;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class SteamLobbyTests
    {
        private sealed class Api : ISteamLobbyApi
        {
            public ulong LocalAccount{get;set;}=111;
            public ulong Host=111;public ulong[] People=new[]{111ul,222ul};
            public readonly Dictionary<string,string> Data=new Dictionary<string,string>();
            public readonly List<ulong> Left=new List<ulong>();public int Request;
            public event Action<int,ulong,bool> Created,Entered;public event Action<ulong> Changed,Invited;
            public void Create(int request){Request=request;}public void Join(ulong lobby,int request){Request=request;}
            public void Leave(ulong lobby)=>Left.Add(lobby);public bool Configure(ulong lobby)=>true;
            public bool Set(ulong lobby,string key,string value){Data[key]=value;return true;}
            public string Get(ulong lobby,string key)=>Data.TryGetValue(key,out var value)?value:null;
            public ulong Owner(ulong lobby)=>Host;public ulong[] Members(ulong lobby)=>People;
            public void Invite(ulong lobby){}public void Dispose(){}
            public void CompleteCreate(int request,ulong lobby)=>Created?.Invoke(request,lobby,true);
            public void CompleteJoin(ulong lobby)=>Entered?.Invoke(Request,lobby,true);
            public void Membership()=>Changed?.Invoke(7);public void Invitation(ulong lobby)=>Invited?.Invoke(lobby);
        }
        [Test] public void ColdLaunchInvitationsAcceptOnlyTheExplicitNumericLobbyArgument()
        {
            Assert.That(SteamLobbyApi.ParseInvitation("game.exe +connect_lobby 123456"),Is.EqualTo(123456));
            Assert.That(SteamLobbyApi.ParseInvitation("+connect_lobby \"123456\""),Is.EqualTo(123456));
            Assert.That(SteamLobbyApi.ParseInvitation("+connect_lobby -1"),Is.Zero);
            Assert.That(SteamLobbyApi.ParseInvitation("+connect_lobby invalid"),Is.Zero);
            Assert.That(SteamLobbyApi.ParseInvitation("+connect_lobby"),Is.Zero);
        }
        [Test] public void HostPublishesCompatibilityAndLateCancelledCallbacksCannotStartOrReplaceSessions()
        {
            var api=new Api();using(var service=new SteamLobbyService(api,"build"))
            {
                int ready=0;service.Ready+=()=>ready++;
                service.Host(0);int old=api.Request;service.Cancel();service.Host(1);int current=api.Request;
                api.CompleteCreate(old,6);Assert.That(api.Left,Does.Contain(6ul));Assert.That(ready,Is.Zero);
                api.CompleteCreate(current,7);Assert.That(ready,Is.EqualTo(1));Assert.That(service.Lobby,Is.EqualTo(7));
                Assert.That(api.Data["protocol"],Is.EqualTo(SteamSessionConnection.Protocol));Assert.That(api.Data["build"],Is.EqualTo("build"));
                Assert.That(service.Host(2),Is.False);
            }
            Assert.That(api.Left,Does.Contain(7ul));
        }
        [Test] public void JoinRejectsWrongBuildOriginalPartnerAndHostMigration()
        {
            foreach(string mismatch in new[]{"build","partner","host"})
            {
                var api=new Api{LocalAccount=222};api.Data["protocol"]=SteamSessionConnection.Protocol;api.Data["build"]="build";api.Data["host"]="111";api.Data["partner"]="222";
                api.Data[mismatch]=mismatch=="build"?"old":"333";
                using(var service=new SteamLobbyService(api,"build")){int ready=0;service.Ready+=()=>ready++;service.Join(7,0);api.CompleteJoin(7);Assert.That(ready,Is.Zero);Assert.That(service.Lobby,Is.Zero);Assert.That(api.Left,Does.Contain(7ul));}
            }
        }
        [Test] public void TimeoutAndInvitationsDoNotSilentlyReplaceAnActiveSession()
        {
            var api=new Api();using(var service=new SteamLobbyService(api,"build"))
            {
                service.Host(0);service.Tick(26);Assert.That(service.Busy,Is.False);
                service.Host(27);api.CompleteCreate(api.Request,7);ulong invitation=0;service.InviteReceived+=value=>invitation=value;
                api.Invitation(9);Assert.That(invitation,Is.EqualTo(9));Assert.That(service.Lobby,Is.EqualTo(7));
                Assert.That(service.Join(9,28),Is.False);api.Host=222;api.Membership();Assert.That(service.Lobby,Is.Zero);
            }
        }
        [Test] public void AdmissionUsesAuthenticatedTransportIdentityAndPinsProfileAndOriginalPartner()
        {
            Guid profile=Guid.NewGuid();var client=new SteamSessionConnection(222,111,false,"build",id=>id==111);
            var host=new SteamSessionConnection(111,111,true,"build",id=>id==222 || id==333,222);
            Assert.That(host.Approve(333,client.Payload(profile),out _),Is.False);
            Assert.That(host.Approve(222,client.Payload(profile),out _),Is.True);
            Assert.Throws<InvalidOperationException>(()=>host.ValidateProfile(222,Guid.NewGuid()));
            Assert.That(host.Approve(222,client.Payload(Guid.NewGuid()),out _),Is.False);
            Assert.That(host.Approve(222,new SteamSessionConnection(222,111,false,"old",id=>true).Payload(profile),out _),Is.False);
            Assert.That(client.Allows(333),Is.False);Assert.That(client.Allows(111),Is.True);
            int writes=0;host.RecordParticipants=(run,owner,peer,id)=>writes++;host.BindParticipants(Guid.NewGuid(),Guid.NewGuid(),222,profile);
            Assert.That(writes,Is.EqualTo(1));Assert.That(host.ExpectedPeer,Is.EqualTo(222));
        }
    }
}
