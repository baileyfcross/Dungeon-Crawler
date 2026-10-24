using System;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    public sealed class ExploredMinimap : VisualElement
    {
        private readonly VisualElement canvas;
        private readonly Label caption;
        private string rendered;
        public ExploredMinimap()
        {
            name = "explored-minimap"; pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.right = 32; style.bottom = 94;
            style.width = 230; style.height = 206; style.backgroundColor = new Color(.04f,.075f,.12f,.92f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;
            caption = new Label { pickingMode = PickingMode.Ignore };
            caption.style.color = new Color(.8f,.9f,.95f); caption.style.fontSize = 12;
            caption.style.marginLeft = 10; caption.style.marginTop = 8;
            Add(caption);
            canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            canvas.style.position = Position.Absolute; canvas.style.left = 10; canvas.style.top = 32;
            canvas.style.width = 210; canvas.style.height = 142; Add(canvas);
            var legend = new Label("● P1     ● P2") { pickingMode = PickingMode.Ignore };
            legend.style.position = Position.Absolute; legend.style.bottom = 7; legend.style.left = 12;
            legend.style.color = PlayerColor(1); legend.style.fontSize = 12;
            legend.text = "P1 teal  ·  P2 purple"; Add(legend);
        }
        public static Color PlayerColor(int identity) => identity == 1 ? new Color(.3f,.86f,.83f) : new Color(.87f,.56f,.95f);

        public void Present(ExploredMap map, int level, int localPlayer)
        {
            if (map == null) { style.display = DisplayStyle.None; rendered = null; return; }
            style.display = DisplayStyle.Flex;
            var key = level + ":" + string.Join(",",map.Rooms.Select(n => n.Id.Value)) + ":" + string.Join(",",map.PlayerRooms.Select(n => n.Value)) + ":" + localPlayer;
            if (key == rendered) return;
            rendered = key; canvas.Clear(); caption.text = $"LEVEL {level}  ·  {map.Rooms.Count} explored";
            float minX=map.Rooms.Min(n=>n.LayoutX), maxX=map.Rooms.Max(n=>n.LayoutX);
            float minY=map.Rooms.Min(n=>n.LayoutY), maxY=map.Rooms.Max(n=>n.LayoutY);
            float scale=Mathf.Min(182/Mathf.Max(1,maxX-minX),114/Mathf.Max(1,maxY-minY));
            Vector2 Point(RoomNode n) => new Vector2(105+(n.LayoutX-(minX+maxX)/2)*scale,71-(n.LayoutY-(minY+maxY)/2)*scale);
            foreach (var edge in map.Connections)
            {
                var a=Point(map.Rooms.First(n=>n.Id.Equals(edge.From))); var b=Point(map.Rooms.First(n=>n.Id.Equals(edge.To)));
                var line=Box("map-connection",a.x,a.y,Vector2.Distance(a,b),2,new Color(.32f,.43f,.5f));
                line.style.transformOrigin=new TransformOrigin(0,0,0);
                line.style.rotate=new Rotate(new Angle(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg));
            }
            foreach(var node in map.Rooms)
            {
                var p=Point(node); var current=node.Id.Equals(map.PlayerRooms[localPlayer-1]);
                var box=Box("map-room-"+node.Id.Value,p.x-9,p.y-7,18,14,current?new Color(.5f,.65f,.72f):new Color(.2f,.31f,.4f));
                box.tooltip="Explored room";
                for(int i=0;i<map.PlayerRooms.Count;i++)if(node.Id.Equals(map.PlayerRooms[i]))
                {
                    var marker=new Label((i+1).ToString()){name="map-player-"+(i+1),pickingMode=PickingMode.Ignore};
                    marker.style.position=Position.Absolute;marker.style.left=map.PlayerRooms.Count==2?i*9:5;marker.style.top=-2;
                    marker.style.fontSize=12;marker.style.unityFontStyleAndWeight=FontStyle.Bold;marker.style.color=PlayerColor(i+1);box.Add(marker);
                }
            }
        }
        private VisualElement Box(string id,float x,float y,float width,float height,Color color)
        {
            var box=new VisualElement{name=id,pickingMode=PickingMode.Ignore};
            box.style.position=Position.Absolute;box.style.left=x;box.style.top=y;box.style.width=width;box.style.height=height;box.style.backgroundColor=color;
            canvas.Add(box);return box;
        }
    }
}
