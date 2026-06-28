using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HBTCombat
{
    /// <summary>
    /// 多次模拟聚合器
    /// </summary>
    public class SimulationRunner
    {
        /// <summary>
        /// 运行 N 次模拟，返回聚合结果
        /// </summary>
        public static SimulationOutput Run(SimulationInput input, int iterations = 10000, int maxMs = 1500)
        {
            var sw = Stopwatch.StartNew();
            var bag = new ConcurrentBag<int>();

            // 多线程模拟（参考 BobsBuddy: ProcessorCount/2 线程）
            int threadCount = Math.Max(1, Environment.ProcessorCount / 2);
            int perThread = iterations / threadCount;

            // 设置 MinionFactory 缓存
            MinionFactoryCache.SetFactory(new MinionFactory());

            Parallel.For(0, threadCount, _ =>
            {
                var sim = new CombatSimulator();
                int count = 0;
                while (count < perThread && sw.ElapsedMilliseconds < maxMs)
                {
                    bag.Add(sim.SimulateFight(input));
                    count++;
                }
            });

            var results = bag.ToList();
            return Aggregate(results);
        }

        private static SimulationOutput Aggregate(List<int> results)
        {
            if (results.Count == 0)
                return new SimulationOutput();

            int wins = 0, ties = 0, losses = 0;
            var playerDamages = new List<int>();
            var opponentDamages = new List<int>();

            foreach (var r in results)
            {
                if (r > 0)
                {
                    wins++;
                    playerDamages.Add(r);
                    // 如果伤害等于对方最大可能血量，算作致命
                }
                else if (r < 0)
                {
                    losses++;
                    opponentDamages.Add(Math.Abs(r));
                }
                else
                {
                    ties++;
                }
            }

            int total = results.Count;
            var result = new SimulationOutput
            {
                WinRate = (float)wins / total,
                TieRate = (float)ties / total,
                LossRate = (float)losses / total,
                SimulationCount = total,
            };

            // 获胜伤害范围
            if (playerDamages.Count > 0)
            {
                playerDamages.Sort();
                result.PlayerDamageMin = playerDamages[0];
                result.PlayerDamageMax = playerDamages[playerDamages.Count - 1];
                result.PlayerDamageAvg = (float)playerDamages.Average();
            }

            // 失败伤害范围
            if (opponentDamages.Count > 0)
            {
                opponentDamages.Sort();
                result.OpponentDamageMin = opponentDamages[0];
                result.OpponentDamageMax = opponentDamages[opponentDamages.Count - 1];
                result.OpponentDamageAvg = (float)opponentDamages.Average();
            }

            return result;
        }
    }
}
