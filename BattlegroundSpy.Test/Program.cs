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
}
}
