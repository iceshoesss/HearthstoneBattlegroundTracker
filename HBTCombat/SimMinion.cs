using System;

namespace HBTCombat
{

/// <summary>
/// 战斗模拟用随从（struct，无 GC 压力）
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

    // 亡语回调
    public Func<SimMinion, SimMinion[]>? Deathrattle;

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
}

}
