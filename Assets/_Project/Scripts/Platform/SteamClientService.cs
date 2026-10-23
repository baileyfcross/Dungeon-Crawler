using System;
using Steamworks;

namespace SpaceCrawler.Platform
{
    public interface ISteamClientApi
    {
        bool Initialize(out string error);
        uint AppId { get; }
        ulong AccountId { get; }
        bool LoggedOn { get; }
        void Pump();
        void Shutdown();
    }

    public sealed class SteamworksClientApi : ISteamClientApi
    {
        public bool Initialize(out string error) => SteamAPI.InitEx(out error) == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        public uint AppId => SteamUtils.GetAppID().m_AppId;
        public ulong AccountId => SteamUser.GetSteamID().m_SteamID;
        public bool LoggedOn => SteamUser.BLoggedOn();
        public void Pump() => SteamAPI.RunCallbacks();
        public void Shutdown() => SteamAPI.Shutdown();
    }

    /// <summary>Owns one application-wide Steam lifetime, independent of gameplay sessions and saves.</summary>
    public sealed class SteamClientService : IDisposable
    {
        public const uint ExpectedAppId = 5488510;
        private readonly ISteamClientApi api;
        private readonly uint expectedAppId;
        private bool initialized, disposed;
        public bool IsReady { get; private set; }
        public string Failure { get; private set; }
        public ulong AccountId { get; private set; }
        public SteamClientService(ISteamClientApi api, uint expectedAppId = ExpectedAppId)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            if (expectedAppId == 0) throw new ArgumentOutOfRangeException(nameof(expectedAppId));
            this.expectedAppId = expectedAppId;
        }
        public bool TryStart()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SteamClientService));
            if (IsReady) return true;
            try
            {
                if (!api.Initialize(out string error)) { Failure = "Steam is unavailable: " + error; return false; }
                initialized = true;
                if (api.AppId != expectedAppId) return Reject("Steam returned a different App ID.");
                if (!api.LoggedOn || api.AccountId == 0) return Reject("Steam is not signed in with a valid account.");
                AccountId = api.AccountId; IsReady = true; Failure = null; return true;
            }
            catch (Exception error) when (error is DllNotFoundException || error is BadImageFormatException || error is EntryPointNotFoundException)
            { return Reject("Steam runtime could not be loaded: " + error.GetType().Name); }
        }
        private bool Reject(string reason) { Failure = reason; Stop(); return false; }
        public bool Online => IsReady && api.LoggedOn;
        public bool IdentityUnchanged() => IsReady && api.AccountId==AccountId;
        public void Pump() { if (IsReady && !disposed) api.Pump(); }
        private void Stop()
        {
            IsReady = false; AccountId = 0;
            if (!initialized) return;
            initialized = false; api.Shutdown();
        }
        public void Dispose() { if (disposed) return; disposed = true; Stop(); }
    }
}
