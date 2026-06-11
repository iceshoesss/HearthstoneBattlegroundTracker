using System;
using System.Collections.Generic;
using System.Linq;

namespace HBT.Services.Combat
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

            // 左到右选攻击方
            int attackerIdx = ChooseAttacker(attackerSide);
            if (attackerIdx < 0) break;

            // 选目标
            int targetIdx = ChooseTarget(defenderSide);
            if (targetIdx < 0) break;

            // 执行攻击（含风怒多段攻击）
            ExecuteAttack(attackerSide, attackerIdx, defenderSide, targetIdx);

            playerTurn = !playerTurn;
        }

        // 500 回合未分胜负
        int playerStars = playerBoard.Sum(m => m.Tier);
        int opponentStars = opponentBoard.Sum(m => m.Tier);
        if (playerStars > opponentStars)
            return CalculateDamage(playerBoard, input.PlayerTier, input.DamageCap);
        if (opponentStars > playerStars)
            return -CalculateDamage(opponentBoard, input.OpponentTier, input.DamageCap);
        return 0;
    }

    // ── 攻击方选择：左到右第一个未攻击的 ──

    private int ChooseAttacker(List<SimMinion> side)
    {
        // 左到右找第一个未攻击的有效攻击者
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && !side[i].HasAttacked && side[i].Attack > 0)
                return i;
        }

        // 所有随从都攻击过了 → 重置标记
        for (int i = 0; i < side.Count; i++)
        {
            var m = side[i];
            m.HasAttacked = false;
            side[i] = m;
        }

        // 重新从左开始
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].Attack > 0)
                return i;
        }

        return -1;
    }

    // ── 目标选择：嘲讽优先（随机），无嘲讽则随机；排除隐身 ──

    private int ChooseTarget(List<SimMinion> side)
    {
        var candidates = new List<int>();
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && !side[i].Stealth)
                candidates.Add(i);
        }

        if (candidates.Count == 0) return -1;

        // 嘲讽优先（随机选一个嘲讽）
        var taunts = candidates.Where(i => side[i].Taunt).ToList();
        if (taunts.Count > 0)
            return taunts[_rng.Next(taunts.Count)];

        // 随机选
        return candidates[_rng.Next(candidates.Count)];
    }

    // ── 执行攻击（含风怒、顺劈、毒、反击）──

    private void ExecuteAttack(List<SimMinion> attackerSide, int attackerIdx,
        List<SimMinion> defenderSide, int targetIdx)
    {
        var attacker = attackerSide[attackerIdx];
        int maxAttacks = attacker.MegaWindfury ? 4 : (attacker.Windfury ? 2 : 1);

        for (int atk = 0; atk < maxAttacks; atk++)
        {
            if (defenderSide.Count == 0) break;

            // 重新选目标（风怒每次攻击重新选）
            int curTarget = ChooseTarget(defenderSide);
            if (curTarget < 0) break;

            // 重新定位攻击者（可能因死亡变化）
            int curAttacker = FindAliveIndex(attackerSide, attacker);
            if (curAttacker < 0) break;

            // 执行单次攻击
            PerformSingleAttack(attackerSide, curAttacker, defenderSide, curTarget);

            // 解析死亡
            ResolveAllDeaths(attackerSide, defenderSide);

            // 隐身随从攻击后移除隐身
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

        // 标记攻击完成
        attackerIdx = FindAliveIndex(attackerSide, attacker);
        if (attackerIdx >= 0)
        {
            var m = attackerSide[attackerIdx];
            m.HasAttacked = true;
            attackerSide[attackerIdx] = m;
        }
    }

    // ── 单次攻击 ──

    private void PerformSingleAttack(List<SimMinion> attackerSide, int attackerIdx,
        List<SimMinion> defenderSide, int targetIdx)
    {
        var attacker = attackerSide[attackerIdx];
        var target = defenderSide[targetIdx];

        // 攻击者对目标造成伤害
        target.TakeDamage(attacker.Attack);

        // 目标反击
        if (target.IsAlive)
        {
            attacker.TakeDamage(target.Attack);
        }

        // 毒/烈毒
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

        // 顺劈
        if (attacker.IsAlive && attacker.Cleave)
        {
            if (targetIdx > 0)
                defenderSide[targetIdx - 1].TakeDamage(attacker.Attack);
            if (targetIdx < defenderSide.Count - 1)
                defenderSide[targetIdx + 1].TakeDamage(attacker.Attack);
        }

        // 写回攻击者
        attackerSide[attackerIdx] = attacker;
    }

    // ── 死亡处理（双方同时结算）──

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
            reborn.DivineShield = dead.DivineShield; // Reborn 恢复圣盾
            if (side.Count < 7)
                side.Add(reborn);
        }
    }

    // ── 伤害计算 ──

    private int CalculateDamage(List<SimMinion> survivingMinions, int tier, int damageCap)
    {
        int stars = survivingMinions.Sum(m => m.Tier);
        int damage = stars + tier;
        if (damageCap > 0 && damage > damageCap)
            damage = damageCap;
        return damage;
    }

    // ── 辅助 ──

    private List<SimMinion> CloneBoard(List<SimMinion> board)
    {
        return board.Select(m => m.Clone()).ToList();
    }

    private int FindAliveIndex(List<SimMinion> side, SimMinion reference)
    {
        // 先用 CardId 找同名存活随从
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].CardId == reference.CardId && !side[i].HasAttacked)
                return i;
        }
        // fallback: 找任意同名存活随从
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].IsAlive && side[i].CardId == reference.CardId)
                return i;
        }
        return -1;
    }
}

}
