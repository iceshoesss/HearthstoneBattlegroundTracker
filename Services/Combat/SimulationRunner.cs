using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HBT.Services.Combat
{

/// <summary>
/// 模拟结果
/// </summary>
public class SimulationResult
{
    public float WinRate;
    public float TieRate;
    public float LossRate;
    public int SimulationCount;

    // 获胜时造成的伤害范围
    public int PlayerDamageMin;
    public int PlayerDamageMax;
    public float PlayerDamageAvg;

    // 失败时受到的伤害范围
    public int OpponentDamageMin;
    public int OpponentDamageMax;
    public float OpponentDamageAvg;
}

/// <summary>
/// 多次模拟聚合器
/// </summary>
public class SimulationRunner
{
    /// <summary>
    /// 运行 N 次模拟，返回聚合结果
    /// </summary>
    public static SimulationResult Run(SimulationInput input, int iterations = 1000, int maxMs = 500)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<int>();
        var simulator = new CombatSimulator();

        for (int i = 0; i < iterations; i++)
        {
            results.Add(simulator.SimulateFight(input));
            if (sw.ElapsedMilliseconds > maxMs)
                break;
        }

        return Aggregate(results);
    }

    private static SimulationResult Aggregate(List<int> results)
    {
        if (results.Count == 0)
            return new SimulationResult();

        int wins = 0, ties = 0, losses = 0;
        var playerDamages = new List<int>();  // 获胜时的伤害（正值）
        var opponentDamages = new List<int>(); // 失败时的伤害（绝对值）

        foreach (var r in results)
        {
            if (r > 0) { wins++; playerDamages.Add(r); }
            else if (r < 0) { losses++; opponentDamages.Add(Math.Abs(r)); }
            else { ties++; }
        }

        int total = results.Count;
        var result = new SimulationResult
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
