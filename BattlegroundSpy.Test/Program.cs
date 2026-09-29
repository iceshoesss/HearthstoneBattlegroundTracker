using System;
using System.Collections.Generic;
using BattlegroundSpy;
using BattlegroundSpy.Objects;

namespace BattlegroundSpy.Test
{

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 用法:
        //   dotnet run --project BattlegroundSpy.Test -c Release
        //   dotnet run --project BattlegroundSpy.Test -c Release -- room        # 单次房间探测
        //   dotnet run --project BattlegroundSpy.Test -c Release -- room 3      # 每 3 秒循环
        var mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
        if (mode == "room" || mode == "probe")
        {
            RunRoomProbe(args);
            return;
        }

        Console.WriteLine("=== BattlegroundSpy 对手识别测试 ===\n");

        try
        {
            using var reader = new BattlegroundSpyReader();

            // 1. 场景模式
            var sceneMode = reader.GetSceneMode();
            Console.WriteLine($"场景模式: {sceneMode}\n");

            // 2. MatchInfo（HDT 方案）
            Console.WriteLine("=== MatchInfo ===");
            try
            {
                var matchInfo = reader.GetMatchInfo();
                if (matchInfo != null)
                {
                    Console.WriteLine($"LocalPlayer: Id={matchInfo.LocalPlayer?.Id}, Name={matchInfo.LocalPlayer?.Name}");
                    Console.WriteLine($"OpposingPlayer: Id={matchInfo.OpposingPlayer?.Id}, Name={matchInfo.OpposingPlayer?.Name}");
                }
                else
                {
                    Console.WriteLine("MatchInfo: null");
                }
            }
            catch (Exception ex) { Console.WriteLine($"MatchInfo ERROR: {ex.Message}"); }

            // 3. m_playerMap 详细信息
            Console.WriteLine("\n=== m_playerMap 详细信息 ===");
            try
            {
                reader.DumpPlayerMap();
            }
            catch (Exception ex) { Console.WriteLine($"PlayerMap ERROR: {ex.Message}"); }

            // 4. ZonePlay(2) 对手场面
            Console.WriteLine("\n=== ZonePlay(2) 对手场面 ===");
            try
            {
                var oppBoard = reader.GetOpponentBoardState();
                if (oppBoard != null)
                {
                    Console.WriteLine($"HeroCardId: {oppBoard.HeroCardId}");
                    Console.WriteLine($"ControllerId: {oppBoard.ControllerId}");
                    Console.WriteLine($"Minions: {oppBoard.BoardCards.Count}");
                    foreach (var c in oppBoard.BoardCards)
                        Console.WriteLine($"  [{c.ZonePosition}] {c.CardId} {c.Attack}/{c.Health}" +
                            (c.Golden ? " 金" : "") +
                            (c.Taunt ? " 嘲讽" : "") +
                            (c.DivineShield ? " 圣盾" : ""));
                }
                else
                {
                    Console.WriteLine("对手场面: null");
                }
            }
            catch (Exception ex) { Console.WriteLine($"对手场面 ERROR: {ex.Message}"); }

            // 5. m_entityMap 中的英雄实体
            Console.WriteLine("\n=== m_entityMap 英雄实体 ===");
            try
            {
                reader.DumpHeroEntities();
            }
            catch (Exception ex) { Console.WriteLine($"英雄实体 ERROR: {ex.Message}"); }

            // 5b. ZONE=PLAY 的英雄（关键！）
            Console.WriteLine("\n=== ZONE=PLAY 的英雄 ===");
            try
            {
                reader.DumpPlayZoneHeroes();
            }
            catch (Exception ex) { Console.WriteLine($"ZONE_PLAY ERROR: {ex.Message}"); }

            // 5c. 对手识别测试
            Console.WriteLine("\n=== 对手识别 ===");
            try
            {
                var opponentEntityId = reader.GetOpponentEntityId();
                Console.WriteLine($"对手 EntityId: {opponentEntityId}");
            }
            catch (Exception ex) { Console.WriteLine($"对手识别 ERROR: {ex.Message}"); }

            // 6. 回合数
            Console.WriteLine("\n=== 回合数 ===");
            var turn = reader.GetTurnNumber();
            Console.WriteLine(turn != null ? $"当前回合: {turn}" : "回合数: 未获取到");

            // 7. 悬停信息
            Console.WriteLine("\n=== 悬停信息 ===");
            try
            {
                var hoveredHero = reader.GetLeaderboardHoveredHeroCardId();
                var hoveredEntityId = reader.GetLeaderboardHoveredEntityId();
                Console.WriteLine($"悬停英雄: {hoveredHero ?? "无"}");
                Console.WriteLine($"悬停 EntityId: {hoveredEntityId}");
                reader.DumpHoveredTile();
            }
            catch (Exception ex) { Console.WriteLine($"悬停 ERROR: {ex.Message}"); }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\n按回车键退出...");
        Console.ReadLine();
    }

    /// <summary>
    /// 好友房进人探测：开房后循环 dump，对比人数/字段变化。
    /// </summary>
    /// <summary>
    /// 好友房探测：完整输出写入 room_probe_*.log，控制台只打摘要。
    /// 用法: room 3   （每 3 秒采样；先空房跑几轮，再拉 1 个好友对比）
    /// </summary>
    static void RunRoomProbe(string[] args)
    {
        int intervalSec = 3;
        if (args.Length > 1 && int.TryParse(args[1], out var iv) && iv > 0)
            intervalSec = iv;

        var logPath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            "room_probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

        using (var fs = new System.IO.StreamWriter(logPath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true })
        {
            Console.WriteLine("=== 好友房 / 进房探测 ===");
            Console.WriteLine("日志文件: " + logPath);
            Console.WriteLine("间隔: " + intervalSec + "s。先空房采几轮，再拉 1 个好友，Ctrl+C 结束。");
            fs.WriteLine("# room probe log " + DateTime.Now.ToString("o"));
            fs.WriteLine("# goal: detect who joined friendly room (PartyManager identity)");
            fs.WriteLine("# interval_sec=" + intervalSec);
            fs.WriteLine();

            try
            {
                using var reader = new BattlegroundSpyReader();
                int round = 0;
                while (true)
                {
                    round++;
                    fs.WriteLine("\n########## Round " + round + " " + DateTime.Now.ToString("HH:mm:ss") + " ##########");
                    try
                    {
                        bool full = round == 1;
                        reader.DumpRoomProbe(fs, full);
                    }
                    catch (Exception ex)
                    {
                        fs.WriteLine("probe error: " + ex.Message);
                        Console.WriteLine("probe error: " + ex.Message);
                    }

                    // 控制台一行摘要
                    try
                    {
                        var scene = reader.GetSceneMode();
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] round=" + round + " scene=" + scene + " → 见 log [identity hits]");
                    }
                    catch { }

                    System.Threading.Thread.Sleep(intervalSec * 1000);
                }
            }
            catch (Exception ex)
            {
                fs.WriteLine("ERROR: " + ex);
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine("日志已写: " + logPath);
            }
        }

        Console.WriteLine("日志: " + logPath);
        Console.WriteLine("\n按回车键退出...");
        Console.ReadLine();
    }
}
}
