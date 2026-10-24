using UnityEngine;
namespace SpaceCrawler.Gameplay
{
    /// <summary>Visible shot only: no collider, simulation, or damage callbacks.</summary>
    public sealed class EnemyProjectileView : MonoBehaviour
    {
        private LineRenderer line;
        public static EnemyProjectileView Create(Transform parent,Material material)
        {
            var root=new GameObject("EnemyProjectile");root.transform.SetParent(parent,false);
            var view=root.AddComponent<EnemyProjectileView>();view.line=root.AddComponent<LineRenderer>();
            view.line.sharedMaterial=material;view.line.widthMultiplier=.22f;view.line.positionCount=2;
            view.line.sortingOrder=8;view.line.numCapVertices=4;view.line.startColor=view.line.endColor=new Color(1,.25f,.8f);
            return view;
        }
        public void Present(Vector2 position,Vector2 direction)
        {transform.position=position;line.SetPosition(0,position-direction*.16f);line.SetPosition(1,position+direction*.16f);}
    }
}
