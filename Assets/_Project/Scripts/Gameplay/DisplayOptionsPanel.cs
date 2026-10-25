using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Draft local display intentions and show the timed confirmation; no gameplay state.</summary>
    public sealed class DisplayOptionsPanel : MonoBehaviour
    {
        private GameDisplay display;
        private VisualElement view,controls,confirmation;
        private DropdownField resolution,mode;
        private Button apply,keep,revert;
        private Label notice,countdown;
        private List<DisplayPreferences> sizes;
        private bool wasPending,ownsPreview;
        public bool Pending=>display!=null && display.Change.Pending;
        public VisualElement View=>view;
        public void Bind(VisualElement parent,GameDisplay owner)
        {
            display=owner;view=new VisualElement{name="display-options"};parent.Add(view);
            controls=new VisualElement();view.Add(controls);
            resolution=new DropdownField("Resolution"){name="display-resolution"};controls.Add(resolution);
            mode=new DropdownField("Window mode",new List<string>{"Windowed","Borderless","Fullscreen"},0){name="display-mode"};controls.Add(mode);
            resolution.style.marginTop=12;mode.style.marginTop=12;
            var hint=new Label("Display changes affect only this computer. Apply to preview; Keep within 15 seconds to save.");hint.AddToClassList("description");hint.style.fontSize=13;hint.style.marginTop=12;hint.style.marginBottom=12;controls.Add(hint);
            apply=new Button(Apply){name="display-apply",text="Apply Display Settings"};apply.AddToClassList("primary");controls.Add(apply);
            var defaults=new Button(()=>SetDraft(display.Device.Defaults)){name="display-defaults",text="Restore Display Defaults"};controls.Add(defaults);
            confirmation=new VisualElement{name="display-confirmation"};confirmation.style.display=DisplayStyle.None;view.Add(confirmation);
            var title=new Label("Keep these display settings?");title.style.fontSize=22;title.style.marginTop=16;title.style.whiteSpace=WhiteSpace.Normal;confirmation.Add(title);
            countdown=new Label();countdown.AddToClassList("description");countdown.style.fontSize=14;confirmation.Add(countdown);
            keep=new Button(()=>{if(display.Keep())ownsPreview=false;Refresh();}){name="display-keep",text="Keep Settings"};keep.AddToClassList("primary");confirmation.Add(keep);
            revert=new Button(Cancel){name="display-revert",text="Revert"};confirmation.Add(revert);
            notice=new Label();notice.AddToClassList("description");notice.style.fontSize=13;notice.style.marginTop=8;notice.style.marginBottom=8;view.Add(notice);
            mode.RegisterValueChangedCallback(_=>RefreshSizes());
            controls.SetEnabled(display!=null && display.Device.CanApply);
        }
        public void Open(){RefreshSizes();SetDraft(display?.Device.Current);Refresh();}
        private void RefreshSizes()
        {
            if(display==null)return;
            var current=display.Device.Current;
            sizes=display.Device.SupportedSizes.ToList();
            // A manually sized window is valid in Windowed mode, but never advertise it as an exclusive mode.
            if(mode.index==0 && !sizes.Any(s=>s.Width==current.Width && s.Height==current.Height))sizes.Add(current);
            sizes=sizes.OrderBy(s=>s.Width).ThenBy(s=>s.Height).ToList();
            string previous=resolution.value;resolution.choices=sizes.Select(s=>$"{s.Width} × {s.Height}").ToList();
            resolution.SetValueWithoutNotify(resolution.choices.Contains(previous)?previous:resolution.choices.Last());
        }
        private void SetDraft(DisplayPreferences value)
        {
            if(value==null)return;mode.SetValueWithoutNotify(mode.choices[(int)value.Mode-1]);RefreshSizes();
            string text=$"{value.Width} × {value.Height}";
            resolution.SetValueWithoutNotify(resolution.choices.Contains(text)?text:resolution.choices.Last());
        }
        private void Apply()
        {
            if(display==null || resolution.index<0 || resolution.index>=sizes.Count || Pending)return;
            var size=sizes[resolution.index];ownsPreview=display.Preview(new DisplayPreferences(size.Width,size.Height,(DisplayMode)(mode.index+1)));Refresh();
        }
        public void Cancel()
        {
            if(ownsPreview){display.Revert();ownsPreview=false;}SetDraft(display?.Device.Current);Refresh();
        }
        private void Refresh()
        {
            controls.SetEnabled(display!=null && display.Device.CanApply);
            bool pending=Pending;controls.style.display=pending?DisplayStyle.None:DisplayStyle.Flex;confirmation.style.display=pending?DisplayStyle.Flex:DisplayStyle.None;
            if(pending)
            {
                keep.SetEnabled(display.Change.CanKeep);countdown.text=$"Reverting in {Math.Ceiling(display.Change.SecondsLeft):0} seconds.\n"+display.Change.Requested;
                if(!wasPending)revert.schedule.Execute(()=>{if(Pending)revert.Focus();});
            }
            else if(wasPending){ownsPreview=false;SetDraft(display.Device.Current);apply.schedule.Execute(()=>apply.Focus());}
            wasPending=pending;
            notice.text=display==null?"Display settings unavailable.":!display.Device.CanApply?"Display changes are available in the Windows player; the Editor is unchanged.":display.Change.Notice ?? "Choose a resolution and window mode.";
        }
        private void Update(){if(view?.resolvedStyle.display!=DisplayStyle.None)Refresh();}
        private void OnDestroy(){if(ownsPreview)display?.Revert();}
    }
}
