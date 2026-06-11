using System;
using System.Collections.Generic;
using System.Linq;

namespace HBTCombat
{

/// <summary>
/// 核心战斗模拟器
/// 参考 BobsBuddy (HDT) 和 Firestone 的攻击逻辑
/// </summary>
public class CombatSimulator
{
    private readonly Random _rng = new Random();

    /// <summary>
    /// 模拟一次战斗，返回伤害值（正=己方赢，负=对方赢，0=平局）
    /// </summary>
    public int SimulateFight(SimulationInput input)
    {
        var playerBoard = CloneBoard(input.PlayerBoard);
        var opponentBoard = CloneBoard(input.OpponentBoard);

        bool playerTurn = _rng.Next(2) == 0;

        for (int round = 0; round < 500; round++)
        {
            if (playerBoard.Count == 0 && opponentBoard.Count == 0)
                return 0;
            if (playerBoard.Count == 0)
                return -CalculateDamage(opponentBoard, input.OpponentTier, input.DamageCap);
            if (opponentBoard.Count == 0)
                return CalculateDamage(playerBoard, input.PlayerTier, input.DamageCap);

            var attackerSide = playerTurn ? playerBoard : opponentBoard;
            var defenderSide = playerTurn ? opponentBoard : playerBoard;

            int attackerIdx = ChooseAttacker(attackerSide);
            if (attackerIdx < 0) break;

            int targetIdx = ChooseTarget(defenderSide);
            if (targetIdx < 0) break;

            ExecuteAttack(attackerSide, attackerIdx, defenderSide, targetIdx);

            playerTurn = !playerTurn;
        }

        int playerStars = playerBoard.Sum(m => m.Tier);
        int opponentStars = opponentBoard.Sum(m => m.Tier);
        if (playerStars > opponentStars)
            return CalculateDamage(playerBoard, input.PlayerTier, input.DamageCap);
        if (opponentStars > playerStars)
            return -CalculateDamage(opponentBoard, input.OpponentTier, input.DamageCap);
        return 0;
    }

    private int ChooseAttacker(List<SimMinion> side)
    {
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && !side[i].HasAttacked && side[i].Attack > 0)
                return i;
        }

        for (int i = 0; i < side.Count; i++)
        {
            var m = side[i];
            m.HasAttacked = false;
            side[i] = m;
        }

        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].Attack > 0)
                return i;
        }

        return -1;
    }

    private int ChooseTarget(List<SimMinion> side)
    {
        var candidates = new List<int>();
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && !side[i].Stealth)
                candidates.Add(i);
        }

        if (candidates.Count == 0) return -1;

        var taunts = candidates.Where(i => side[i].Taunt).ToList();
        if (taunts.Count > 0)
            return taunts[_rng.Next(taunts.Count)];

        return candidates[_rng.Next(candidates.Count)];
    }

    private void ExecuteAttack(List<SimMinion> attackerSide, int attackerIdx,
        List<SimMinion> defenderSide, int targetIdx)
    {
        var attacker = attackerSide[attackerIdx];
        int maxAttacks = attacker.MegaWindfury ? 4 : (attacker.Windfury ? 2 : 1);

        for (int atk = 0; atk < maxAttacks; atk++)
        {
            if (defenderSide.Count == 0) break;

            int curTarget = ChooseTarget(defenderSide);
            if (curTarget < 0) break;

            int curAttacker = FindAliveIndex(attackerSide, attacker);
            if (curAttacker < 0) break;

            PerformSingleAttack(attackerSide, curAttacker, defenderSide, curTarget);

            ResolveAllDeaths(attackerSide, defenderSide);

            curAttacker = FindAliveIndex(attackerSide, attacker);
            if (curAttacker >= 0)
            {
                var m = attackerSide[curAttacker];
                if (m.Stealth)
                {
                    m.Stealth = false;
                    attackerSide[curAttacker] = m;
                }
            }
        }

        attackerIdx = FindAliveIndex(attackerSide, attacker);
        if (attackerIdx >= 0)
        {
            var m = attackerSide[attackerIdx];
            m.HasAttacked = true;
            attackerSide[attackerIdx] = m;
        }
    }

    private void PerformSingleAttack(List<SimMinion> attackerSide, int attackerIdx,
        List<SimMinion> defenderSide, int targetIdx)
    {
        var attacker = attackerSide[attackerIdx];
        var target = defenderSide[targetIdx];

        target.TakeDamage(attacker.Attack);

        if (target.IsAlive)
        {
            attacker.TakeDamage(target.Attack);
        }

        if (attacker.IsAlive && attacker.Attack > 0)
        {
            if (attacker.Poisonous || attacker.Venomous)
                target.CurrentHealth = 0;
        }
        if (target.IsAlive && target.Attack > 0)
        {
            if (target.Poisonous || target.Venomous)
                attacker.CurrentHealth = 0;
        }

        if (attacker.IsAlive && attacker.Cleave)
        {
            if (targetIdx > 0)
                defenderSide[targetIdx - 1].TakeDamage(attacker.Attack);
            if (targetIdx < defenderSide.Count - 1)
                defenderSide[targetIdx + 1].TakeDamage(attacker.Attack);
        }

        attackerSide[attackerIdx] = attacker;
    }

    private void ResolveAllDeaths(List<SimMinion> side1, List<SimMinion> side2)
    {
        var dead1 = side1.Where(m => !m.IsAlive).ToList();
        var dead2 = side2.Where(m => !m.IsAlive).ToList();

        foreach (var d in dead1) side1.Remove(d);
        foreach (var d in dead2) side2.Remove(d);

        foreach (var d in dead1) TriggerDeathrattle(d, side1);
        foreach (var d in dead2) TriggerDeathrattle(d, side2);

        foreach (var d in dead1) TriggerReborn(d, side1);
        foreach (var d in dead2) TriggerReborn(d, side2);
    }

    private void TriggerDeathrattle(SimMinion dead, List<SimMinion> side)
    {
        if (dead.Deathrattle != null)
        {
            var summons = dead.Deathrattle(dead);
            if (summons != null)
            {
                foreach (var s in summons)
                {
                    if (side.Count < 7)
                        side.Add(s);
                }
            }
        }
    }

    private void TriggerReborn(SimMinion dead, List<SimMinion> side)
    {
        if (dead.Reborn)
        {
            var reborn = dead.Clone();
            reborn.CurrentHealth = 1;
            reborn.Reborn = false;
            reborn.DivineShield = dead.DivineShield;
            if (side.Count < 7)
                side.Add(reborn);
        }
    }

    private int CalculateDamage(List<SimMinion> survivingMinions, int tier, int damageCap)
    {
        int stars = survivingMinions.Sum(m => m.Golden ? m.Tier * 2 : m.Tier);
        int damage = stars + tier;
        if (damageCap > 0 && damage > damageCap)
            damage = damageCap;
        return damage;
    }

    private List<SimMinion> CloneBoard(List<SimMinion> board)
    {
        return board.Select(m => m.Clone()).ToList();
    }

    private int FindAliveIndex(List<SimMinion> side, SimMinion reference)
    {
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].CardId == reference.CardId && !side[i].HasAttacked)
                return i;
        }
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].CardId == reference.CardId)
                return i;
        }
        return -1;
    }
}

}
