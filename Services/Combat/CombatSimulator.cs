using System;
using System.Collections.Generic;
using System.Linq;

namespace HBT.Services.Combat
{

/// <summary>
/// 核心战斗模拟器
/// 设计参考 twanvl 方案：flat struct + 函数指针，无嵌套 Scope
/// </summary>
public class CombatSimulator
{
    private static readonly Random _rng = new Random();

    /// <summary>
    /// 模拟一次战斗，返回伤害值（正=己方赢，负=对方赢，0=平局）
    /// </summary>
    public int SimulateFight(SimulationInput input)
    {
        var playerBoard = CloneBoard(input.PlayerBoard);
        var opponentBoard = CloneBoard(input.OpponentBoard);

        bool playerTurn = _rng.Next(2) == 0; // 随机决定先手

        for (int round = 0; round < 500; round++)
        {
            // 检查胜负
            if (playerBoard.Count == 0 && opponentBoard.Count == 0)
                return 0; // 平局
            if (playerBoard.Count == 0)
                return -CalculateDamage(opponentBoard, input.OpponentTier, input.DamageCap);
            if (opponentBoard.Count == 0)
                return CalculateDamage(playerBoard, input.PlayerTier, input.DamageCap);

            // 选攻击方
            var attackerSide = playerTurn ? playerBoard : opponentBoard;
            var defenderSide = playerTurn ? opponentBoard : playerBoard;

            var attacker = ChooseAttacker(attackerSide);
            if (attacker == null) break;

            // 选目标
            var target = ChooseTarget(defenderSide);
            if (target == null) break;

            // 执行攻击
            PerformAttack(attacker.Value, target.Value, attackerSide, defenderSide);

            // 清理死亡随从 + 触发亡语
            ResolveDeaths(attackerSide);
            ResolveDeaths(defenderSide);

            // 风怒处理
            if (attacker.Value.IsAlive && (attacker.Value.Windfury || attacker.Value.MegaWindfury))
            {
                int extraAttacks = attacker.Value.MegaWindfury ? 3 : 1;
                for (int i = 0; i < extraAttacks; i++)
                {
                    if (defenderSide.Count == 0) break;
                    target = ChooseTarget(defenderSide);
                    if (target == null) break;

                    var a = FindMinion(attackerSide, attacker.Value.CardId);
                    if (a == null) break;

                    PerformAttack(a.Value, target.Value, attackerSide, defenderSide);
                    ResolveDeaths(attackerSide);
                    ResolveDeaths(defenderSide);
                }
            }

            playerTurn = !playerTurn;
        }

        // 500 回合未分胜负 → 按存活随从 star sum 判断
        int playerStars = playerBoard.Sum(m => m.BaseAttack + m.BaseHealth);
        int opponentStars = opponentBoard.Sum(m => m.BaseAttack + m.BaseHealth);
        if (playerStars > opponentStars)
            return CalculateDamage(playerBoard, input.PlayerTier, input.DamageCap);
        if (opponentStars > playerStars)
            return -CalculateDamage(opponentBoard, input.OpponentTier, input.DamageCap);
        return 0;
    }

    // ── 攻击者选择 ──

    private SimMinion? ChooseAttacker(List<SimMinion> side)
    {
        // 嘲讽优先？不，攻击者是自己选的，从左到右第一个未攻击的
        // 简化：随机选一个可攻击的
        var valid = side.Where(m => m.IsAlive && m.Attack > 0).ToList();
        if (valid.Count == 0) return null;
        return valid[_rng.Next(valid.Count)];
    }

    // ── 目标选择 ──

    private SimMinion? ChooseTarget(List<SimMinion> side)
    {
        var alive = side.Where(m => m.IsAlive).ToList();
        if (alive.Count == 0) return null;

        // 嘲讽优先
        var taunts = alive.Where(m => m.Taunt).ToList();
        if (taunts.Count > 0)
            return taunts[_rng.Next(taunts.Count)];

        return alive[_rng.Next(alive.Count)];
    }

    // ── 执行攻击 ──

    private void PerformAttack(SimMinion attacker, SimMinion target,
        List<SimMinion> attackerSide, List<SimMinion> defenderSide)
    {
        // 攻击者对目标造成伤害
        target.TakeDamage(attacker.Attack);

        // 目标反击（除非攻击者免疫）
        if (target.IsAlive)
        {
            attacker.TakeDamage(target.Attack);
        }

        // 毒/烈毒：直接击杀
        if (attacker.IsAlive && target.IsAlive)
        {
            if (attacker.Poisonous || attacker.Venomous)
                target.CurrentHealth = 0;
            if (target.Poisonous || target.Venomous)
                attacker.CurrentHealth = 0;
        }

        // 顺劈：对相邻随从造成等量伤害
        if (attacker.IsAlive && attacker.Cleave)
        {
            var targetIdx = defenderSide.IndexOf(target);
            if (targetIdx > 0)
                defenderSide[targetIdx - 1].TakeDamage(attacker.Attack);
            if (targetIdx < defenderSide.Count - 1)
                defenderSide[targetIdx + 1].TakeDamage(attacker.Attack);
        }
    }

    // ── 死亡处理 + 亡语 ──

    private void ResolveDeaths(List<SimMinion> side)
    {
        // 找出死亡随从
        var dead = side.Where(m => !m.IsAlive).ToList();
        if (dead.Count == 0) return;

        // 移除死亡随从
        foreach (var d in dead)
            side.Remove(d);

        // 触发亡语
        foreach (var d in dead)
        {
            if (d.Deathrattle != null)
            {
                var summons = d.Deathrattle(d);
                if (summons != null)
                {
                    foreach (var s in summons)
                    {
                        if (side.Count < 7) // 最多 7 个随从
                            side.Add(s);
                    }
                }
            }

            // 复生：重新召唤（1 血）
            if (d.Reborn)
            {
                var reborn = d.Clone();
                reborn.CurrentHealth = 1;
                reborn.Reborn = false; // 复生只触发一次
                if (side.Count < 7)
                    side.Add(reborn);
            }
        }
    }

    // ── 伤害计算 ──

    private int CalculateDamage(List<SimMinion> survivingMinions, int tier, int damageCap)
    {
        int stars = survivingMinions.Sum(m => m.Tier);
        int damage = stars + tier;

        // 伤害上限（从游戏 tag 读取）
        if (damageCap > 0 && damage > damageCap)
            damage = damageCap;

        return damage;
    }

    // ── 辅助 ──

    private List<SimMinion> CloneBoard(List<SimMinion> board)
    {
        return board.Select(m => m.Clone()).ToList();
    }

    private SimMinion? FindMinion(List<SimMinion> side, string cardId)
    {
        for (int i = 0; i < side.Count; i++)
        {
            if (side[i].CardId == cardId && side[i].IsAlive)
                return side[i];
        }
        return null;
    }
}

}
