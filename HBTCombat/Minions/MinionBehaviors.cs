using System;
using System.Collections.Generic;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 随从行为注册中心
    /// 集中注册所有有特殊行为的随从
    /// </summary>
    public static class MinionBehaviors
    {
        /// <summary>
        /// 注册所有已知的随从行为
        /// </summary>
        public static void RegisterAll()
        {
            // === 亡语召唤类 ===
            RegisterDeathrattleSummons();

            // === 亡语效果类 ===
            RegisterDeathrattleEffects();

            // === 友方死亡触发类 ===
            RegisterOnFriendlyMinionDied();

            // === 战斗开始触发类 ===
            RegisterStartOfCombat();

            // === 攻击后触发类 ===
            RegisterAfterAttack();

            // === 被动加成类 ===
            RegisterPassiveBonuses();

            // === 复仇触发类 ===
            RegisterAvenge();
        }

        // ── 亡语召唤 ──

        private static void RegisterDeathrattleSummons()
        {
            // Rat Pack: 亡语召唤等同于攻击力数量的 1/1 Rat
            MinionFactory.RegisterDeathrattle("BG21_002", (m, p) => new List<IDeathrattle>
            {
                new RatPackDeathrattle()
            });

            // Infested Wolf: 亡语召唤两个 1/1 Spider
            MinionFactory.RegisterDeathrattle("BG21_005", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_005t" }, 2, 4)
            });

            // Savannah Highmane: 亡语召唤两个 2/2 Hyena
            MinionFactory.RegisterDeathrattle("BG21_006", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_006t" }, 2, 4)
            });

            // Kindly Grandmother: 亡语召唤 3/2 Big Bad Wolf
            MinionFactory.RegisterDeathrattle("BG21_001", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_001t" }, 1, 2)
            });

            // Sewer Rat: 亡语召唤 2/3 Taunt
            MinionFactory.RegisterDeathrattle("BG21_003", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_003t" }, 1, 2)
            });

            // Ghastcoiler: 亡语召唤 2 个随机亡语随从（简化为固定衍生物）
            MinionFactory.RegisterDeathrattle("BG21_008", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_008t" }, 2, 4)
            });

            // Sneeds Old Shredder: 亡语召唤随机传说随从（简化）
            MinionFactory.RegisterDeathrattle("BG21_009", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_009t" }, 1, 2)
            });

            // Imp Mama: 受到伤害时召唤恶魔（简化为亡语）
            MinionFactory.RegisterDeathrattle("BG21_020", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_020t" }, 3, 6)
            });

            // Fiendish Servant: 亡语将攻击力随机分配给友方随从
            MinionFactory.RegisterDeathrattle("BG21_021", (m, p) => new List<IDeathrattle>
            {
                new FiendishServantDeathrattle()
            });

            // Mechano-Egg: 亡语召唤 8/8 Robosaur
            MinionFactory.RegisterDeathrattle("BG21_024", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_024t" }, 1, 2)
            });

            // Kaboom Bot: 亡语对随机敌方随从造成 4 伤害
            MinionFactory.RegisterDeathrattleEffect("BG21_025", (m, p) => new List<IDeathrattleEffect>
            {
                new KaboomBotDeathrattleEffect()
            });

            // Harvest Golem: 亡语召唤 2/3 Damaged Golem
            MinionFactory.RegisterDeathrattle("BG21_026", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_026t" }, 1, 2)
            });

            // Mecharoo: 亡语召唤 1/1 Jo-E Bot
            MinionFactory.RegisterDeathrattle("BG21_027", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_027t" }, 1, 2)
            });

            // Replicating Menace: 亡语召唤三个 1/1 Microbot
            MinionFactory.RegisterDeathrattle("BG21_030", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_030t" }, 3, 6)
            });

            // Imprisoner: 亡语召唤 1/1 Imp
            MinionFactory.RegisterDeathrattle("BG21_031", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_031t" }, 1, 2)
            });

            // Voidlord: 亡语召唤三个 1/3 Voidwalker
            MinionFactory.RegisterDeathrattle("BG21_032", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_032t" }, 3, 6)
            });

            // Imp Gang Boss: 受到伤害时召唤 1/1 Imp（简化）
            MinionFactory.RegisterFriendlyMinionDied("BG21_033", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new ImpGangBossTrigger()
            });

            // Scallywag: 亡语召唤 1/1 Sky Pirate 并立即攻击
            MinionFactory.RegisterDeathrattle("BG21_036", (m, p) => new List<IDeathrattle>
            {
                new ScallywagDeathrattle()
            });

            // Ring Matron: 亡语召唤两个 3/2 Imp
            MinionFactory.RegisterDeathrattle("BG21_037", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG21_037t" }, 2, 4)
            });

            // Foul Egg: 亡语召唤 4/4 Foul Chicken
            MinionFactory.RegisterDeathrattle("BG23_001", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG23_001t" }, 1, 2)
            });
        }

        // ── 亡语效果 ──

        private static void RegisterDeathrattleEffects()
        {
            // Goldrinn: 亡语给所有友方野兽 +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG21_007", (m, p) => new List<IDeathrattleEffect>
            {
                new GoldrinnDeathrattleEffect()
            });

            // Spawn of NZoth: 亡语给所有友方随从 +1/+1
            MinionFactory.RegisterDeathrattleEffect("BG21_040", (m, p) => new List<IDeathrattleEffect>
            {
                new SpawnOfNZothDeathrattleEffect()
            });

            // Selfless Hero: 亡语给一个随机友方随从圣盾
            MinionFactory.RegisterDeathrattleEffect("BG21_041", (m, p) => new List<IDeathrattleEffect>
            {
                new SelflessHeroDeathrattleEffect()
            });

            // Unstable Ghoul: 亡语对所有随从造成 1 伤害
            MinionFactory.RegisterDeathrattleEffect("BG21_042", (m, p) => new List<IDeathrattleEffect>
            {
                new UnstableGhoulDeathrattleEffect()
            });

            // Tunnel Blaster: 亡语对所有随从造成 3 伤害
            MinionFactory.RegisterDeathrattleEffect("BG21_043", (m, p) => new List<IDeathrattleEffect>
            {
                new TunnelBlasterDeathrattleEffect()
            });
        }

        // ── 友方死亡触发 ──

        private static void RegisterOnFriendlyMinionDied()
        {
            // Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
            MinionFactory.RegisterFriendlyMinionDied("BG21_044", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new ScavengingHyenaTrigger()
            });

            // Junkbot: 当友方机械死亡时获得 +2/+2
            MinionFactory.RegisterFriendlyMinionDied("BG21_045", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new JunkbotTrigger()
            });

            // Flesheating Ghoul: 当任意随从死亡时获得 +1 攻击
            MinionFactory.RegisterFriendlyMinionDied("BG21_046", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new FlesheatingGhoulTrigger()
            });
        }

        // ── 战斗开始触发 ──

        private static void RegisterStartOfCombat()
        {
            // Red Whelp: 战斗开始时每有一条龙造成 1 伤害
            MinionFactory.RegisterStartOfCombat("BG21_050", (m, p) => new List<IOnStartOfCombat>
            {
                new RedWhelpTrigger()
            });
        }

        // ── 攻击后触发 ──

        private static void RegisterAfterAttack()
        {
            // Monstrous Macaw: 攻击后触发友方随从的亡语
            MinionFactory.RegisterAfterAttack("BG21_060", (m, p) => new List<IOnAfterAttack>
            {
                new MonstrousMacawTrigger()
            });
        }

        // ── 被动加成 ──

        private static void RegisterPassiveBonuses()
        {
            // Mama Bear: 召唤野兽时给予 +4/+4
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_061", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new MamaBearTrigger()
            });

            // Pack Leader: 召唤野兽时给予 +3 攻击
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_062", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new PackLeaderTrigger()
            });

            // Mal'Ganis: 友方恶魔获得 +2/+2
            MinionFactory.RegisterPassiveAttackBonus("BG21_063", (m, p) => new MalGanisAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_063", (m, p) => new MalGanisHealthBonus());

            // Kalecgos: 友方龙获得 +1/+1（简化）
            MinionFactory.RegisterPassiveAttackBonus("BG21_064", (m, p) => new KalecgosAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_064", (m, p) => new KalecgosHealthBonus());
        }

        // ── 复仇触发 ──

        private static void RegisterAvenge()
        {
            // 示例：复仇效果随从
            // 这里可以添加复仇触发的随从
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 亡语实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Rat Pack 亡语：召唤等同于攻击力数量的 1/1 Rat
    /// </summary>
    public class RatPackDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            int count = source.BaseAttack;
            if (golden) count *= 2;
            var result = new List<Minion>();
            var factory = MinionFactoryCache.GetFactory();
            for (int i = 0; i < count; i++)
            {
                var rat = factory.CreateFromCardId("BG21_002t", source.ControlledByPlayer);
                rat.Golden = golden;
                result.Add(rat);
            }
            return result;
        }
    }

    /// <summary>
    /// Fiendish Servant 亡语：将攻击力随机分配给友方随从
    /// </summary>
    public class FiendishServantDeathrattle : IDeathrattle
    {
        private static readonly Random Rng = new Random();

        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 这个效果需要在战斗状态中处理，返回空列表
            // 实际效果在 CombatSimulator 中特殊处理
            return new List<Minion>();
        }
    }

    /// <summary>
    /// Kaboom Bot 亡语：对随机敌方随从造成 4 伤害
    /// </summary>
    public class KaboomBotDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();

        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 8 : 4;
            var enemyBoard = state.GetEnemyBoard(source);
            if (enemyBoard.Count == 0) return;
            var target = enemyBoard[Rng.Next(enemyBoard.Count)];
            target.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Scallywag 亡语：召唤 1/1 Sky Pirate 并立即攻击
    /// </summary>
    public class ScallywagDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var factory = MinionFactoryCache.GetFactory();
            var pirate = factory.CreateFromCardId("BG21_036t", source.ControlledByPlayer);
            pirate.Golden = golden;
            return new List<Minion> { pirate };
        }
    }

    /// <summary>
    /// Goldrinn 亡语：给所有友方野兽 +4/+4
    /// </summary>
    public class GoldrinnDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int atkBonus = source.Golden ? 8 : 4;
            int hpBonus = source.Golden ? 8 : 4;
            var friendlyBoard = state.GetFriendlyBoard(source);
            foreach (var m in friendlyBoard)
            {
                if (m.PrimaryRace == "Beast")
                {
                    m.BaseAttack += atkBonus;
                    m.MaxHealth += hpBonus;
                    m.CurrentHealth += hpBonus;
                }
            }
        }
    }

    /// <summary>
    /// Spawn of NZoth 亡语：给所有友方随从 +1/+1
    /// </summary>
    public class SpawnOfNZothDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 2 : 1;
            var friendlyBoard = state.GetFriendlyBoard(source);
            foreach (var m in friendlyBoard)
            {
                m.BaseAttack += bonus;
                m.MaxHealth += bonus;
                m.CurrentHealth += bonus;
            }
        }
    }

    /// <summary>
    /// Selfless Hero 亡语：给一个随机友方随从圣盾
    /// </summary>
    public class SelflessHeroDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();

        public void Trigger(Minion source, CombatState state)
        {
            int count = source.Golden ? 2 : 1;
            var friendlyBoard = state.GetFriendlyBoard(source);
            var candidates = new List<Minion>();
            foreach (var m in friendlyBoard)
            {
                if (m != source && !m.DivineShield)
                    candidates.Add(m);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int idx = Rng.Next(candidates.Count);
                candidates[idx].DivineShield = true;
                candidates.RemoveAt(idx);
            }
        }
    }

    /// <summary>
    /// Unstable Ghoul 亡语：对所有随从造成 1 伤害
    /// </summary>
    public class UnstableGhoulDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 2 : 1;
            foreach (var m in state.PlayerBoard)
                m.TakeDamage(damage);
            foreach (var m in state.OpponentBoard)
                m.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Tunnel Blaster 亡语：对所有随从造成 3 伤害
    /// </summary>
    public class TunnelBlasterDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            foreach (var m in state.PlayerBoard)
                m.TakeDamage(damage);
            foreach (var m in state.OpponentBoard)
                m.TakeDamage(damage);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 友方死亡触发实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
    /// </summary>
    public class ScavengingHyenaTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            if (dead.PrimaryRace == "Beast")
            {
                int atkBonus = self.Golden ? 4 : 2;
                int hpBonus = self.Golden ? 2 : 1;
                self.BaseAttack += atkBonus;
                self.MaxHealth += hpBonus;
                self.CurrentHealth += hpBonus;
            }
        }
    }

    /// <summary>
    /// Junkbot: 当友方机械死亡时获得 +2/+2
    /// </summary>
    public class JunkbotTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            if (dead.PrimaryRace == "Mech")
            {
                int bonus = self.Golden ? 4 : 2;
                self.BaseAttack += bonus;
                self.MaxHealth += bonus;
                self.CurrentHealth += bonus;
            }
        }
    }

    /// <summary>
    /// Flesheating Ghoul: 当任意随从死亡时获得 +1 攻击
    /// </summary>
    public class FlesheatingGhoulTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            int bonus = self.Golden ? 2 : 1;
            self.BaseAttack += bonus;
        }
    }

    /// <summary>
    /// Imp Gang Boss: 受到伤害时召唤 1/1 Imp
    /// </summary>
    public class ImpGangBossTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            // 简化：当友方恶魔死亡时召唤 Imp
            if (dead.ControlledByPlayer == self.ControlledByPlayer && dead.PrimaryRace == "Demon")
            {
                var factory = MinionFactoryCache.GetFactory();
                var board = state.GetFriendlyBoard(self);
                if (board.Count < 7)
                {
                    var imp = factory.CreateFromCardId("BG21_033t", self.ControlledByPlayer);
                    imp.Golden = self.Golden;
                    board.Add(imp);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 战斗开始触发实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Red Whelp: 战斗开始时每有一条龙造成 1 伤害
    /// </summary>
    public class RedWhelpTrigger : IOnStartOfCombat
    {
        private static readonly Random Rng = new Random();

        public void OnStartOfCombat(Minion self, CombatState state)
        {
            var friendlyBoard = state.GetFriendlyBoard(self);
            int dragonCount = 0;
            foreach (var m in friendlyBoard)
            {
                if (m.PrimaryRace == "Dragon")
                    dragonCount++;
            }

            int damage = self.Golden ? dragonCount * 2 : dragonCount;
            if (damage <= 0) return;

            var enemyBoard = state.GetEnemyBoard(self);
            if (enemyBoard.Count == 0) return;

            // 对随机敌方随从造成伤害
            for (int i = 0; i < damage; i++)
            {
                if (enemyBoard.Count == 0) break;
                var target = enemyBoard[Rng.Next(enemyBoard.Count)];
                target.TakeDamage(1);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 攻击后触发实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Monstrous Macaw: 攻击后触发友方随从的亡语
    /// </summary>
    public class MonstrousMacawTrigger : IOnAfterAttack
    {
        private static readonly Random Rng = new Random();

        public void OnAfterAttack(Minion self, Minion attacker, Minion target, CombatState state)
        {
            if (attacker != self) return; // 只在自己攻击时触发

            var friendlyBoard = state.GetFriendlyBoard(self);
            var deathrattleMinions = new List<Minion>();
            foreach (var m in friendlyBoard)
            {
                if (m != self && m.Deathrattles.Count > 0)
                    deathrattleMinions.Add(m);
            }

            if (deathrattleMinions.Count == 0) return;

            var chosen = deathrattleMinions[Rng.Next(deathrattleMinions.Count)];
            // 触发选中随从的亡语
            foreach (var dr in chosen.Deathrattles)
            {
                var summons = dr.TriggerDeathrattle(chosen, chosen.Golden);
                if (summons != null)
                {
                    foreach (var s in summons)
                    {
                        if (friendlyBoard.Count < 7)
                            friendlyBoard.Add(s);
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 友方随从召唤触发实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Mama Bear: 召唤野兽时给予 +4/+4
    /// </summary>
    public class MamaBearTrigger : IOnFriendlyMinionSummoned
    {
        public void OnFriendlyMinionSummoned(Minion self, Minion summoned, CombatState state)
        {
            if (summoned.PrimaryRace == "Beast")
            {
                int atkBonus = self.Golden ? 8 : 4;
                int hpBonus = self.Golden ? 8 : 4;
                summoned.BaseAttack += atkBonus;
                summoned.MaxHealth += hpBonus;
                summoned.CurrentHealth += hpBonus;
            }
        }
    }

    /// <summary>
    /// Pack Leader: 召唤野兽时给予 +3 攻击
    /// </summary>
    public class PackLeaderTrigger : IOnFriendlyMinionSummoned
    {
        public void OnFriendlyMinionSummoned(Minion self, Minion summoned, CombatState state)
        {
            if (summoned.PrimaryRace == "Beast")
            {
                int bonus = self.Golden ? 6 : 3;
                summoned.BaseAttack += bonus;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 被动加成实现
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Mal'Ganis: 友方恶魔获得 +2/+2
    /// </summary>
    public class MalGanisAttackBonus : IPassiveAttackBonus
    {
        public int GetPassiveAttackBonus(Minion self, CombatState state)
        {
            // Mal'Ganis 自己不获得加成
            return 0;
        }
    }

    public class MalGanisHealthBonus : IPassiveHealthBonus
    {
        public int GetPassiveHealthBonus(Minion self, CombatState state)
        {
            return 0;
        }
    }

    /// <summary>
    /// Kalecgos: 友方龙获得 +1/+1
    /// </summary>
    public class KalecgosAttackBonus : IPassiveAttackBonus
    {
        public int GetPassiveAttackBonus(Minion self, CombatState state)
        {
            return 0;
        }
    }

    public class KalecgosHealthBonus : IPassiveHealthBonus
    {
        public int GetPassiveHealthBonus(Minion self, CombatState state)
        {
            return 0;
        }
    }
}
