using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunCompletionTests
    {
        private static RunGenerationConfiguration Catalog() => new RunGenerationConfiguration(new RunGenerationSettings(10,25,0),
            Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c=>new RoomConfiguration("room."+c,c)));
        public static RunProgression FinalProgression(RunSequence sequence)
        {
            while (true)
            {
                var progress = new RunProgression(sequence.CurrentGraph,new PlayerIdentity(1)); progress.SetGameplayActive(true); progress.TryCompleteEntry(sequence.CurrentGraph.StartRoomId);
                foreach (var edge in sequence.CurrentGraph.Route(progress.CurrentRoomId,sequence.CurrentGraph.TerminalRoomId))
                { progress.TryRequestExit(new ExitIntent(progress.Player,edge.From,edge.To)); progress.TryBeginAcceptedEntry(); progress.TryCompleteEntry(edge.To); }
                if (!sequence.TryAdvance(progress)) return progress;
            }
        }
        [Test]
        public void OnlyFinalRoomFreshNearbyLivingPartyConfirmationsCanCompleteOnce()
        {
            var sequence=new RunSequence(42,Catalog()); var life=new RunLife(RunLifeSnapshot.Fresh(2));
            life.BindProfile(0,Guid.NewGuid()); life.BindProfile(1,Guid.NewGuid());
            var completion=new RunCompletion(life); var progress=FinalProgression(sequence);
            var positions=new[]{Vector2.Zero,Vector2.UnitX}; var available=new[]{true,true};
            completion.Observe(2,true,progress,Vector2.Zero,positions,available); completion.Interact(new PlayerIdentity(1),false);
            Assert.That(completion.Interact(new PlayerIdentity(1),true),Is.False);
            completion.Observe(3,false,progress,Vector2.Zero,positions,available); Assert.That(completion.Available,Is.False);
            completion.Observe(3,true,progress,Vector2.Zero,new[]{Vector2.Zero,new Vector2(20,0)},available); Assert.That(completion.PartyReady,Is.False);
            completion.Observe(3,true,progress,Vector2.Zero,positions,available);
            Assert.That(completion.Interact(new PlayerIdentity(1),true),Is.False,"held input cannot carry into the exit");
            Assert.That(completion.Interact(new PlayerIdentity(3),true),Is.False);
            completion.Interact(new PlayerIdentity(1),false); Assert.That(completion.Interact(new PlayerIdentity(1),true),Is.True);
            Assert.That(life.Ended,Is.False); Assert.That(completion.Confirmations,Is.EqualTo(1));
            completion.Observe(3,true,progress,Vector2.Zero,positions,new[]{false,true}); Assert.That(completion.Confirmations,Is.Zero);
            completion.Observe(3,true,progress,Vector2.Zero,positions,available);
            completion.Interact(new PlayerIdentity(1),false);completion.Interact(new PlayerIdentity(1),true);
            life.TryDamage(new PlayerIdentity(2),Guid.NewGuid(),100); completion.Observe(3,true,progress,Vector2.Zero,positions,available);
            Assert.That(completion.PartyReady,Is.False);Assert.That(completion.Confirmations,Is.Zero); Assert.That(life.TryComplete(),Is.False);
            completion.Interact(new PlayerIdentity(2),false);completion.Interact(new PlayerIdentity(2),true);
            life.SetReviveHeld(new PlayerIdentity(1),false);life.SetReviveHeld(new PlayerIdentity(1),true);life.AdvanceRevival(3,positions,available);
            completion.Observe(3,true,progress,Vector2.Zero,positions,available);
            Assert.That(completion.Interact(new PlayerIdentity(2),true),Is.False,"downed held input cannot become a confirmation after revival");
            int endings=0;life.Changed+=()=>endings++;
            foreach (int slot in new[]{1,2}) {completion.Interact(new PlayerIdentity(slot),false);completion.Interact(new PlayerIdentity(slot),true);}
            Assert.That(life.EndReason,Is.EqualTo(RunEndReason.Completed));Assert.That(endings,Is.EqualTo(1));
            Assert.That(life.TryComplete(),Is.False);Assert.That(life.TryDamage(new PlayerIdentity(1),Guid.NewGuid(),1000),Is.False);
            Assert.That(life.TryAwardRunXp(10),Is.False);Assert.That(life.CreateReceipt(RunEndReason.Completed).Reason,Is.EqualTo(RunEndReason.Completed));
            Assert.Throws<InvalidOperationException>(()=>life.CreateReceipt(RunEndReason.Loss));
        }
        [Test]
        public void SuccessfulReceiptCannotBeCreatedForAnUnfinishedRunAndFlowUsesApprovedPanel()
        {
            var life=new RunLife(RunLifeSnapshot.Fresh(1));Assert.Throws<InvalidOperationException>(()=>life.CreateReceipt(RunEndReason.Completed));
            var flow=new ApplicationFlow();flow.TryRequest(ApplicationCommand.Initialize);flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame);flow.CompleteLoading(ApplicationState.Gameplay);flow.BeginRunEnd();
            Assert.That(flow.TryRequest(ApplicationCommand.ReturnToMainMenu),Is.False);Assert.That(flow.TryRequest(ApplicationCommand.Pause),Is.False);
            Assert.That(flow.TryRequest(ApplicationCommand.CompleteRun),Is.True);Assert.That(flow.State,Is.EqualTo(ApplicationState.Completed));
            Assert.That(flow.TryRequest(ApplicationCommand.NewGame),Is.False);Assert.That(flow.TryRequest(ApplicationCommand.Continue),Is.False);
            Assert.That(flow.TryRequest(ApplicationCommand.ReturnToMainMenu),Is.True);flow.CompleteLoading(ApplicationState.MainMenu);
            Assert.That(flow.TryRequest(ApplicationCommand.NewGame),Is.True);
        }
        [Test]
        public void CreditedAmountIncludesFractionalCarryAndReplayReturnsZero()
        {
            var folder=System.IO.Path.Combine("Logs","Validation","Completion","Fractions",Guid.NewGuid().ToString("N"));var store=new ProfileProgressStore(folder);
            var first=new RunEndReceipt(Guid.NewGuid(),10,RunEndReason.Loss,new[]{store.Profile.Id});
            Assert.That(store.EndRunWithCredit(first),Is.Zero);
            var completed=new RunEndReceipt(Guid.NewGuid(),10,RunEndReason.Completed,new[]{store.Profile.Id});
            Assert.That(store.EndRunWithCredit(completed),Is.EqualTo(1));Assert.That(store.Profile.FractionHundredths,Is.EqualTo(20));
            Assert.That(store.EndRunWithCredit(completed),Is.Zero);
        }
        [Test]
        public void CompletedRewardJournalSurvivesReloadAndDuplicateDeliveryWithoutDoubleCredit()
        {
            var folder=System.IO.Path.Combine("Logs","Validation","Completion","Rewards",Guid.NewGuid().ToString("N"));var store=new ProfileProgressStore(folder);
            var receipt=new RunEndReceipt(Guid.NewGuid(),250,RunEndReason.Completed,new[]{store.Profile.Id});
            Assert.That(store.EndRunWithCredit(receipt),Is.EqualTo(15));Assert.That(store.EndRunWithCredit(receipt),Is.Zero);
            var reopened=new ProfileProgressStore(folder);Assert.That(reopened.Profile.Experience,Is.EqualTo(15));
            Assert.That(reopened.EndRunWithCredit(receipt),Is.Zero);Assert.That(reopened.Profile.HasEndedRun(receipt.RunId),Is.True);
        }
    }
}
