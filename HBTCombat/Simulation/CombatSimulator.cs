using System;
using System.Collections.Generic;
using System.Linq;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 核心战斗模拟器
    /// 参考 BobsBuddy 的完整战斗逻辑
    /// </summary>
    public class CombatSimulator
    {
        private readonly Random _rng = new Random();
        private const int MaxRounds = 500;

        /// <summary>
        /// 模拟一次战斗，返回伤害值（正=己方赢，负=对方赢，0=平局）
        /// </summary>
        public int SimulateFight(SimulationInput input)
        {
            var state = new CombatState
            {
                PlayerBoard = CloneBoard(input.PlayerBoard),
                OpponentBoard = CloneBoard(input.OpponentBoard),
                AvailableRaces = input.AvailableRaces,
            };

            // 更新位置
            UpdatePositions(state.PlayerBoard);
            UpdatePositions(state.OpponentBoard);

            // 触发战斗开始效果
            TriggerStartOfCombat(state);

            // 检查战斗开始后是否已结束
            if (CheckBattleEnd(state, input, out int earlyResult))
                return earlyResult;

            // 随机选择先手
            bool playerTurn = _rng.Next(2) == 0;
            state.IsPlayerTurn = playerTurn;

            for (int round = 0; round < MaxRounds; round++)
            {
                state.RoundNumber = round;

                var attackerSide = playerTurn ? state.PlayerBoard : state.OpponentBoard;
                var defenderSide = playerTurn ? state.OpponentBoard : state.PlayerBoard;

                // 选择攻击者
                int attackerIdx = ChooseAttacker(attackerSide);
                if (attackerIdx < 0) break;

                // 选择目标
                int targetIdx = ChooseTarget(defenderSide);
                if (targetIdx < 0) break;

                // 执行攻击（含风怒多段攻击）
                ExecuteAttack(attackerSide, attackerIdx, defenderSide, targetIdx, state);

                // 检查战斗结束
                if (CheckBattleEnd(state, input, out int result))
                    return result;

                playerTurn = !playerTurn;
                state.IsPlayerTurn = playerTurn;
            }

            // MaxRounds 未分胜负，按星级判定
            return TiebreakByStars(state, input);
        }

        // ── 战斗开始触发 ──

        private void TriggerStartOfCombat(CombatState state)
        {
            // 己方随从的战斗开始效果
            foreach (var minion in state.PlayerBoard.ToList())
            {
                if (!minion.IsAlive) continue;
                foreach (var trigger in minion.StartOfCombatTriggers)
                {
                    trigger.OnStartOfCombat(minion, state);
                }
            }

            // 对方随从的战斗开始效果
            foreach (var minion in state.OpponentBoard.ToList())
            {
                if (!minion.IsAlive) continue;
                foreach (var trigger in minion.StartOfCombatTriggers)
                {
                    trigger.OnStartOfCombat(minion, state);
                }
            }

            // 清理战斗开始中死亡的随从
            ResolveAllDeaths(state);
        }

        // ── 攻击方选择：左到右第一个未攻击的 ──

        private int ChooseAttacker(List<Minion> side)
        {
            // 左到右找第一个未攻击的有效攻击者
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].IsAlive && !side[i].HasAttacked && side[i].Attack > 0)
                    return i;
            }

            // 所有随从都攻击过了 → 重置标记
            foreach (var m in side)
                m.HasAttacked = false;

            // 重新从左开始
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].IsAlive && side[i].Attack > 0)
                    return i;
            }

            return -1;
        }

        // ── 目标选择：嘲讽优先（随机），无嘲讽则随机；排除隐身 ──

        private int ChooseTarget(List<Minion> side)
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

        private void ExecuteAttack(List<Minion> attackerSide, int attackerIdx,
            List<Minion> defenderSide, int targetIdx, CombatState state)
        {
            var attacker = attackerSide[attackerIdx];
            int maxAttacks = attacker.MaxAttacks;

            for (int atk = 0; atk < maxAttacks; atk++)
            {
                if (defenderSide.Count == 0) break;
                if (!attacker.IsAlive) break;

                // 重新选目标（风怒每次攻击重新选）
                int curTarget = ChooseTarget(defenderSide);
                if (curTarget < 0) break;

                // 执行单次攻击
                PerformSingleAttack(attackerSide, attacker, defenderSide, curTarget, state);

                // 解析死亡
                ResolveAllDeaths(state);

                // 隐身随从攻击后移除隐身
                if (attacker.IsAlive && attacker.Stealth)
                    attacker.Stealth = false;
            }

            // 标记攻击完成
            if (attacker.IsAlive)
                attacker.HasAttacked = true;
        }

        // ── 单次攻击 ──

        private void PerformSingleAttack(List<Minion> attackerSide, Minion attacker,
            List<Minion> defenderSide, int targetIdx, CombatState state)
        {
            var target = defenderSide[targetIdx];

            // 计算有效攻击力（含被动加成）
            int attackerAttack = attacker.GetEffectiveAttack(state);
            int targetAttack = target.GetEffectiveAttack(state);

            // 记录毒/烈毒状态（在伤害前，因为伤害可能杀死随从）
            bool attackerHasPoison = (attacker.Poisonous || attacker.Venomous) && attackerAttack > 0;
            bool targetHasPoison = (target.Poisonous || target.Venomous) && targetAttack > 0;

            // 攻击者对目标造成伤害
            target.TakeDamage(attackerAttack);

            // 目标反击（如果还活着）
            if (target.IsAlive)
            {
                attacker.TakeDamage(targetAttack);
            }

            // 毒/烈毒：对目标生效（只要攻击者有毒且造成了伤害）
            if (attackerHasPoison)
            {
                target.CurrentHealth = 0;
            }
            // 毒/烈毒：对攻击者生效（只要目标有毒且造成了伤害）
            if (targetHasPoison)
            {
                attacker.CurrentHealth = 0;
            }

            // 顺劈：对相邻随从造成伤害
            if (attacker.IsAlive && attacker.Cleave)
            {
                if (targetIdx > 0 && defenderSide[targetIdx - 1].IsAlive)
                    defenderSide[targetIdx - 1].TakeDamage(attackerAttack);
                if (targetIdx < defenderSide.Count - 1 && defenderSide[targetIdx + 1].IsAlive)
                    defenderSide[targetIdx + 1].TakeDamage(attackerAttack);
            }

            // 触发攻击后效果
            foreach (var trigger in attacker.AfterAttackTriggers)
                trigger.OnAfterAttack(attacker, attacker, target, state);
            foreach (var trigger in target.AfterAttackTriggers)
                trigger.OnAfterAttack(target, attacker, target, state);
        }

        // ── 死亡处理 ──

        private void ResolveAllDeaths(CombatState state)
        {
            var playerDead = state.PlayerBoard.Where(m => !m.IsAlive).ToList();
            var opponentDead = state.OpponentBoard.Where(m => !m.IsAlive).ToList();

            // 移除死亡随从
            foreach (var d in playerDead) state.PlayerBoard.Remove(d);
            foreach (var d in opponentDead) state.OpponentBoard.Remove(d);

            // 更新位置
            UpdatePositions(state.PlayerBoard);
            UpdatePositions(state.OpponentBoard);

            // 触发友方随从死亡效果
            foreach (var dead in playerDead)
            {
                // 触发己方存活随从的 IOnFriendlyMinionDied
                foreach (var minion in state.PlayerBoard.ToList())
                {
                    if (!minion.IsAlive) continue;
                    foreach (var trigger in minion.FriendlyMinionDiedTriggers)
                        trigger.OnFriendlyMinionDied(minion, dead, state);
                    // 更新复仇计数
                    minion.AvengeCounter++;
                    foreach (var avenge in minion.AvengeTriggers)
                    {
                        if (minion.AvengeCounter >= avenge.AvengeCount)
                        {
                            avenge.OnAvenge(minion, state);
                            minion.AvengeCounter = 0;
                        }
                    }
                }
            }

            foreach (var dead in opponentDead)
            {
                foreach (var minion in state.OpponentBoard.ToList())
                {
                    if (!minion.IsAlive) continue;
                    foreach (var trigger in minion.FriendlyMinionDiedTriggers)
                        trigger.OnFriendlyMinionDied(minion, dead, state);
                    minion.AvengeCounter++;
                    foreach (var avenge in minion.AvengeTriggers)
                    {
                        if (minion.AvengeCounter >= avenge.AvengeCount)
                        {
                            avenge.OnAvenge(minion, state);
                            minion.AvengeCounter = 0;
                        }
                    }
                }
            }

            // 触发亡语（召唤类）
            foreach (var dead in playerDead)
                TriggerDeathrattle(dead, state.PlayerBoard, state);
            foreach (var dead in opponentDead)
                TriggerDeathrattle(dead, state.OpponentBoard, state);

            // 触发亡语效果类
            foreach (var dead in playerDead)
                TriggerDeathrattleEffect(dead, state);
            foreach (var dead in opponentDead)
                TriggerDeathrattleEffect(dead, state);

            // 触发复生
            foreach (var dead in playerDead)
                TriggerReborn(dead, state.PlayerBoard);
            foreach (var dead in opponentDead)
                TriggerReborn(dead, state.OpponentBoard);

            // 清理新死亡的随从（亡语/复生可能产生新的死亡）
            var newPlayerDead = state.PlayerBoard.Where(m => !m.IsAlive).ToList();
            var newOpponentDead = state.OpponentBoard.Where(m => !m.IsAlive).ToList();
            foreach (var d in newPlayerDead) state.PlayerBoard.Remove(d);
            foreach (var d in newOpponentDead) state.OpponentBoard.Remove(d);

            // 如果有新的死亡，递归处理
            if (newPlayerDead.Count > 0 || newOpponentDead.Count > 0)
                ResolveAllDeaths(state);
        }

        private void TriggerDeathrattle(Minion dead, List<Minion> side, CombatState state)
        {
            foreach (var dr in dead.Deathrattles)
            {
                var summons = dr.TriggerDeathrattle(dead, dead.Golden);
                if (summons != null)
                {
                    foreach (var s in summons)
                    {
                        if (side.Count < 7)
                        {
                            side.Add(s);
                            // 触发友方随从召唤效果
                            foreach (var minion in side.ToList())
                            {
                                if (minion == s || !minion.IsAlive) continue;
                                foreach (var trigger in minion.FriendlyMinionSummonedTriggers)
                                    trigger.OnFriendlyMinionSummoned(minion, s, state);
                            }
                        }
                    }
                }
            }
        }

        private void TriggerDeathrattleEffect(Minion dead, CombatState state)
        {
            foreach (var effect in dead.DeathrattleEffects)
            {
                effect.Trigger(dead, state);
            }
        }

        private void TriggerReborn(Minion dead, List<Minion> side)
        {
            if (!dead.Reborn) return;

            Minion reborn;
            if (dead.RebornBehavior != null)
            {
                reborn = dead.RebornBehavior.OnReborn(dead);
            }
            else
            {
                // 默认复生：1HP，移除复生标记
                reborn = dead.Clone();
                reborn.CurrentHealth = 1;
                reborn.Reborn = false;
                reborn.DivineShield = false; // 复生不保留圣盾
            }

            if (reborn != null && side.Count < 7)
                side.Add(reborn);
        }

        // ── 战斗结束检查 ──

        private bool CheckBattleEnd(CombatState state, SimulationInput input, out int result)
        {
            bool playerEmpty = state.PlayerBoard.Count == 0;
            bool opponentEmpty = state.OpponentBoard.Count == 0;

            if (playerEmpty && opponentEmpty)
            {
                result = 0;
                return true;
            }
            if (playerEmpty)
            {
                result = -CalculateDamage(state.OpponentBoard, input.OpponentTier, input.DamageCap);
                return true;
            }
            if (opponentEmpty)
            {
                result = CalculateDamage(state.PlayerBoard, input.PlayerTier, input.DamageCap);
                return true;
            }

            result = 0;
            return false;
        }

        private int TiebreakByStars(CombatState state, SimulationInput input)
        {
            int playerStars = state.PlayerBoard.Sum(m => m.Tier);
            int opponentStars = state.OpponentBoard.Sum(m => m.Tier);
            if (playerStars > opponentStars)
                return CalculateDamage(state.PlayerBoard, input.PlayerTier, input.DamageCap);
            if (opponentStars > playerStars)
                return -CalculateDamage(state.OpponentBoard, input.OpponentTier, input.DamageCap);
            return 0;
        }

        // ── 伤害计算 ──

        private int CalculateDamage(List<Minion> survivingMinions, int tier, int damageCap)
        {
            int stars = survivingMinions.Sum(m => m.Tier);
            int damage = stars + tier;
            if (damageCap > 0 && damage > damageCap)
                damage = damageCap;
            return damage;
        }

        // ── 辅助 ──

        private List<Minion> CloneBoard(List<Minion> board)
        {
            return board.Select(m => m.Clone()).ToList();
        }

        private void UpdatePositions(List<Minion> board)
        {
            for (int i = 0; i < board.Count; i++)
                board[i].Position = i;
        }
    }
}
