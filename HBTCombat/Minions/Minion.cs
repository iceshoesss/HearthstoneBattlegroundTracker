using System;
using System.Collections.Generic;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 战斗模拟用随从基类
    /// 参考 BobsBuddy.Minion 设计，支持行为接口
    /// </summary>
    public class Minion
    {
        // === 基础属性 ===
        public string CardId { get; set; }
        public string Name { get; set; }
        public int BaseAttack { get; set; }
        public int BaseHealth { get; set; }
        public int MaxHealth { get; set; }
        public int CurrentHealth { get; set; }
        public int Tier { get; set; }
        public string PrimaryRace { get; set; }
        public bool ControlledByPlayer { get; set; }
        public int GameId { get; set; }

        // === 关键词 ===
        public bool Taunt { get; set; }
        public bool DivineShield { get; set; }
        public bool Poisonous { get; set; }
        public bool Venomous { get; set; }
        public bool Windfury { get; set; }
        public bool MegaWindfury { get; set; }
        public bool Cleave { get; set; }
        public bool Reborn { get; set; }
        public bool Stealth { get; set; }
        public bool Golden { get; set; }

        // === 战斗状态 ===
        public bool HasAttacked { get; set; }
        public int Position { get; set; }
        public int AvengeCounter { get; set; }

        // === ScriptData（用于某些随从的特殊计数器） ===
        public int ScriptDataNum1 { get; set; }
        public int ScriptDataNum2 { get; set; }
        public int ScriptDataNum3 { get; set; }
        public int ScriptDataNum4 { get; set; }

        // === 行为接口列表 ===
        public List<IDeathrattle> Deathrattles { get; set; } = new List<IDeathrattle>();
        public List<IDeathrattleEffect> DeathrattleEffects { get; set; } = new List<IDeathrattleEffect>();
        public List<IOnStartOfCombat> StartOfCombatTriggers { get; set; } = new List<IOnStartOfCombat>();
        public List<IOnAfterAttack> AfterAttackTriggers { get; set; } = new List<IOnAfterAttack>();
        public List<IOnFriendlyMinionDied> FriendlyMinionDiedTriggers { get; set; } = new List<IOnFriendlyMinionDied>();
        public List<IOnFriendlyMinionSummoned> FriendlyMinionSummonedTriggers { get; set; } = new List<IOnFriendlyMinionSummoned>();
        public List<IAvenge> AvengeTriggers { get; set; } = new List<IAvenge>();
        public List<IPassiveAttackBonus> PassiveAttackBonuses { get; set; } = new List<IPassiveAttackBonus>();
        public List<IPassiveHealthBonus> PassiveHealthBonuses { get; set; } = new List<IPassiveHealthBonus>();
        public IRebornBehavior RebornBehavior { get; set; }

        // === 便捷属性 ===
        public int Attack => BaseAttack + GetPassiveAttackBonus();
        public int Health => CurrentHealth;
        public bool IsAlive => CurrentHealth > 0;
        public bool HasDivineShield => DivineShield;

        /// <summary>
        /// 获取当前攻击次数上限
        /// </summary>
        public int MaxAttacks => MegaWindfury ? 4 : (Windfury ? 2 : 1);

        /// <summary>
        /// 获取被动攻击加成总和
        /// </summary>
        private int GetPassiveAttackBonus()
        {
            int bonus = 0;
            // 注意：被动加成需要 CombatState，这里只返回基础值
            // 实际加成在战斗中通过 GetEffectiveAttack(CombatState) 计算
            return bonus;
        }

        /// <summary>
        /// 获取考虑被动加成后的实际攻击力
        /// </summary>
        public int GetEffectiveAttack(CombatState state)
        {
            int bonus = 0;
            foreach (var pab in PassiveAttackBonuses)
                bonus += pab.GetPassiveAttackBonus(this, state);
            return BaseAttack + bonus;
        }

        /// <summary>
        /// 获取考虑被动加成后的实际生命值上限
        /// </summary>
        public int GetEffectiveMaxHealth(CombatState state)
        {
            int bonus = 0;
            foreach (var phb in PassiveHealthBonuses)
                bonus += phb.GetPassiveHealthBonus(this, state);
            return MaxHealth + bonus;
        }

        /// <summary>
        /// 受到伤害（考虑圣盾）
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            if (DivineShield)
            {
                DivineShield = false;
                return;
            }
            CurrentHealth -= amount;
        }

        /// <summary>
        /// 治疗
        /// </summary>
        public void Heal(int amount)
        {
            if (amount <= 0) return;
            CurrentHealth = Math.Min(CurrentHealth + amount, MaxHealth);
        }

        /// <summary>
        /// 克隆随从（用于模拟时复制面板状态）
        /// </summary>
        public Minion Clone()
        {
            var clone = new Minion
            {
                CardId = CardId,
                Name = Name,
                BaseAttack = BaseAttack,
                BaseHealth = BaseHealth,
                MaxHealth = MaxHealth,
                CurrentHealth = CurrentHealth,
                Tier = Tier,
                PrimaryRace = PrimaryRace,
                ControlledByPlayer = ControlledByPlayer,
                GameId = GameId,
                Taunt = Taunt,
                DivineShield = DivineShield,
                Poisonous = Poisonous,
                Venomous = Venomous,
                Windfury = Windfury,
                MegaWindfury = MegaWindfury,
                Cleave = Cleave,
                Reborn = Reborn,
                Stealth = Stealth,
                Golden = Golden,
                HasAttacked = HasAttacked,
                Position = Position,
                AvengeCounter = AvengeCounter,
                ScriptDataNum1 = ScriptDataNum1,
                ScriptDataNum2 = ScriptDataNum2,
                ScriptDataNum3 = ScriptDataNum3,
                ScriptDataNum4 = ScriptDataNum4,
                RebornBehavior = RebornBehavior,
            };

            // 共享行为引用（不可变）
            clone.Deathrattles = Deathrattles;
            clone.DeathrattleEffects = DeathrattleEffects;
            clone.StartOfCombatTriggers = StartOfCombatTriggers;
            clone.AfterAttackTriggers = AfterAttackTriggers;
            clone.FriendlyMinionDiedTriggers = FriendlyMinionDiedTriggers;
            clone.FriendlyMinionSummonedTriggers = FriendlyMinionSummonedTriggers;
            clone.AvengeTriggers = AvengeTriggers;
            clone.PassiveAttackBonuses = PassiveAttackBonuses;
            clone.PassiveHealthBonuses = PassiveHealthBonuses;

            return clone;
        }

        /// <summary>
        /// 检查是否有某个行为接口
        /// </summary>
        public bool HasBehavior<T>() where T : class
        {
            if (typeof(T) == typeof(IDeathrattle) && Deathrattles.Count > 0) return true;
            if (typeof(T) == typeof(IOnStartOfCombat) && StartOfCombatTriggers.Count > 0) return true;
            if (typeof(T) == typeof(IOnAfterAttack) && AfterAttackTriggers.Count > 0) return true;
            if (typeof(T) == typeof(IOnFriendlyMinionDied) && FriendlyMinionDiedTriggers.Count > 0) return true;
            if (typeof(T) == typeof(IOnFriendlyMinionSummoned) && FriendlyMinionSummonedTriggers.Count > 0) return true;
            if (typeof(T) == typeof(IAvenge) && AvengeTriggers.Count > 0) return true;
            if (typeof(T) == typeof(IPassiveAttackBonus) && PassiveAttackBonuses.Count > 0) return true;
            if (typeof(T) == typeof(IPassiveHealthBonus) && PassiveHealthBonuses.Count > 0) return true;
            if (typeof(T) == typeof(IRebornBehavior) && RebornBehavior != null) return true;
            return false;
        }

        public override string ToString()
        {
            return $"{CardId} [{Attack}/{Health}]";
        }
    }
}
