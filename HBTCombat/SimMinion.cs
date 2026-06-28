using System;
using System.Collections.Generic;
using System.Linq;

namespace HBTCombat
{
    /// <summary>
    /// 简化的随从输入结构（向后兼容）
    /// 用于 HBT 主程序创建战斗模拟输入
    /// 内部会转换为完整的 Minion 对象
    /// </summary>
    public struct SimMinion
    {
        public string CardId;
        public int BaseAttack;
        public int BaseHealth;
        public int CurrentHealth;
        public int Tier;

        // 关键词
        public bool Taunt;
        public bool DivineShield;
        public bool Poisonous;
        public bool Venomous;
        public bool Windfury;
        public bool MegaWindfury;
        public bool Cleave;
        public bool Reborn;
        public bool Stealth;
        public bool HasAttacked;
        public bool Golden;

        // 亡语回调（向后兼容）
        public Func<SimMinion, SimMinion[]> Deathrattle;

        // ScriptData
        public int ScriptDataNum1;
        public int ScriptDataNum2;

        public int Attack => BaseAttack;
        public int Health => CurrentHealth;
        public bool IsAlive => CurrentHealth > 0;
        public bool HasDivineShield => DivineShield;

        public static SimMinion Create(string cardId, int attack, int health, int tier = 1)
        {
            return new SimMinion
            {
                CardId = cardId,
                BaseAttack = attack,
                BaseHealth = health,
                CurrentHealth = health,
                Tier = tier,
            };
        }

        public SimMinion Clone()
        {
            return new SimMinion
            {
                CardId = CardId,
                BaseAttack = BaseAttack,
                BaseHealth = BaseHealth,
                CurrentHealth = CurrentHealth,
                Tier = Tier,
                Taunt = Taunt,
                DivineShield = DivineShield,
                Poisonous = Poisonous,
                Venomous = Venomous,
                Windfury = Windfury,
                MegaWindfury = MegaWindfury,
                Cleave = Cleave,
                Reborn = Reborn,
                Stealth = Stealth,
                HasAttacked = HasAttacked,
                Golden = Golden,
                Deathrattle = Deathrattle,
                ScriptDataNum1 = ScriptDataNum1,
                ScriptDataNum2 = ScriptDataNum2,
            };
        }

        public void TakeDamage(int amount)
        {
            if (DivineShield)
            {
                DivineShield = false;
                return;
            }
            CurrentHealth -= amount;
        }

        public override string ToString()
        {
            return $"{CardId} [{Attack}/{Health}]";
        }

        /// <summary>
        /// 转换为完整的 Minion 对象
        /// </summary>
        public Minion ToMinion(bool controlledByPlayer, MinionFactory factory = null)
        {
            if (factory == null)
                factory = MinionFactoryCache.GetFactory();

            return factory.CreateFromCardId(
                CardId, controlledByPlayer,
                BaseAttack, CurrentHealth, BaseHealth,
                Taunt, DivineShield, Poisonous, Venomous,
                Windfury, MegaWindfury, Stealth, Reborn,
                Golden, Tier, ScriptDataNum1, ScriptDataNum2
            );
        }
    }

    /// <summary>
    /// 向后兼容的 SimulationInput（使用 SimMinion）
    /// </summary>
    public class SimMinionInput
    {
        public List<SimMinion> PlayerBoard { get; set; } = new List<SimMinion>();
        public List<SimMinion> OpponentBoard { get; set; } = new List<SimMinion>();
        public int PlayerHealth { get; set; }
        public int OpponentHealth { get; set; }
        public int PlayerTier { get; set; }
        public int OpponentTier { get; set; }
        public int Turn { get; set; }
        public int DamageCap { get; set; }

        /// <summary>
        /// 转换为 SimulationInput
        /// </summary>
        public SimulationInput ToSimulationInput()
        {
            var factory = MinionFactoryCache.GetFactory();
            return new SimulationInput
            {
                PlayerBoard = PlayerBoard.Select(m => m.ToMinion(true, factory)).ToList(),
                OpponentBoard = OpponentBoard.Select(m => m.ToMinion(false, factory)).ToList(),
                PlayerHealth = PlayerHealth,
                OpponentHealth = OpponentHealth,
                PlayerTier = PlayerTier,
                OpponentTier = OpponentTier,
                Turn = Turn,
                DamageCap = DamageCap,
            };
        }
    }
}
