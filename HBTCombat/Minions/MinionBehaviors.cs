using System;
using System.Collections.Generic;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 随从行为注册中心
    /// 集中注册所有有特殊行为的随从
    ///
    /// 注意：使用 BattlegroundDB 中的真实卡牌 ID
    /// 数据驱动的亡语（有 ChildIds 的）会自动处理，这里只注册需要特殊逻辑的随从
    /// </summary>
    public static class MinionBehaviors
    {
        /// <summary>
        /// 注册所有已知的随从行为
        /// </summary>
        public static void RegisterAll()
        {
            // === 亡语效果类（需要特殊逻辑）===
            RegisterDeathrattleEffects();

            // === 友方死亡触发类 ===
            RegisterOnFriendlyMinionDied();

            // === 战斗开始触发类 ===
            RegisterStartOfCombat();

            // === 攻击后触发类 ===
            RegisterAfterAttack();

            // === 友方召唤触发类 ===
            RegisterOnFriendlyMinionSummoned();

            // === 复仇触发类 ===
            RegisterAvenge();

            // === 被动加成类 ===
            RegisterPassiveBonuses();

            // === 自定义亡语召唤（特殊逻辑）===
            RegisterSpecialDeathrattles();
        }

        // ═══════════════════════════════════════════════════════════════
        // 亡语效果类（非召唤）
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterDeathrattleEffects()
        {
            // Goldrinn, the Great Wolf (T6): 给所有友方野兽 +8/+8
            MinionFactory.RegisterDeathrattleEffect("BGS_018", (m, p) => new List<IDeathrattleEffect>
            {
                new GoldrinnDeathrattleEffect()
            });

            // Scarlet Skull (T2): +1/+2 to a friendly Undead
            MinionFactory.RegisterDeathrattleEffect("BG25_022", (m, p) => new List<IDeathrattleEffect>
            {
                new ScarletSkullDeathrattleEffect()
            });

            // Spiked Savior (T5): +1 health to all friendly minions AND deals 1 damage to each
            // TODO: 需要实现

            // Tunnel Blaster (T4): deals 3 damage to all minions
            MinionFactory.RegisterDeathrattleEffect("BG_DAL_775", (m, p) => new List<IDeathrattleEffect>
            {
                new TunnelBlasterDeathrattleEffect()
            });

            // Silent Enforcer (T4): deals 2 damage to all minions (except friendly demons)
            MinionFactory.RegisterDeathrattleEffect("BG33_156", (m, p) => new List<IDeathrattleEffect>
            {
                new SilentEnforcerDeathrattleEffect()
            });

            // Baneling (T2): deals damage equal to its attack to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG31_HERO_811t5", (m, p) => new List<IDeathrattleEffect>
            {
                new BanelingDeathrattleEffect()
            });

            // Leeroy the Reckless (T5): destroys the minion that killed it
            // TODO: 需要跟踪击杀者，暂时跳过

            // Plaguerunner (T4): +1/+1 to a random friendly minion for each minion that died this combat
            MinionFactory.RegisterDeathrattleEffect("BG34_690", (m, p) => new List<IDeathrattleEffect>
            {
                new PlaguerunnerDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 友方死亡触发
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterOnFriendlyMinionDied()
        {
            // Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
            // CardId: BG21_044 (可能已过期，需要验证)
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

            // Imp Gang Boss: 受到伤害时召唤 1/1 Imp
            MinionFactory.RegisterFriendlyMinionDied("BG21_033", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new ImpGangBossTrigger()
            });

            // Elementium Squirrel Bomb (T4): deals 4 damage per friendly mech that died this combat
            MinionFactory.RegisterDeathrattleEffect("TB_BaconShop_HERO_17_Buddy", (m, p) => new List<IDeathrattleEffect>
            {
                new ElementiumSquirrelBombDeathrattleEffect()
            });

            // Ingenious Inventor (T5): +1/+1 for each friendly minion that died this combat
            MinionFactory.RegisterDeathrattleEffect("BG35_890", (m, p) => new List<IDeathrattleEffect>
            {
                new IngeniousInventorDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 战斗开始触发
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterStartOfCombat()
        {
            // Red Whelp: 战斗开始时每有一条龙造成 1 伤害
            // CardId: BG21_050 (可能已过期)
            MinionFactory.RegisterStartOfCombat("BG21_050", (m, p) => new List<IOnStartOfCombat>
            {
                new RedWhelpTrigger()
            });

            // Spirit of Air (T1): gives a random friendly minion Windfury, Divine Shield, and Taunt
            MinionFactory.RegisterStartOfCombat("TB_BaconShop_HERO_76_Buddy", (m, p) => new List<IOnStartOfCombat>
            {
                new SpiritOfAirTrigger()
            });

            // Dozy Whelp: 战斗开始时获得 +1 攻击（如果有其他龙）
            // TODO: 需要实现
        }

        // ═══════════════════════════════════════════════════════════════
        // 攻击后触发
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterAfterAttack()
        {
            // Monstrous Macaw: 攻击后触发友方随从的亡语
            // CardId: BG21_060 (可能已过期)
            MinionFactory.RegisterAfterAttack("BG21_060", (m, p) => new List<IOnAfterAttack>
            {
                new MonstrousMacawTrigger()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 友方召唤触发
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterOnFriendlyMinionSummoned()
        {
            // Mama Bear: 召唤野兽时给予 +4/+4
            // CardId: BG21_061 (可能已过期)
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_061", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new MamaBearTrigger()
            });

            // Pack Leader: 召唤野兽时给予 +3 攻击
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_062", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new PackLeaderTrigger()
            });

            // Khadgar: 你的卡牌召唤随从时召唤 2 个副本
            // TODO: 需要实现（复杂，需要修改召唤逻辑）
        }

        // ═══════════════════════════════════════════════════════════════
        // 复仇触发
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterAvenge()
        {
            // Baron Rivendare: 你的亡语触发两次
            // TODO: 需要实现（需要修改亡语触发逻辑）

            // Brann Bronzebeard: 你的战吼触发两次
            // TODO: 战斗中不适用
        }

        // ═══════════════════════════════════════════════════════════════
        // 被动加成
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterPassiveBonuses()
        {
            // Mal'Ganis: 友方恶魔获得 +2/+2
            // CardId: BG21_063 (可能已过期)
            MinionFactory.RegisterPassiveAttackBonus("BG21_063", (m, p) => new MalGanisAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_063", (m, p) => new MalGanisHealthBonus());

            // Kalecgos: 友方龙获得 +1/+1
            MinionFactory.RegisterPassiveAttackBonus("BG21_064", (m, p) => new KalecgosAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_064", (m, p) => new KalecgosHealthBonus());
        }

        // ═══════════════════════════════════════════════════════════════
        // 特殊亡语召唤（需要自定义逻辑）
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterSpecialDeathrattles()
        {
            // Sly Raptor (T3): summons a random beast with stats set to 6/6
            MinionFactory.RegisterDeathrattle("BG25_806", (m, p) => new List<IDeathrattle>
            {
                new SlyRaptorDeathrattle()
            });

            // Kangor's Apprentice (T5): summons copies of the first 2 mechs that died
            // TODO: 需要跟踪死亡顺序

            // Twilight Hatchling (T1): summons a 3/3 that attacks immediately
            MinionFactory.RegisterDeathrattle("BG34_630", (m, p) => new List<IDeathrattle>
            {
                new TwilightHatchlingDeathrattle()
            });

            // Handless Forsaken (T3): summons a 2/1 with Reborn
            MinionFactory.RegisterDeathrattle("BG25_010", (m, p) => new List<IDeathrattle>
            {
                new HandlessForsakenDeathrattle()
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 亡语效果实现
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Goldrinn 亡语：给所有友方野兽 +8/+8
    /// </summary>
    public class GoldrinnDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int atkBonus = source.Golden ? 16 : 8;
            int hpBonus = source.Golden ? 16 : 8;
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
    /// Scarlet Skull 亡语：+1/+2 to a friendly Undead
    /// </summary>
    public class ScarletSkullDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();

        public void Trigger(Minion source, CombatState state)
        {
            int atkBonus = source.Golden ? 2 : 1;
            int hpBonus = source.Golden ? 4 : 2;

            var friendlyBoard = state.GetFriendlyBoard(source);
            var undead = new List<Minion>();
            foreach (var m in friendlyBoard)
            {
                if (m.PrimaryRace == "Undead" && m != source)
                    undead.Add(m);
            }

            if (undead.Count > 0)
            {
                var target = undead[Rng.Next(undead.Count)];
                target.BaseAttack += atkBonus;
                target.MaxHealth += hpBonus;
                target.CurrentHealth += hpBonus;
            }
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

    /// <summary>
    /// Silent Enforcer 亡语：对所有非友方恶魔的随从造成 2 伤害
    /// </summary>
    public class SilentEnforcerDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 4 : 2;
            var friendlyBoard = state.GetFriendlyBoard(source);
            var enemyBoard = state.GetEnemyBoard(source);

            // 对友方非恶魔随从造成伤害
            foreach (var m in friendlyBoard)
            {
                if (m.PrimaryRace != "Demon")
                    m.TakeDamage(damage);
            }

            // 对所有敌方随从造成伤害
            foreach (var m in enemyBoard)
                m.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Baneling 亡语：对随机敌方随从造成等同于攻击力的伤害
    /// </summary>
    public class BanelingDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();

        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? source.BaseAttack * 2 : source.BaseAttack;
            var enemyBoard = state.GetEnemyBoard(source);
            if (enemyBoard.Count == 0) return;

            var target = enemyBoard[Rng.Next(enemyBoard.Count)];
            target.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Plaguerunner 亡语：+1/+1 for each friendly minion that died this combat
    /// </summary>
    public class PlaguerunnerDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            // 需要跟踪死亡计数，暂时使用 ScriptDataNum1
            int deaths = source.ScriptDataNum1;
            int bonus = source.Golden ? deaths * 2 : deaths;

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
    /// Elementium Squirrel Bomb 亡语：对随机敌方随从造成 4 伤害（每个友方机械死亡 +4）
    /// </summary>
    public class ElementiumSquirrelBombDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();

        public void Trigger(Minion source, CombatState state)
        {
            int mechDeaths = source.ScriptDataNum1;
            int damage = source.Golden ? (mechDeaths + 1) * 8 : (mechDeaths + 1) * 4;

            var enemyBoard = state.GetEnemyBoard(source);
            if (enemyBoard.Count == 0) return;

            var target = enemyBoard[Rng.Next(enemyBoard.Count)];
            target.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Ingenious Inventor 亡语：+1/+1 for each friendly minion that died this combat
    /// </summary>
    public class IngeniousInventorDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int deaths = source.ScriptDataNum1;
            int bonus = source.Golden ? deaths * 2 : deaths;

            source.BaseAttack += bonus;
            source.MaxHealth += bonus;
            source.CurrentHealth += bonus;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 友方死亡触发实现
    // ═══════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════
    // 战斗开始触发实现
    // ═══════════════════════════════════════════════════════════════════════

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

            for (int i = 0; i < damage; i++)
            {
                if (enemyBoard.Count == 0) break;
                var target = enemyBoard[Rng.Next(enemyBoard.Count)];
                target.TakeDamage(1);
            }
        }
    }

    /// <summary>
    /// Spirit of Air: gives a random friendly minion Windfury, Divine Shield, and Taunt
    /// </summary>
    public class SpiritOfAirTrigger : IOnStartOfCombat
    {
        private static readonly Random Rng = new Random();

        public void OnStartOfCombat(Minion self, CombatState state)
        {
            var friendlyBoard = state.GetFriendlyBoard(self);
            if (friendlyBoard.Count == 0) return;

            var candidates = new List<Minion>();
            foreach (var m in friendlyBoard)
            {
                if (m != self)
                    candidates.Add(m);
            }

            if (candidates.Count == 0) return;

            var target = candidates[Rng.Next(candidates.Count)];
            target.Windfury = true;
            target.DivineShield = true;
            target.Taunt = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 攻击后触发实现
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Monstrous Macaw: 攻击后触发友方随从的亡语
    /// </summary>
    public class MonstrousMacawTrigger : IOnAfterAttack
    {
        private static readonly Random Rng = new Random();

        public void OnAfterAttack(Minion self, Minion attacker, Minion target, CombatState state)
        {
            if (attacker != self) return;

            var friendlyBoard = state.GetFriendlyBoard(self);
            var deathrattleMinions = new List<Minion>();
            foreach (var m in friendlyBoard)
            {
                if (m != self && (m.Deathrattles.Count > 0 || m.DeathrattleEffects.Count > 0))
                    deathrattleMinions.Add(m);
            }

            if (deathrattleMinions.Count == 0) return;

            var chosen = deathrattleMinions[Rng.Next(deathrattleMinions.Count)];

            // 触发召唤类亡语
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

            // 触发效果类亡语
            foreach (var effect in chosen.DeathrattleEffects)
            {
                effect.Trigger(chosen, state);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 友方召唤触发实现
    // ═══════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════
    // 被动加成实现
    // ═══════════════════════════════════════════════════════════════════════

    public class MalGanisAttackBonus : IPassiveAttackBonus
    {
        public int GetPassiveAttackBonus(Minion self, CombatState state)
        {
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

    // ═══════════════════════════════════════════════════════════════════════
    // 特殊亡语召唤实现
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sly Raptor 亡语：召唤一个随机野兽，属性设为 6/6
    /// </summary>
    public class SlyRaptorDeathrattle : IDeathrattle
    {
        private static readonly Random Rng = new Random();

        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 简化：召唤一个 6/6 的野兽 token
            var factory = MinionFactoryCache.GetFactory();
            var token = factory.CreateFromCardId("BG25_806t", source.ControlledByPlayer);
            token.BaseAttack = golden ? 12 : 6;
            token.BaseHealth = golden ? 12 : 6;
            token.MaxHealth = token.BaseHealth;
            token.CurrentHealth = token.BaseHealth;
            token.Golden = golden;
            return new List<Minion> { token };
        }
    }

    /// <summary>
    /// Twilight Hatchling 亡语：召唤一个 3/3 并立即攻击
    /// </summary>
    public class TwilightHatchlingDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var factory = MinionFactoryCache.GetFactory();
            var token = factory.CreateFromCardId("BG34_630t", source.ControlledByPlayer);
            token.BaseAttack = golden ? 6 : 3;
            token.BaseHealth = golden ? 6 : 3;
            token.MaxHealth = token.BaseHealth;
            token.CurrentHealth = token.BaseHealth;
            token.Golden = golden;
            // TODO: 立即攻击效果需要在 CombatSimulator 中处理
            return new List<Minion> { token };
        }
    }

    /// <summary>
    /// Handless Forsaken 亡语：召唤一个 2/1 并具有复生
    /// </summary>
    public class HandlessForsakenDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var factory = MinionFactoryCache.GetFactory();
            var token = factory.CreateFromCardId("BG25_010t", source.ControlledByPlayer);
            token.BaseAttack = golden ? 4 : 2;
            token.BaseHealth = golden ? 2 : 1;
            token.MaxHealth = token.BaseHealth;
            token.CurrentHealth = token.BaseHealth;
            token.Reborn = true;
            token.Golden = golden;
            return new List<Minion> { token };
        }
    }
}
