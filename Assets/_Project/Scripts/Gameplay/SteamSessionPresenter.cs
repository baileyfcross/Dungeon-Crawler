using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace SpaceCrawler.Gameplay
{
    /// <summary>Existing screens expose Steam actions without introducing a new gameplay destination.</summary>
    public sealed class SteamSessionPresenter : MonoBehaviour
    {
        private ApplicationRoot root;
        private Label status;
        private VisualElement sessionControls;
        private Button associate,host,join,invite,cancel,continueGame,resume;
        public void Bind(ApplicationRoot application,VisualElement view)
        {
            root=application;sessionControls=view.Q("steam-session-controls");status=view.Q<Label>("steam-status");continueGame=view.Q<Button>("continue");resume=view.Q<Button>("resume");
            associate=Wire(view,"steam-associate",()=>root.AssociateSteamProfile());
            host=Wire(view,"steam-host",()=>root.HostSteam(false));join=Wire(view,"steam-join",()=>root.JoinSteamInvitation());
            invite=Wire(view,"steam-invite",()=>root.InviteSteamFriend());cancel=Wire(view,"steam-cancel",()=>root.CancelSteamSession());
        }
        private static Button Wire(VisualElement view,string name,Action action)
        {var button=view.Q<Button>(name);if(button!=null)button.clicked+=action;return button;}
        private void Update()
        {
            if(root==null)return;
            bool menu=root.Flow.State==SpaceCrawler.Core.ApplicationState.MainMenu;
            if(menu && continueGame!=null)continueGame.SetEnabled(root.CanContinueCurrentSave);
            if(resume!=null && !root.CanResumeCurrentRun)resume.SetEnabled(false);
            if(sessionControls!=null)sessionControls.style.display=root.SteamSessionActive || root.HasSteamInvitation || !root.CanResumeCurrentRun ? DisplayStyle.Flex:DisplayStyle.None;
            if(status!=null)status.text=root.SteamStatus;
            if(associate!=null){associate.style.display=root.SteamReady && !root.SteamProfileAssociated ? DisplayStyle.Flex:DisplayStyle.None;associate.SetEnabled(menu && root.Profile!=null && !root.SteamBusy);}
            host?.SetEnabled(menu && root.SteamReady && root.SteamProfileAssociated && !root.SteamBusy && !root.SteamSessionActive);
            join?.SetEnabled(menu && root.SteamReady && root.SteamProfileAssociated && root.HasSteamInvitation && !root.SteamBusy && !root.SteamSessionActive);
            invite?.SetEnabled(root.CanInviteSteam);
            if(cancel!=null){cancel.style.display=root.SteamBusy || root.SteamSessionActive ? DisplayStyle.Flex:DisplayStyle.None;cancel.SetEnabled(menu || root.Flow.CanRequest(SpaceCrawler.Core.ApplicationCommand.ReturnToMainMenu));}
        }
    }
}
