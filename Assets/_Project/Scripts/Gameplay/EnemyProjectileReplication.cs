using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using Unity.Netcode;
using UnityEngine;
namespace SpaceCrawler.Gameplay
{
    public sealed partial class MultiplayerSession
    {
        private readonly Dictionary<long,EnemyProjectileView> enemyProjectileViews=new Dictionary<long,EnemyProjectileView>();
        public int HostileProjectileCount => Mode==SessionRole.Host ? enemies?.ProjectileCount ?? 0 : enemyProjectileViews.Count;
        private void WriteEnemyProjectiles(FastBufferWriter writer)
        {
            var values=enemies?.ProjectileSnapshots.ToArray() ?? Array.Empty<EnemyProjectileSnapshot>();
            writer.WriteValueSafe(values.Length);foreach(var value in values)writer.WriteValueSafe(value);
        }
        private void ReadEnemyProjectiles(FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);if(count<0||count>32)throw new InvalidOperationException("Invalid hostile projectile count.");
            var present=new HashSet<long>();
            for(int i=0;i<count;i++)
            {
                reader.ReadValueSafe(out EnemyProjectileSnapshot value);
                if(value.Sequence<=0 || !present.Add(value.Sequence) || !float.IsFinite(value.Position.sqrMagnitude)
                    || !float.IsFinite(value.Direction.sqrMagnitude) || Mathf.Abs(value.Direction.sqrMagnitude-1)>.001f)
                    throw new InvalidOperationException("Invalid hostile projectile state.");
                var actor=(IndependentTravel?cooperativeRooms.Values.SelectMany(r=>r.CurrentRoom.Encounter.Enemies):rooms.CurrentRoom.Encounter.Enemies)
                    .SingleOrDefault(e=>e.Identity.Encounter==value.Encounter && e.Identity.Spawn==value.Spawn);
                if(actor==null || actor.Archetype!=EnemyArchetype.Ranged)throw new InvalidOperationException("Unknown hostile projectile source.");
                var room=actor.GetComponentInParent<AuthoredRoom>();
                if(!room.PlayableBounds.Contains(room.transform.InverseTransformPoint(value.Position)))throw new InvalidOperationException("Projectile outside its room.");
                if(!enemyProjectileViews.TryGetValue(value.Sequence,out var view))
                {view=EnemyProjectileView.Create(transform,actor.GetComponentInChildren<LineRenderer>().sharedMaterial);enemyProjectileViews.Add(value.Sequence,view);}
                view.Present(value.Position,value.Direction);
            }
            foreach(long key in enemyProjectileViews.Keys.Where(k=>!present.Contains(k)).ToArray())
            {Destroy(enemyProjectileViews[key].gameObject);enemyProjectileViews.Remove(key);}
        }
        private void ClearEnemyProjectileViews()
        {foreach(var view in enemyProjectileViews.Values)if(view!=null)Destroy(view.gameObject);enemyProjectileViews.Clear();}
    }
}
