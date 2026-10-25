using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public enum SessionRole { None, Host, Client }

    [Serializable] public sealed class RunSessionDescriptor
    {
        public int protocol = 14, generationVersion, currentRoom, destination, state;
        public string runId;
        public int level = 1, sequenceVersion;
        public bool levels;
        public string runSeed;
        public uint epoch;
        public string seed, fingerprint, weapon;
        public int difficulty,encounterVersion,rewardVersion,rewardBaseXp;
        public float[] rewardFactors;
        public int[] playerRooms, explored;
        public string[] classes;
        public UpgradeData upgrades;
        public bool classesLocked;
    }
    [Serializable] public sealed class RoomLoadAcknowledgment
    { public uint epoch; public int room, level; public string fingerprint, profileId, classId; }
    [Serializable] public sealed class RunEndMessage
    { public string runId; public long xp; public int reason, percent; public string[] profiles; }
    [Serializable] public sealed class RewardAcknowledgment
    { public string runId, profileId; }

    public struct PlayerInputMessage : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Sequence, AttackSequence;
        public int ClaimedPlayer;
        public bool Active, InteractHeld, Sprint;
        public Vector2 Move, Aim;
    }
    public struct SessionStateHeader : INetworkSerializeByMemcpy
    {
        public uint Epoch;
        public ulong Tick;
        public int CurrentRoom, State, RoomsEntered, LeftReady, RightReady, ThirdReady, FourthReady, Level;
        public bool Running, Ending, CompletionAvailable;
        public int CompletionConfirmations;
        public long RunXp;
    }
    public struct PlayerStateSnapshot : INetworkSerializeByMemcpy
    {
        public int Player;
        public Vector2 Position, Velocity, Aim, AttackOrigin, AttackAim;
        public long Attacks;
        public float Cooldown;
        public bool InputEnabled;
        public int Health, MaximumHealth, LifeState;
        public float ReviveProgress;
    }
    public struct TargetStateSnapshot : INetworkSerializeByMemcpy
    { public int Identity, Current, Maximum; }
    public struct EnemyStateSnapshot : INetworkSerializeByMemcpy
    {
        public uint Encounter, Revision;
        public int Archetype, Spawn, Current, Maximum, Lifecycle, Aggro;
        public bool Restored;
        public Vector2 Position;
        public int Behaviour;
        public Vector2 Facing;
        public float WindupProgress;
        public long StrikeSequence;
    }
    public struct EnemyProjectileSnapshot : INetworkSerializeByMemcpy
    {public uint Encounter;public int Spawn;public long Sequence;public Vector2 Position,Direction;}
    public struct ProjectileStateSnapshot : INetworkSerializeByMemcpy
    { public int Owner; public long Sequence; public Vector2 Position; }
}
