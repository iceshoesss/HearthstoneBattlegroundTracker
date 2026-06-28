using System;
using System.Collections.Generic;
using HBTCombat.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HBTCombat.Tests
{
    [TestClass]
    public class CombatSimulatorTests
    {
        private CombatSimulator _simulator;
        private MinionFactory _factory;

        [TestInitialize]
        public void Setup()
        {
            _simulator = new CombatSimulator();
            _factory = new MinionFactory();
            MinionFactoryCache.SetFactory(_factory);
        }

        // ═══════════════════════════════════════════════════════════════
        // 基础攻击测试
        // ═══════════════════════════════════════════════════════════════

        [TestMethod]
        public void BasicAttack_PlayerWins()
        {
            // 己方 10/10 vs 对方 1/1
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 10, 10, 1) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1) }
            );

            var result = _simulator.SimulateFight(input);

            // 应该赢（正数伤害）
            Assert.IsTrue(result > 0, $"Expected positive damage, got {result}");
        }

        [TestMethod]
        public void BasicAttack_OpponentWins()
        {
            // 己方 1/1 vs 对方 10/10
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 1, 1, 1) },
                opponentBoard: new[] { ("TEST_002", 10, 10, 1) }
            );

            var result = _simulator.SimulateFight(input);

            // 应该输（负数伤害）
            Assert.IsTrue(result < 0, $"Expected negative damage, got {result}");
        }

        [TestMethod]
        public void BasicAttack_EmptyBoard_Tie()
        {
            // 双方都空
            var input = CreateInput(
                playerBoard: new (string, int, int, int)[0],
                opponentBoard: new (string, int, int, int)[0]
            );

            var result = _simulator.SimulateFight(input);

            Assert.AreEqual(0, result, "Empty boards should tie");
        }

        // ═══════════════════════════════════════════════════════════════
        // 关键词测试
        // ═══════════════════════════════════════════════════════════════

        [TestMethod]
        public void Taunt_ForcesAttack()
        {
            // 己方 5/5 vs 对方: 1/1 + 10/10 Taunt
            // 己方应该攻击嘲讽的 10/10
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 5, 5, 1) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1), ("TEST_003", 10, 10, 1) }
            );
            // 设置嘲讽
            input.OpponentBoard[1].Taunt = true;

            var result = _simulator.SimulateFight(input);

            // 己方应该被嘲讽随从杀死
            Assert.IsTrue(result < 0, $"Expected loss due to taunt, got {result}");
        }

        [TestMethod]
        public void DivineShield_BlocksDamage()
        {
            // 己方 10/10 圣盾 vs 对方 1/1
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 10, 10, 1) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1) }
            );
            input.PlayerBoard[0].DivineShield = true;

            var result = _simulator.SimulateFight(input);

            // 己方应该赢，圣盾吸收了 1 点伤害
            Assert.IsTrue(result > 0, $"Expected win with divine shield, got {result}");
        }

        [TestMethod]
        public void Poisonous_KillsTarget()
        {
            // 己方 1/100 剧毒 vs 对方 100/100
            // 毒杀对方，但自己也死于反击 → 平局
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 1, 100, 1) },
                opponentBoard: new[] { ("TEST_002", 100, 100, 1) }
            );
            input.PlayerBoard[0].Poisonous = true;

            var result = _simulator.SimulateFight(input);

            // 双方都死 → 平局
            Assert.AreEqual(0, result, $"Expected tie (both die), got {result}");
        }

        [TestMethod]
        public void Poisonous_AttackerSurvives()
        {
            // 己方 1/200 剧毒 vs 对方 100/100
            // 毒杀对方，自己存活 → 赢
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 1, 200, 1) },
                opponentBoard: new[] { ("TEST_002", 100, 100, 1) }
            );
            input.PlayerBoard[0].Poisonous = true;

            var result = _simulator.SimulateFight(input);

            // 己方应该赢
            Assert.IsTrue(result > 0, $"Expected win with poison, got {result}");
        }

        [TestMethod]
        public void Venomous_KillsTarget()
        {
            // 己方 1/100 烈毒 vs 对方 100/100
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 1, 100, 1) },
                opponentBoard: new[] { ("TEST_002", 100, 100, 1) }
            );
            input.PlayerBoard[0].Venomous = true;

            var result = _simulator.SimulateFight(input);

            // 双方都死 → 平局
            Assert.AreEqual(0, result, $"Expected tie (both die), got {result}");
        }

        [TestMethod]
        public void Windfury_AttacksTwice()
        {
            // 己方 3/10 风怒 vs 对方 2/5, 2/5
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 3, 10, 1) },
                opponentBoard: new[] { ("TEST_002", 2, 5, 1), ("TEST_003", 2, 5, 1) }
            );
            input.PlayerBoard[0].Windfury = true;

            // 运行多次模拟
            int wins = 0;
            for (int i = 0; i < 100; i++)
            {
                var result = _simulator.SimulateFight(input);
                if (result > 0) wins++;
            }

            // 风怒应该能杀死两个 2/5
            Assert.IsTrue(wins > 50, $"Expected many wins with windfury, got {wins}/100");
        }

        [TestMethod]
        public void Cleave_HitsAdjacent()
        {
            // 己方 5/10 顺劈 vs 对方 3/1, 3/1, 3/1
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 5, 10, 1) },
                opponentBoard: new[] { ("TEST_002", 3, 1, 1), ("TEST_003", 3, 1, 1), ("TEST_004", 3, 1, 1) }
            );
            input.PlayerBoard[0].Cleave = true;

            // 运行多次模拟
            int wins = 0;
            for (int i = 0; i < 100; i++)
            {
                var result = _simulator.SimulateFight(input);
                if (result > 0) wins++;
            }

            // 顺劈应该能杀死相邻随从
            Assert.IsTrue(wins > 50, $"Expected many wins with cleave, got {wins}/100");
        }

        [TestMethod]
        public void Reborn_ComesBackWith1HP()
        {
            // 己方 10/1 复生 vs 对方 1/1
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 10, 1, 1) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1) }
            );
            input.PlayerBoard[0].Reborn = true;

            var result = _simulator.SimulateFight(input);

            // 复生应该让己方赢
            Assert.IsTrue(result > 0, $"Expected win with reborn, got {result}");
        }

        // ═══════════════════════════════════════════════════════════════
        // 亡语测试
        // ═══════════════════════════════════════════════════════════════

        [TestMethod]
        public void Deathrattle_SummonsTokens()
        {
            // 己方 1/1 有亡语召唤 vs 对方 1/1
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 1, 1, 1) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1) }
            );

            // 添加亡语：召唤一个 5/5
            input.PlayerBoard[0].Deathrattles.Add(new TestSummonDeathrattle("TOKEN_001", 5, 5));

            // 运行多次模拟
            int wins = 0;
            for (int i = 0; i < 100; i++)
            {
                var result = _simulator.SimulateFight(input);
                if (result > 0) wins++;
            }

            // 亡语召唤应该增加胜率
            Assert.IsTrue(wins > 30, $"Expected some wins with deathrattle, got {wins}/100");
        }

        // ═══════════════════════════════════════════════════════════════
        // 模拟器测试
        // ═══════════════════════════════════════════════════════════════

        [TestMethod]
        public void SimulationRunner_ReturnsValidResults()
        {
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 5, 5, 3) },
                opponentBoard: new[] { ("TEST_002", 5, 5, 3) }
            );

            var result = SimulationRunner.Run(input, iterations: 1000, maxMs: 1000);

            // 验证结果
            Assert.IsTrue(result.SimulationCount > 0, "Should have simulations");
            Assert.IsTrue(result.WinRate >= 0 && result.WinRate <= 1, "WinRate should be 0-1");
            Assert.IsTrue(result.TieRate >= 0 && result.TieRate <= 1, "TieRate should be 0-1");
            Assert.IsTrue(result.LossRate >= 0 && result.LossRate <= 1, "LossRate should be 0-1");

            float total = result.WinRate + result.TieRate + result.LossRate;
            Assert.IsTrue(Math.Abs(total - 1.0f) < 0.01f, $"Rates should sum to 1, got {total}");
        }

        [TestMethod]
        public void SimulationRunner_DamageRanges()
        {
            var input = CreateInput(
                playerBoard: new[] { ("TEST_001", 10, 10, 5) },
                opponentBoard: new[] { ("TEST_002", 1, 1, 1) }
            );

            var result = SimulationRunner.Run(input, iterations: 1000, maxMs: 1000);

            // 己方应该总是赢
            Assert.IsTrue(result.WinRate > 0.9f, $"Expected high win rate, got {result.WinRate}");

            // 伤害范围应该合理
            Assert.IsTrue(result.PlayerDamageMin > 0, "PlayerDamageMin should be positive");
            Assert.IsTrue(result.PlayerDamageMax >= result.PlayerDamageMin, "Max should be >= min");
        }

        // ═══════════════════════════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════════════════════════

        private SimulationInput CreateInput(
            (string cardId, int attack, int health, int tier)[] playerBoard,
            (string cardId, int attack, int health, int tier)[] opponentBoard)
        {
            var input = new SimulationInput
            {
                PlayerHealth = 40,
                OpponentHealth = 40,
                PlayerTier = 6,
                OpponentTier = 6,
                Turn = 10,
                DamageCap = 15,
            };

            foreach (var (cardId, attack, health, tier) in playerBoard)
            {
                var minion = CreateMinion(cardId, attack, health, tier, true);
                input.PlayerBoard.Add(minion);
            }

            foreach (var (cardId, attack, health, tier) in opponentBoard)
            {
                var minion = CreateMinion(cardId, attack, health, tier, false);
                input.OpponentBoard.Add(minion);
            }

            return input;
        }

        private Minion CreateMinion(string cardId, int attack, int health, int tier, bool player)
        {
            return new Minion
            {
                CardId = cardId,
                Name = cardId,
                BaseAttack = attack,
                BaseHealth = health,
                MaxHealth = health,
                CurrentHealth = health,
                Tier = tier,
                ControlledByPlayer = player,
            };
        }
    }

    /// <summary>
    /// 测试用亡语：召唤一个指定随从
    /// </summary>
    public class TestSummonDeathrattle : IDeathrattle
    {
        private readonly string _tokenCardId;
        private readonly int _attack;
        private readonly int _health;

        public TestSummonDeathrattle(string tokenCardId, int attack, int health)
        {
            _tokenCardId = tokenCardId;
            _attack = attack;
            _health = health;
        }

        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            return new List<Minion>
            {
                new Minion
                {
                    CardId = _tokenCardId,
                    Name = _tokenCardId,
                    BaseAttack = _attack,
                    BaseHealth = _health,
                    MaxHealth = _health,
                    CurrentHealth = _health,
                    Tier = 1,
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                }
            };
        }
    }
}
