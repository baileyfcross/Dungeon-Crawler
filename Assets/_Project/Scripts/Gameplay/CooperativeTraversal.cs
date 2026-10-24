using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed partial class MultiplayerSession
    {
        private readonly Dictionary<int,RoomLoader> cooperativeRooms=new Dictionary<int,RoomLoader>();
        private readonly int[] playerRooms=new int[2];
        private readonly RoomConnection?[] pendingDoors=new RoomConnection?[2];
        private readonly HashSet<RunRoomId> replicaExplored=new HashSet<RunRoomId>();
        private RunProgression partnerProgression;
        private int roomCacheLevel;
        private int inputRoom=-1,inputLevel;
        public int EncounterVersion { get; private set; }
        public RunDifficulty Difficulty { get; private set; }
        public bool IndependentTravel => sequence!=null && Run!=null && Run.GenerationVersion>=2;
        public RunRoomId PlayerRoom(int slot) => IndependentTravel ? new RunRoomId(playerRooms[slot]) : CurrentRoom;
        public RoomLoader PlayerRoomLoader(int slot) => IndependentTravel ? cooperativeRooms[playerRooms[slot]] : rooms;
        public ExploredMap Map => !RoomReady || Run==null ? null : new ExploredMap(Run,EnteredRooms(),
            new[]{PlayerRoom(0),PlayerRoom(1)});
        private IEnumerable<RunRoomId> EnteredRooms()
        {
            if(Mode==SessionRole.Client)return replicaExplored;
            return IndependentTravel ? Progression.VisitedRooms.Concat(partnerProgression.VisitedRooms).Distinct() : Progression.VisitedRooms;
        }
        private uint RoomIncarnation(int id) => checked((uint)(LevelNumber*64+id+1));
        private void CompleteInitialEntry(RunProgression progression)
        {
            if(progression.State==RoomFlowState.EnteringRoom && !progression.TryCompleteEntry(progression.CurrentRoomId))
                throw new InvalidOperationException("An entered cooperative room could not be committed.");
        }
        private void ClearCooperativeRooms()
        {
            enemies?.ClearRooms();
            foreach(var loader in cooperativeRooms.Values)
            {
                loader.ClearLoadedRoom();
                if(loader!=rooms)Destroy(loader.gameObject);
            }
            cooperativeRooms.Clear();replicaExplored.Clear();roomCacheLevel=LevelNumber;
        }
        private RoomLoader EnsureCooperativeRoom(int id,bool authoritative)
        {
            if(cooperativeRooms.TryGetValue(id,out var existing))return existing;
            var destination=new RunRoomId(id);var node=Run.GetRoom(destination);
            var loader=id==0 && rooms.CurrentRoom==null ? rooms : rooms.CreateSibling(new Vector3(id*64,0,0));
            loader.InitializeCatalog(Run,generation,true);
            var choices=Run.Connections.Where(e=>e.From.Equals(destination)).ToArray();
            loader.LoadSharedRoom(Run,destination,choices,(edge,collider,present)=>ObserveCooperativeDoor(destination,edge,collider,present));
            var room=loader.CurrentRoom;
            IReadOnlyList<EnemySpawn> selected=room.SelectEncounter(Difficulty,destination.Equals(Run.StartRoomId),node.IsTerminal,Run.Seed^(ulong)id,EncounterVersion);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(EnemyFixtureCount>0 || SessionDiagnostics.Option("--session-validation")=="enemies")
                selected=SessionValidationDriver.CreateEnemySpawns(room,EnemyFixtureCount>0?EnemyFixtureCount:1);
#endif
            room.InitializeEncounter(authoritative,RoomIncarnation(id),selected,Difficulty);
            cooperativeRooms.Add(id,loader);
            room.ShowRunExit(LevelNumber==RunSequence.LevelCount && node.IsTerminal);
            if(authoritative)
            {
                foreach(var target in room.Targets)target.Initialize();
                int rewardLevel=LevelNumber;
                persistence.RestoreRoom(rewardLevel,destination,room,()=>TrySave(),a=>RewardEnemy(rewardLevel,destination,a));
            }
            else foreach(var target in room.Targets)target.PresentAuthoritativeHealth(target.InitialHealth,target.InitialHealth,true);
            return loader;
        }
        private void LoadCooperativeWorld(bool placePlayers)
        {
            SetRoomReady(false);Authority?.SetRunning(false,epoch);
            if(roomCacheLevel!=LevelNumber)ClearCooperativeRooms();
            foreach(int id in playerRooms.Distinct())EnsureCooperativeRoom(id,true);
            enemies?.RetainOccupiedRooms(playerRooms.Distinct().Select(id=>cooperativeRooms[id].CurrentRoom));
            for(int i=0;i<2;i++)
            {
                var rig=players[i];rig.Simulation.SetActive(false);rig.Motor.Stop();
                var room=cooperativeRooms[playerRooms[i]].CurrentRoom;
                if(placePlayers || !rig.Combat.Targets.SequenceEqual(room.CombatTargets))
                {rig.ClearRoom();rig.Combat.RegisterRoomTargets(room.CombatTargets,false);}
                if(placePlayers)persistence.PlacePlayer(room,-1,i,2,rig.Motor,rig.Simulation,rig.Combat);
                rig.Motor.Body.simulated=true;rig.Motor.CaptureState();
                if(placePlayers)room.SuppressOccupiedExits(new[]{rig.Motor});
            }
            if(placePlayers)persistence.FinishPlacingPlayers();
            loadedRoom=new RunRoomId(playerRooms[0]);
            Readiness.Reset(loadedRoom,Progression.ConnectionsFrom(loadedRoom),Authority.BothConnected);
            Physics2D.SyncTransforms();follower.Follow();entryStarted=Time.realtimeSinceStartupAsDouble;
        }
        private void ObserveCooperativeDoor(RunRoomId source,RoomConnection edge,Collider2D collider,bool present)
        {
            if(Mode!=SessionRole.Host || Authority==null || !present || !roomReady || RunEnded || collider==null || !Authority.BothConnected)return;
            for(int i=0;i<2;i++)
                if(playerRooms[i]==source.Value && collider.attachedRigidbody==players[i].Motor.Body && Life[i].State==PlayerLifeState.Alive)
                    pendingDoors[i]=edge;
        }
        private void AdvanceCooperativeTravel()
        {
            bool changed=false;
            for(int i=0;i<2;i++)
            {
                var requested=pendingDoors[i];pendingDoors[i]=null;
                if(!requested.HasValue || Life[i].State!=PlayerLifeState.Alive || !Authority.InputEnabled(i))continue;
                var edge=requested.Value;var progression=i==0?Progression:partnerProgression;
                if(progression.CurrentRoomId.Value!=edge.From.Value || !TrySave())continue;
                if(!progression.TryRequestExit(new ExitIntent(new PlayerIdentity(i+1),edge.From,edge.To))
                    || !progression.TryBeginAcceptedEntry() || !progression.TryCompleteEntry(edge.To))continue;
                changed=true;playerRooms[i]=edge.To.Value;
                var destination=EnsureCooperativeRoom(edge.To.Value,true).CurrentRoom;
                players[i].ClearRoom();players[i].Motor.PlaceAt(destination.Arrival(edge.ToPort,i,2));
                players[i].Motor.CaptureState();destination.SuppressOccupiedExits(new[]{players[i].Motor});
            }
            bool nextLevel=LevelNumber<RunSequence.LevelCount && Progression.IsTerminal && partnerProgression.IsTerminal
                && Life[0].State==PlayerLifeState.Alive && Life[1].State==PlayerLifeState.Alive;
            if(!changed && !nextLevel)return;
            epoch++;
            if(nextLevel)
            {
                // Both routes end in this level's exit; neither player can enter the next level alone.
                CaptureCooperativeRooms();
                if(!sequence.TryAdvance(Progression))throw new InvalidOperationException("Cooperative level exit was not ready.");
                Run=sequence.CurrentGraph;Fingerprint=GraphFingerprint.Compute(Run);
                Progression=new RunProgression(Run,new PlayerIdentity(1));partnerProgression=new RunProgression(Run,new PlayerIdentity(2));
                Progression.SetGameplayActive(true);partnerProgression.SetGameplayActive(true);
                playerRooms[0]=playerRooms[1]=Run.StartRoomId.Value;
                LoadCooperativeWorld(true);
            }
            else LoadCooperativeWorld(false);
            SendDescriptor();
        }
        private void ReceiveCooperativeRooms(RunSessionDescriptor incoming)
        {
            if(incoming.playerRooms==null || incoming.playerRooms.Length!=2 || incoming.explored==null || incoming.explored.Length>Run.Nodes.Count)
                throw new InvalidOperationException("Invalid explored-room descriptor.");
            if(roomCacheLevel!=LevelNumber)ClearCooperativeRooms();
            var entered=new HashSet<RunRoomId>();
            foreach(int id in incoming.explored)entered.Add(Run.GetRoom(new RunRoomId(id)).Id);
            foreach(int id in incoming.playerRooms)entered.Add(Run.GetRoom(new RunRoomId(id)).Id);
            if(!entered.Contains(Run.StartRoomId))throw new InvalidOperationException("Exploration must include the starting room.");
            SetRoomReady(false);ClearReplicaProjectiles();
            for(int i=0;i<2;i++){playerRooms[i]=incoming.playerRooms[i];players[i].ClearRoom();EnsureCooperativeRoom(playerRooms[i],false);}
            SetRoomReady(false);
            replicaExplored.Clear();replicaExplored.UnionWith(entered);
            loadedRoom=new RunRoomId(incoming.destination);
            if(loadedRoom.Value!=playerRooms[0])throw new InvalidOperationException("Host room descriptor mismatch.");
            SendJson(AckMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new RoomLoadAcknowledgment{
                epoch=epoch,room=loadedRoom.Value,level=LevelNumber,fingerprint=Fingerprint,profileId=profileId.ToString("N"),classId=selectedClass}));
            Changed?.Invoke();
        }
        private void AdvanceCooperativeEnemies()
        {
            if(Mode!=SessionRole.Host || !roomReady || stopped || stopRequested || RunEnded || Life.Ended)return;
            enemies.RetainOccupiedRooms(playerRooms.Distinct().Select(id=>cooperativeRooms[id].CurrentRoom));
            foreach(int id in playerRooms.Distinct())enemies.Advance(cooperativeRooms[id].CurrentRoom,true,Time.fixedDeltaTime);
        }
        private void CaptureCooperativeRooms()
        { foreach(var entry in cooperativeRooms)persistence.CaptureRoom(LevelNumber,new RunRoomId(entry.Key),entry.Value.CurrentRoom); }
        private void SaveCooperativeWorld()
        {
            if(Mode!=SessionRole.Host || Life==null || Life.Ended || cooperativeRooms.Count==0
                || Progression.State==RoomFlowState.EnteringRoom || partnerProgression.State==RoomFlowState.EnteringRoom)return;
            CaptureCooperativeRooms();
            var world=persistence.CapturePlayers(players.Select(p=>p.Motor).ToArray(),players.Select(p=>p.Simulation).ToArray(),players.Select(p=>p.Combat).ToArray(),
                playerRooms.Select(id=>cooperativeRooms[id].CurrentRoom).ToArray());
            if(commitCheckpoint!=null)persistence.Commit(RunCheckpoint.Capture(sequence,Progression,SavedSessionMode.Cooperative,
                new[]{weapon.Id,weapon.Id},Life.Capture(),world,Difficulty,partnerProgression.VisitedRooms,EncounterVersion,RewardPolicy),commitCheckpoint);
        }
        private void SendCooperativeState()
        {
            if(!remoteSender.HasValue || !network.IsListening || !roomReady)return;
            var header=new SessionStateHeader{Epoch=epoch,Tick=++tick,CurrentRoom=playerRooms[0],Level=LevelNumber,Running=true,
                State=(int)Progression.State,RoomsEntered=Progression.RoomsEntered,RunXp=Life.RunXp,Ending=Life.Ended,
                CompletionAvailable=Completion.Available,CompletionConfirmations=Completion.Confirmations};lastHeader=header;
            using(var writer=new FastBufferWriter(16384,Allocator.Temp))
            {
                writer.WriteValueSafe(header);
                for(int i=0;i<2;i++)writer.WriteValueSafe(players[i].Capture(Authority.InputEnabled(i)));
                var occupied=playerRooms.Distinct().ToArray();writer.WriteValueSafe(occupied.Length);
                foreach(int id in occupied)
                {
                    var room=cooperativeRooms[id].CurrentRoom;writer.WriteValueSafe(id);writer.WriteValueSafe(room.Targets.Count);
                    foreach(var target in room.Targets)writer.WriteValueSafe(new TargetStateSnapshot{Identity=target.Identity.Value,Current=target.Health.Current,Maximum=target.Health.Maximum});
                    writer.WriteValueSafe(room.Encounter.Enemies.Count);
                    foreach(var enemy in room.Encounter.Enemies)writer.WriteValueSafe(new EnemyStateSnapshot{Encounter=enemy.Identity.Encounter,Spawn=enemy.Identity.Spawn,Archetype=(int)enemy.Archetype,
                        Restored=enemy.Restored,Revision=enemy.Revision,Current=enemy.Target.Health.Current,Maximum=enemy.Target.Health.Maximum,
                        Lifecycle=(int)enemy.Lifecycle,Aggro=enemy.AggroPlayer,Position=enemy.transform.position,Behaviour=(int)enemy.BehaviourState,
                        Facing=enemy.Facing,WindupProgress=enemy.WindupProgress,StrikeSequence=enemy.StrikeSequence});
                }
                writer.WriteValueSafe(players.Sum(p=>p.Combat.Projectiles.Count));
                foreach(var rig in players)foreach(var projectile in rig.Combat.Projectiles)
                    writer.WriteValueSafe(new ProjectileStateSnapshot{Owner=rig.State.Player.Value,Sequence=projectile.Execution.Sequence,Position=projectile.Position});
                WriteEnemyProjectiles(writer);
                network.CustomMessagingManager.SendNamedMessage(StateMessage,remoteSender.Value,writer,
                    writer.Length>1200?NetworkDelivery.ReliableFragmentedSequenced:NetworkDelivery.UnreliableSequenced);
            }
            Changed?.Invoke();
        }
        private void ReceiveCooperativeState(ulong sender,FastBufferReader reader)
        {
            if(Mode!=SessionRole.Client || sender!=NetworkManager.ServerClientId)return;
            try
            {
                reader.ReadValueSafe(out SessionStateHeader header);
                if(header.Epoch!=epoch || header.Level!=LevelNumber || header.Tick<=lastTick || header.CurrentRoom!=playerRooms[0] || !header.Running)return;
                if(header.RunXp<0 || header.RunXp>long.MaxValue/100)throw new InvalidOperationException("Invalid run experience.");
                lastTick=header.Tick;lastHeader=header;
                if(header.Ending && !RunEnded && (flow.State==ApplicationState.Gameplay || flow.State==ApplicationState.Paused))flow.BeginRunEnd();
                for(int i=0;i<2;i++){reader.ReadValueSafe(out PlayerStateSnapshot state);players[i].Apply(state);}
                reader.ReadValueSafe(out int count);
                if(count!=playerRooms.Distinct().Count())throw new InvalidOperationException("Invalid occupied room count.");
                var seen=new HashSet<int>();
                for(int n=0;n<count;n++)
                {
                    reader.ReadValueSafe(out int id);
                    if(!playerRooms.Contains(id) || !seen.Add(id))throw new InvalidOperationException("Invalid occupied room identity.");
                    var room=cooperativeRooms[id].CurrentRoom;
                    reader.ReadValueSafe(out int targets);if(targets!=room.Targets.Count)throw new InvalidOperationException("Room target count mismatch.");
                    for(int i=0;i<targets;i++)
                    {
                        reader.ReadValueSafe(out TargetStateSnapshot target);
                        if(target.Identity!=room.Targets[i].Identity.Value)throw new InvalidOperationException("Room target identity mismatch.");
                        room.Targets[i].PresentAuthoritativeHealth(target.Current,target.Maximum);
                    }
                    reader.ReadValueSafe(out int enemies);
                    if(enemies!=room.Encounter.Enemies.Count)throw new InvalidOperationException("Room enemy count mismatch.");
                    for(int i=0;i<enemies;i++)
                    {
                        reader.ReadValueSafe(out EnemyStateSnapshot enemy);var actor=room.Encounter.Enemies[i];
                        if(enemy.Encounter!=RoomIncarnation(id) || enemy.Spawn!=actor.Identity.Spawn || enemy.Archetype!=(int)actor.Archetype || float.IsNaN(enemy.Position.sqrMagnitude) || float.IsInfinity(enemy.Position.sqrMagnitude))
                            throw new InvalidOperationException("Room enemy identity or position mismatch.");
                        actor.ApplySnapshot(actor.Identity,(EnemyLifecycle)enemy.Lifecycle,enemy.Current,enemy.Maximum,enemy.Revision,enemy.Aggro,enemy.Restored);
                        actor.transform.position=enemy.Position;actor.ApplyBehaviourSnapshot(enemy.Behaviour,enemy.Facing,enemy.WindupProgress,enemy.StrikeSequence);
                    }
                }
                reader.ReadValueSafe(out int projectiles);
                if(projectiles<0 || projectiles>32)throw new InvalidOperationException("Invalid projectile count.");
                var present=new HashSet<(int,long)>();
                for(int i=0;i<projectiles;i++)
                {
                    reader.ReadValueSafe(out ProjectileStateSnapshot projectile);
                    if(projectile.Owner<1 || projectile.Owner>2 || float.IsNaN(projectile.Position.sqrMagnitude) || float.IsInfinity(projectile.Position.sqrMagnitude))
                        throw new InvalidOperationException("Invalid projectile owner or position.");
                    var key=(projectile.Owner,projectile.Sequence);present.Add(key);
                    if(!projectileViews.TryGetValue(key,out var view))
                    {view=Instantiate(playerPrefab.Combat.ProjectilePrefab,transform);view.enabled=false;projectileViews.Add(key,view);}
                    view.transform.position=projectile.Position;
                }
                foreach(var key in projectileViews.Keys.Where(k=>!present.Contains(k)).ToArray())
                {Destroy(projectileViews[key].gameObject);projectileViews.Remove(key);}
                ReadEnemyProjectiles(reader);
                SetRoomReady(true);Changed?.Invoke();
            }
            catch(Exception error){Fail("Rejected cooperative state: "+error.Message);}
        }
    }
}
