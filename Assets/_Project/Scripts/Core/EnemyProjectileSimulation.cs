using System;
using System.Numerics;
namespace SpaceCrawler.Core
{
    /// <summary>A hostile shot is semantic authority state. Unity supplies swept collision results.</summary>
    public sealed class EnemyProjectileSimulation
    {
        public Vector2 Position { get; private set; }
        public Vector2 Direction { get; }
        public float Speed { get; }
        public float Remaining { get; private set; } = 3;
        public bool Active { get; private set; } = true;
        public Guid AttackId { get; } = Guid.NewGuid();
        public EnemyProjectileSimulation(Vector2 position,Vector2 direction,float speed)
        {
            if(!PlayerSimulation.IsFinite(position)||!PlayerSimulation.IsFinite(direction)||Math.Abs(direction.LengthSquared()-1)>.001f||float.IsNaN(speed)||float.IsInfinity(speed)||speed<=0)
                throw new ArgumentException("Invalid hostile projectile.");
            Position=position;Direction=direction;Speed=speed;
        }
        public float Distance(float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0)throw new ArgumentException("Invalid projectile tick.");
            return Active?Speed*Math.Min(seconds,Remaining):0;
        }
        public void Advance(float seconds,float travelled,bool collided)
        {
            float maximum=Distance(seconds);
            if(float.IsNaN(travelled)||float.IsInfinity(travelled)||travelled<0||travelled>maximum+.001f)throw new ArgumentException("Invalid collision result.");
            if(!Active)return;
            Position+=Direction*travelled;Remaining=Math.Max(0,Remaining-seconds);Active=!collided&&Remaining>0;
        }
    }
}
