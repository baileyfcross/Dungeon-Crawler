using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public enum SpikeMode { None, Host, Client }

    [Serializable] public sealed class SpikeRunDescriptor
    {
        public int protocol = 1, generationVersion, currentRoom, destination, state;
        public uint epoch;
        public string seed, fingerprint, weapon;
    }
    [Serializable] public sealed class SpikeRoomAck
    { public uint epoch; public int room; public string fingerprint; }

    public struct SpikeInput : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Sequence, AttackSequence;
        public int ClaimedPlayer;
        public bool Active;
        public Vector2 Move, Aim;
    }
    public struct SpikeStateHeader : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Tick;
        public int CurrentRoom, State, RoomsEntered, LeftReady, RightReady;
        public bool Running;
    }
    public struct SpikePlayerState : INetworkSerializeByMemcpy
    {
        public int Player;
        public Vector2 Position, Velocity, Aim, AttackOrigin, AttackAim;
        public long Attacks;
        public float Cooldown;
        public bool InputEnabled;
    }
    public struct SpikeTargetState : INetworkSerializeByMemcpy
    { public int Identity, Current, Maximum; }
    public struct SpikeProjectileState : INetworkSerializeByMemcpy
    { public int Owner; public long Sequence; public Vector2 Position; }
}
