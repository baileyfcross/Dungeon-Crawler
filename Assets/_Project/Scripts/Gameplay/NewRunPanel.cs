using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Modal run intentions. Cancellation never starts a run or changes committed choices.</summary>
    public sealed class NewRunPanel : MonoBehaviour
    {
        private ApplicationRoot root;
        private VisualElement overlay, host;
        private DropdownField difficulty, playerClass;
        private Button start, cancel, opener;
        private Label title, notice;
        private Func<bool> begin;
        private bool chooseDifficulty;
        private readonly List<Tuple<VisualElement,bool>> background=new List<Tuple<VisualElement,bool>>();
        public bool IsOpen { get; private set; }

        public void Bind(ApplicationRoot application, VisualElement view)
        {
            root=application;host=view.Q(className:"screen") ?? view;
            overlay=new VisualElement{name="new-run-overlay"};overlay.AddToClassList("pause-overlay");overlay.style.display=DisplayStyle.None;
            var card=new VisualElement();card.AddToClassList("pause-card");overlay.Add(card);
            title=new Label("New Game");title.AddToClassList("title");title.style.fontSize=32;card.Add(title);
            difficulty=new DropdownField("Difficulty",new List<string>{"Easy","Normal","Hard"},1){name="run-difficulty"};card.Add(difficulty);
            var choices=new List<string>{"Choose a class"};choices.AddRange(PlayerClasses.Names);
            playerClass=new DropdownField("Class",choices,0){name="player-class"};playerClass.style.marginTop=16;card.Add(playerClass);
            notice=new Label("Choose your class and difficulty. Selections are saved with the run.");notice.AddToClassList("description");notice.style.fontSize=14;notice.style.marginTop=16;card.Add(notice);
            start=new Button(Confirm){name="start-run",text="Start Game"};start.AddToClassList("primary");card.Add(start);
            cancel=new Button(()=>Close()){name="cancel-new-run",text="Cancel"};card.Add(cancel);host.Add(overlay);
            playerClass.RegisterValueChangedCallback(_=>start.SetEnabled(playerClass.index>0));
            overlay.RegisterCallback<NavigationCancelEvent>(evt=>{Close();evt.StopPropagation();});
            root.Flow.StateChanged+=OnFlow;
        }
        public bool Open(Func<bool> action,bool allowDifficulty,string heading,Button source=null)
        {
            if(IsOpen || root.Flow.State!=ApplicationState.MainMenu || root.SteamBusy || root.SteamSessionActive || root.Profile==null || !root.CanResumeCurrentRun)return false;
            begin=action;chooseDifficulty=allowDifficulty;opener=source;title.text=heading;IsOpen=true;
            difficulty.style.display=allowDifficulty?DisplayStyle.Flex:DisplayStyle.None;
            difficulty.SetValueWithoutNotify(root.SelectedDifficulty.ToString());
            int selected=0;for(int i=0;i<PlayerClasses.Ids.Count;i++)if(PlayerClasses.Ids[i]==root.SelectedClass)selected=i+1;
            playerClass.SetValueWithoutNotify(playerClass.choices[selected]);start.SetEnabled(selected>0);
            start.text=allowDifficulty?"Start Game":"Join Game";
            notice.text=allowDifficulty?"Choose your class and difficulty. Selections are saved with the run.":"Choose your class. The host sets difficulty. Continuing a saved run restores your saved class.";
            background.Clear();foreach(var child in host.Children())if(child!=overlay){background.Add(Tuple.Create(child,child.enabledSelf));child.SetEnabled(false);}
            overlay.style.display=DisplayStyle.Flex;overlay.BringToFront();playerClass.schedule.Execute(()=>{if(IsOpen)playerClass.Focus();});return true;
        }
        private void Confirm()
        {
            if(!IsOpen || playerClass.index<1)return;
            var selectedDifficulty=chooseDifficulty?(RunDifficulty)(difficulty.index+1):root.SelectedDifficulty;
            if(!root.SelectRunOptions(selectedDifficulty,PlayerClasses.Ids[playerClass.index-1]))return;
            var action=begin;Close();action?.Invoke();
        }
        public void Close()
        {
            if(!IsOpen)return;IsOpen=false;overlay.style.display=DisplayStyle.None;begin=null;
            foreach(var item in background)item.Item1.SetEnabled(item.Item2);background.Clear();
            if(root.Flow.State==ApplicationState.MainMenu)opener?.Focus();
        }
        private void OnFlow(ApplicationState state){if(state!=ApplicationState.MainMenu)Close();}
        private void Update(){if(IsOpen && (!root.CanResumeCurrentRun || root.Profile==null || root.SteamBusy || root.SteamSessionActive))Close();}
        private void OnDestroy(){if(root!=null && root.Flow!=null)root.Flow.StateChanged-=OnFlow;}
    }
}
