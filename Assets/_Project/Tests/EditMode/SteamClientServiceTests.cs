using System;
using NUnit.Framework;
using SpaceCrawler.Platform;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class SteamClientServiceTests
    {
        private sealed class Api : ISteamClientApi
        {
            public bool Available=true;
            public bool LoggedOn { get; set; }=true;
            public uint AppId { get; set; }=SteamClientService.ExpectedAppId;
            public ulong AccountId { get; set; }=123;
            public int Starts, Pumps, Stops;
            public Exception LoadFailure;
            public bool Initialize(out string error) { Starts++; error="client unavailable"; if(LoadFailure!=null)throw LoadFailure; return Available; }
            public void Pump()=>Pumps++;
            public void Shutdown()=>Stops++;
        }
        [Test] public void MissingClientDoesNotExposeIdentityOrCallUninitializedApisAndCanRetry()
        {
            var api=new Api{Available=false};using(var service=new SteamClientService(api))
            {
                Assert.That(service.TryStart(),Is.False);service.Pump();Assert.That(api.Pumps,Is.Zero);Assert.That(service.AccountId,Is.Zero);
                Assert.That(api.Stops,Is.Zero);api.Available=true;Assert.That(service.TryStart(),Is.True);
                Assert.That(service.Failure,Is.Null);Assert.That(service.AccountId,Is.EqualTo(123));
            }
            Assert.That(api.Stops,Is.EqualTo(1));
        }
        [TestCase(480u,true,123ul)] [TestCase(5488510u,false,123ul)] [TestCase(5488510u,true,0ul)]
        public void WrongApplicationOrInvalidIdentityFailsClosed(uint app,bool loggedOn,ulong account)
        {
            var api=new Api{AppId=app,LoggedOn=loggedOn,AccountId=account};using(var service=new SteamClientService(api))
            { Assert.That(service.TryStart(),Is.False);Assert.That(service.IsReady,Is.False);Assert.That(service.AccountId,Is.Zero);Assert.That(api.Stops,Is.EqualTo(1)); }
            Assert.That(api.Stops,Is.EqualTo(1));
        }
        [Test] public void LifetimeIsIdempotentAndCallbacksRunOnlyWhileReady()
        {
            var api=new Api();var service=new SteamClientService(api);Assert.That(service.TryStart(),Is.True);Assert.That(service.TryStart(),Is.True);
            service.Pump();Assert.That(api.Starts,Is.EqualTo(1));Assert.That(api.Pumps,Is.EqualTo(1));
            service.Dispose();service.Dispose();service.Pump();Assert.That(api.Stops,Is.EqualTo(1));Assert.That(api.Pumps,Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(()=>service.TryStart());
        }
        [Test] public void CandidateAppIdRequiresExplicitConfigurationAndCannotSilentlyReplaceDefault()
        {
            var api=new Api{AppId=123456};
            using(var original=new SteamClientService(api)) { Assert.That(original.TryStart(),Is.False); }
            using(var candidate=new SteamClientService(api,123456)) { Assert.That(candidate.TryStart(),Is.True); }
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SteamClientService(api,0));
        }
        [Test] public void NetworkOfflineDoesNotChangeProfileOwnershipButDifferentAccountDoes()
        {
            var api=new Api();using(var service=new SteamClientService(api))
            {
                Assert.That(service.TryStart(),Is.True);api.LoggedOn=false;
                Assert.That(service.Online,Is.False);Assert.That(service.IdentityUnchanged(),Is.True);
                api.AccountId=456;Assert.That(service.IdentityUnchanged(),Is.False);
            }
        }
        [Test] public void MissingNativeLibraryIsReportedWithoutClaimingSteamAvailable()
        {
            var api=new Api{LoadFailure=new DllNotFoundException()};using(var service=new SteamClientService(api))
            { Assert.That(service.TryStart(),Is.False);StringAssert.Contains("DllNotFoundException",service.Failure);Assert.That(api.Stops,Is.Zero); }
        }
    }
}
