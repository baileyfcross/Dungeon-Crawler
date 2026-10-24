using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class ExplorationRulesTests
    {
        [TestCase(RunDifficulty.Easy,75,15,2)]
        [TestCase(RunDifficulty.Normal,100,20,3)]
        [TestCase(RunDifficulty.Hard,150,30,5)]
        public void DifficultyUsesApprovedFactorsAndSafeCapacity(RunDifficulty difficulty,int health,int damage,int count)
        {
            Assert.That(DifficultyRules.Scale(100,difficulty),Is.EqualTo(health));
            Assert.That(DifficultyRules.Scale(20,difficulty),Is.EqualTo(damage));
            Assert.That(DifficultyRules.EnemyCount(3,5,difficulty,false),Is.EqualTo(count));
            Assert.That(DifficultyRules.EnemyCount(3,5,difficulty,true),Is.Zero);
            Assert.That(DifficultyRules.EnemyCount(3,1,difficulty,false),Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyRules.Factor((RunDifficulty)55));
        }
        [Test]
        public void MapSnapshotCannotExposeUnenteredRoomsOrTheirConnections()
        {
            var catalog=new RunGenerationConfiguration(new RunGenerationSettings(12,12,0),new[]{
                new RoomConfiguration("start",RoomCategory.Starting,1),new RoomConfiguration("engine",RoomCategory.Engine,1),
                new RoomConfiguration("generator",RoomCategory.Generator,1)});
            var graph=new RunSequence(42,catalog).CurrentGraph;
            var first=graph.Connections.First(e=>e.From.Equals(graph.StartRoomId));
            var map=new ExploredMap(graph,new[]{first.From,first.To,first.To},new[]{first.From,first.To});
            Assert.That(map.Rooms.Count,Is.EqualTo(2));Assert.That(map.Connections.Count,Is.EqualTo(1));
            Assert.That(map.PlayerRooms[0],Is.Not.EqualTo(map.PlayerRooms[1]));
            Assert.That(map.Connections.All(e=>map.Rooms.Any(n=>n.Id.Equals(e.From)) && map.Rooms.Any(n=>n.Id.Equals(e.To))),Is.True);
            Assert.Throws<ArgumentException>(()=>new ExploredMap(graph,new[]{first.From},new[]{first.To}));
        }
    }
}
