using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public enum SessionRole { None, Host, Client }

    [Serializable] public sealed class RunSessionDescriptor
    {
        public int protocol = 3, generationVersion, currentRoom, destination, state;
        public int level = 1, sequenceVersion;
        public bool levels;
        public string runSeed;
        public uint epoch;
        public string seed, fingerprint, weapon;
    }
    [Serializable] public sealed class RoomLoadAcknowledgment
    { public uint epoch; public int room, level; public string fingerprint; }

    public struct PlayerInputMessage : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Sequence, AttackSequence;
        public int ClaimedPlayer;
        public bool Active;
        public Vector2 Move, Aim;
    }
    public struct SessionStateHeader : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Tick;
        public int CurrentRoom, State, RoomsEntered, LeftReady, RightReady, Level;
        public bool Running;
    }
    public struct PlayerStateSnapshot : INetworkSerializeByMemcpy
    {
        public int Player;
        public Vector2 Position, Velocity, Aim, AttackOrigin, AttackAim;
        public long Attacks;
        public float Cooldown;
        public bool InputEnabled;
    }
    public struct TargetStateSnapshot : INetworkSerializeByMemcpy
    { public int Identity, Current, Maximum; }
    public struct ProjectileStateSnapshot : INetworkSerializeByMemcpy
    { public int Owner; public long Sequence; public Vector2 Position; }
}
