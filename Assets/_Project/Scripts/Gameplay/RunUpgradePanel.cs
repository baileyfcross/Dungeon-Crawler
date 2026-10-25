using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Displays authoritative offers and submits one identified choice; never changes stats.</summary>
    public sealed class RunUpgradePanel : MonoBehaviour
    {
        private ApplicationFlow flow;
        private Func<RunUpgrades> state;
        private Func<int> slot;
        private Func<bool> ready;
        private Func<int,bool> select;
        private Func<string> failure;
        private Func<RunStatusPresentation> readStatus;
        private VisualElement host,overlay;
        private readonly Button[] offers=new Button[3];
        private Button confirm;
        private Label title,status;
        private readonly List<Tuple<VisualElement,bool>> background=new List<Tuple<VisualElement,bool>>();
        private int draft,shownLevel,lastPauseFrame=-1;
        private bool submitted;
        public bool IsOpen { get; private set; }
        public void Bind(VisualElement view,ApplicationFlow application,Func<RunUpgrades> read,Func<int> localSlot,Func<bool> available,Func<int,bool> choose,Func<string> notice,Func<RunStatusPresentation> presentation)
        {
            flow=application;state=read;slot=localSlot;ready=available;select=choose;failure=notice;readStatus=presentation;
            host=view.Q(className:"gameplay-root") ?? view;
            overlay=new VisualElement{name="run-upgrade-overlay"};overlay.AddToClassList("pause-overlay");overlay.style.display=DisplayStyle.None;
            var card=new VisualElement();card.AddToClassList("pause-card");card.style.width=540;overlay.Add(card);
            title=new Label();title.AddToClassList("title");title.style.fontSize=30;card.Add(title);
            var hint=new Label("Choose one upgrade for this run. Confirmed choices cannot be changed.");hint.AddToClassList("description");hint.style.fontSize=15;card.Add(hint);
            for(int i=0;i<3;i++)
            {
                int id=i+1;var button=new Button(()=>{draft=id;Refresh();}){name="upgrade-"+id,text=RunUpgrades.Name(id)+"\n"+RunUpgrades.Description(id)};
                button.style.height=72;button.style.fontSize=14;button.style.whiteSpace=WhiteSpace.Normal;offers[i]=button;card.Add(button);
            }
            var stacking=new Label("Movement and damage bonuses add to base stats. Damage rounds once. Sprint stays ×1.5."){name="upgrade-stacking-hint"};
            stacking.AddToClassList("description");stacking.style.fontSize=13;stacking.style.marginBottom=12;card.Add(stacking);
            confirm=new Button(()=>{if(draft>0 && select(draft))submitted=true;Refresh();}){name="confirm-upgrade",text="Confirm Upgrade"};confirm.AddToClassList("primary");card.Add(confirm);
            var menu=new Button(()=>flow.TryRequest(ApplicationCommand.ReturnToMainMenu)){name="upgrade-return-to-menu",text="Save & Return to Main Menu"};card.Add(menu);
            status=new Label();status.AddToClassList("description");status.style.fontSize=14;status.style.marginTop=8;status.style.marginBottom=0;card.Add(status);
            host.Add(overlay);overlay.RegisterCallback<NavigationCancelEvent>(evt=>{if(ConsumePause())evt.StopPropagation();});
        }
        public bool ConsumePause()
        {
            if(IsOpen){lastPauseFrame=Time.frameCount;flow.TryRequest(ApplicationCommand.Pause);return true;}
            return lastPauseFrame==Time.frameCount;
        }
        private void Update()
        {
            var current=state();bool show=current?.Pending==true && ready() && flow.State==ApplicationState.Gameplay;
            if(show && !IsOpen)
            {
                IsOpen=true;background.Clear();foreach(var child in host.Children())if(child!=overlay){background.Add(Tuple.Create(child,child.enabledSelf));child.SetEnabled(false);}
                overlay.style.display=DisplayStyle.Flex;overlay.BringToFront();offers[0].schedule.Execute(()=>{if(IsOpen)offers[0].Focus();});
            }
            else if(!show && IsOpen)
            {
                IsOpen=false;overlay.style.display=DisplayStyle.None;foreach(var item in background)item.Item1.SetEnabled(item.Item2);background.Clear();
            }
            if(current==null || !current.Pending){draft=0;shownLevel=0;submitted=false;}
            else if(shownLevel!=current.PendingLevel){shownLevel=current.PendingLevel;draft=0;submitted=false;}
            if(IsOpen)Refresh();
        }
        private void Refresh()
        {
            var current=state();if(current==null || !current.Pending)return;
            int own=slot(),chosen=current.Choice(own);if(chosen>0)submitted=false;
            title.text="Upgrade before Level "+(current.PendingLevel+1);
            var presentation=readStatus();
            for(int i=0;i<3;i++)
            {
                string text=presentation?.Offer(i+1) ?? RunUpgrades.Name(i+1);
                if(offers[i].text!=text)offers[i].text=text;
                offers[i].SetEnabled(chosen==0 && !submitted);
                offers[i].EnableInClassList("primary",(chosen>0?chosen:draft)==i+1);
            }
            confirm.SetEnabled(chosen==0 && draft>0 && !submitted);
            confirm.text=chosen>0?"Choice Confirmed":submitted?"Confirming...":"Confirm Upgrade";
            string notice=failure();
            status.text=!string.IsNullOrEmpty(notice)?notice:current.CanAdvance?"Preparing the next level...":current.AllConfirmed?"Saving upgrades...":chosen>0?"Waiting for your teammate to choose.":submitted?"Waiting for the host to confirm your choice.":"Upgrades reset when the run ends. Profile XP is unchanged.";
        }
    }
}
