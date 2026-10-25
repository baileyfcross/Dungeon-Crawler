using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    public sealed class AudioOptionsPanel : MonoBehaviour
    {
        private ApplicationFlow flow;
        private GameAudio audio;
        private VisualElement overlay,host;
        private Button options,close;
        private SliderInt master,effects,ui;
        private Toggle mute;
        private Label notice;
        private DisplayOptionsPanel displayOptions;
        private VisualElement audioControls,tabs;
        private Button audioTab,displayTab;
        private bool displaySelected;
        private int cancelledFrame=-1;
        private void SelectTab(bool display)
        {
            if(displayOptions.Pending)return;
            displaySelected=display;audioControls.style.display=display?DisplayStyle.None:DisplayStyle.Flex;
            displayOptions.View.style.display=display?DisplayStyle.Flex:DisplayStyle.None;
            audioTab.EnableInClassList("primary",!display);displayTab.EnableInClassList("primary",display);
            if(display)displayOptions.Open();
        }
        private readonly List<Tuple<VisualElement,bool>> background=new List<Tuple<VisualElement,bool>>();
        private int closedFrame=-1;
        private ApplicationState openedFrom;
        public bool IsOpen { get; private set; }
        public void Bind(VisualElement view,ApplicationFlow applicationFlow,GameAudio gameAudio)
        {
            flow=applicationFlow;audio=gameAudio;options=view.Q<Button>("options");
            host=view.Q(className:"screen") ?? view.Q(className:"gameplay-root") ?? view;
            overlay=new VisualElement{name="options-overlay"};overlay.AddToClassList("pause-overlay");overlay.style.display=DisplayStyle.None;
            var card=new VisualElement();card.AddToClassList("pause-card");card.style.maxHeight=Length.Percent(92);overlay.Add(card);
            var body=new ScrollView(ScrollViewMode.Vertical);body.style.flexShrink=1;card.Add(body);
            var title=new Label("Options");title.AddToClassList("title");title.style.fontSize=32;body.Add(title);
            body.Add(new Label("Preferences apply only on this computer."));
            tabs=new VisualElement();tabs.style.flexDirection=FlexDirection.Row;body.Add(tabs);
            audioTab=new Button(()=>SelectTab(false)){name="options-audio",text="Audio"};audioTab.style.flexGrow=1;tabs.Add(audioTab);
            displayTab=new Button(()=>SelectTab(true)){name="options-display",text="Display"};displayTab.style.flexGrow=1;tabs.Add(displayTab);
            audioControls=new VisualElement();body.Add(audioControls);
            master=Slider(audioControls,"audio-master","Master volume");effects=Slider(audioControls,"audio-effects","Effects volume");ui=Slider(audioControls,"audio-ui","UI volume");
            mute=new Toggle("Mute all audio"){name="audio-mute"};mute.style.marginTop=12;audioControls.Add(mute);mute.RegisterValueChangedCallback(_=>Apply());
            var defaults=new Button(RestoreDefaults){name="audio-defaults",text="Restore Defaults"};defaults.style.marginTop=16;audioControls.Add(defaults);
            displayOptions=gameObject.AddComponent<DisplayOptionsPanel>();displayOptions.Bind(body,GameDisplay.Current);SelectTab(false);
            close=new Button(()=>TryClose()){name="options-close",text="Close"};close.AddToClassList("primary");card.Add(close);
            notice=new Label();notice.AddToClassList("description");notice.style.fontSize=13;notice.style.marginBottom=0;card.Add(notice);
            host.Add(overlay);
            overlay.RegisterCallback<NavigationCancelEvent>(evt=>{TryClose();evt.StopPropagation();});
            if(options!=null){options.text="Options";options.tooltip="Adjust audio and display preferences for this computer.";options.SetEnabled(audio!=null);options.clicked+=Open;}
            view.Query<Button>().ForEach(button=>button.clicked+=()=>GameAudio.Emit(SoundCue.UiActivate));
            view.RegisterCallback<FocusInEvent>(evt=>{if(evt.target is Button || evt.target is SliderInt || evt.target is Toggle)GameAudio.Emit(SoundCue.UiFocus);});
            flow.StateChanged+=OnFlow;
        }
        private SliderInt Slider(VisualElement parent,string name,string label)
        {
            var slider=new SliderInt(label,0,100){name=name,showInputField=true};slider.style.marginTop=16;
            slider.RegisterValueChangedCallback(_=>Apply());parent.Add(slider);return slider;
        }
        private void Populate()
        {
            var settings=audio.Preferences;master.SetValueWithoutNotify(Mathf.RoundToInt(settings.Master*100));effects.SetValueWithoutNotify(Mathf.RoundToInt(settings.Effects*100));
            ui.SetValueWithoutNotify(Mathf.RoundToInt(settings.Ui*100));mute.SetValueWithoutNotify(settings.Muted);
        }
        private void Apply(){if(audio!=null)audio.SetPreferences(new AudioPreferences(master.value/100f,effects.value/100f,ui.value/100f,mute.value));}
        private void RestoreDefaults(){if(audio==null)return;audio.SetPreferences(AudioPreferences.Defaults);Populate();}
        private void Open()
        {
            if(audio==null || IsOpen || (flow.State!=ApplicationState.MainMenu && flow.State!=ApplicationState.Paused))return;
            openedFrom=flow.State;IsOpen=true;Populate();SelectTab(false);background.Clear();
            foreach(var child in host.Children())if(child!=overlay){background.Add(Tuple.Create(child,child.enabledSelf));child.SetEnabled(false);}
            overlay.style.display=DisplayStyle.Flex;master.schedule.Execute(()=>{if(IsOpen)master.Focus();});
        }
        public bool TryClose()
        {
            if(!IsOpen)return false;
            if(cancelledFrame==Time.frameCount)return true;
            if(displayOptions.Pending){displayOptions.Cancel();cancelledFrame=Time.frameCount;return true;}
            Close();return true;
        }
        private void Close()
        {
            displayOptions.Cancel();
            IsOpen=false;closedFrame=Time.frameCount;audio?.SavePreferences();overlay.style.display=DisplayStyle.None;
            foreach(var item in background)item.Item1.SetEnabled(item.Item2);background.Clear();
            if(flow.State==openedFrom)options?.Focus();
        }
        // UI Cancel and the player's Pause action can arrive in either order during the same frame.
        public bool ConsumePause(){if(IsOpen)return TryClose();return closedFrame==Time.frameCount;}
        private void OnFlow(ApplicationState state){if(IsOpen && state!=openedFrom)Close();}
        private void Update()
        {
            if(!IsOpen)return;
            bool pending=displayOptions.Pending;tabs.SetEnabled(!pending);close.SetEnabled(!pending);
            notice.text=displaySelected?"Escape / Cancel reverts an unconfirmed display change.":audio.Notice ?? "Audio changes apply immediately. Close to save. Escape / Cancel returns to the previous menu.";
        }
        private void OnDestroy(){if(flow!=null)flow.StateChanged-=OnFlow;}
    }
}
