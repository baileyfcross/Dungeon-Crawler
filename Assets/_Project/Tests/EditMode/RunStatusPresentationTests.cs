using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunStatusPresentationTests
    {
        private static WeaponConfiguration Weapon(int damage)=>WeaponConfiguration.Melee("weapon.test",damage,.5f,2,90);
        private static RunStatusPresentation View(RunUpgrades upgrades,PlayerVitals vitals,int damage=25,int slot=0)
            =>new RunStatusPresentation("logistics-manager",RunDifficulty.Hard,2,"Test weapon",Weapon(damage),vitals,upgrades,slot);

        [TestCase(25,30,35)] [TestCase(20,24,28)]
        public void DamagePreviewMatchesActualGrantFromBaseAtBothStops(int baseline,int first,int second)
        {
            var state=new RunUpgrades(1).Begin(1);
            foreach(int expected in new[]{first,second})
            {
                string preview=View(state,PlayerVitals.Starting,baseline).Offer(3);
                Assert.That(preview,Does.Contain("→ "+expected));
                state.TrySelect(0,3,out state);state=state.Grant();
                Assert.That(Weapon(baseline).WithDamageBonus(state.DamagePercent(0)).Damage,Is.EqualTo(expected));
                if(state.GrantedLevels==1)state=state.EnterLevel(2).Begin(2);
            }
            Assert.That(View(state,PlayerVitals.Starting,baseline).Offer(3),Does.Not.Contain("→"),"Saved grants never preview another application.");
        }
        [Test]
        public void HealthAndMovementPreviewsAreReadOnlyAndRetainTheHealthDeficit()
        {
            var state=new RunUpgrades(1).Begin(1);var life=new RunLife(RunLifeSnapshot.Fresh(1));
            life.TryDamage(new PlayerIdentity(1),System.Guid.NewGuid(),65);
            var view=View(state,life[0]);Assert.That(view.Offer(1),Does.Contain("35/100 HP → 55/120 HP"));
            Assert.That(view.Offer(2),Does.Contain("+0% → +8%"));
            Assert.That(state.Choice(0),Is.Zero);Assert.That(life[0].Current,Is.EqualTo(35));
            state.TrySelect(0,1,out state);var health=state.PreviewGrantHealth(life.Capture());
            Assert.That(health.Players[0].Current,Is.EqualTo(55));Assert.That(health.Players[0].Maximum,Is.EqualTo(120));
            state=new RunUpgrades(1).Begin(1);state.TrySelect(0,2,out state);state=state.Grant().EnterLevel(2).Begin(2);
            Assert.That(View(state,PlayerVitals.Starting).Offer(2),Does.Contain("Owned: 1/2").And.Contain("+8% → +16%"));
        }
        [Test]
        public void PartySummaryAndOffersUseOnlyTheLocalSlotsGrantedRanks()
        {
            var state=new RunUpgrades(2).Begin(1);state.TrySelect(0,1,out state);state.TrySelect(1,3,out state);state=state.Grant().EnterLevel(2).Begin(2);
            var host=View(state,new PlayerVitals(55,120,PlayerLifeState.Alive));var client=View(state,PlayerVitals.Starting,slot:1);
            Assert.That(host.Summary,Does.Contain("Logistics Manager").And.Contain("Hard").And.Contain("Level 2/3").And.Contain("55/120 HP"));
            Assert.That(host.Summary,Does.Contain("Reinforced Suit: Rank 1/2").And.Contain("+0% base damage"));
            Assert.That(client.Summary,Does.Contain("Reinforced Suit: Rank 0/2").And.Contain("+20% base damage"));
            Assert.That(client.Offer(3),Does.Contain("30 → 35").And.Contain("Owned: 1/2"));
            Assert.That(state.Choice(0),Is.Zero);Assert.That(state.Choice(1),Is.Zero);
        }
        [Test]
        public void LegacyRunShowsNoInventedUpgradesOrClass()
        {
            var view=new RunStatusPresentation("",RunDifficulty.Normal,1,"Knife",Weapon(25),PlayerVitals.Starting,null,0);
            Assert.That(view.Summary,Does.Contain("Not selected").And.Contain("unavailable for this saved run").And.Contain("+0% base movement, +0% base damage"));
        }
    }
}
