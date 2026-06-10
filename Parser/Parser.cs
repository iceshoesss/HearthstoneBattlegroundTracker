using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HBT
{

/// <summary>
/// 纯函数解析器 — 逐行解析 Power.log。
/// 无内部可变状态，所有状态由外部通过 GameState 传入传出。
/// </summary>
public static class Parser
{
    // 实体追踪：当前正在处理的 FULL_ENTITY ID
    private static int _currentTrackingEntityId;

    // ── 英雄卡牌过滤 ──────────────────────────────────

    private static readonly string[] HeroPrefixes =
    {
        "TB_BaconShop_HERO_",
        "BG20_HERO_", "BG21_HERO_", "BG22_HERO_", "BG23_HERO_",
        "BG24_HERO_", "BG25_HERO_", "BG26_HERO_", "BG27_HERO_",
        "BG28_HERO_", "BG29_HERO_", "BG30_HERO_", "BG31_HERO_",
        "BG32_HERO_", "BG33_HERO_", "BG34_HERO_", "BG35_HERO_",
    };

    private static readonly HashSet<string> HeroExclude = new HashSet<string>()
    {
        "TB_BaconShop_HERO_PH"
    };

    public static bool IsHeroCard(string cardId)
    {
        if (HeroExclude.Contains(cardId)) return false;
        // 构筑模式职业英雄: HERO_01 ~ HERO_99
        if (Regex.IsMatch(cardId, @"^HERO_\d{2}"))
            return true;
        foreach (var prefix in HeroPrefixes)
        {
            if (!cardId.StartsWith(prefix)) continue;
            var suffix = cardId.Substring(prefix.Length);
            return int.TryParse(suffix, out _);
        }
        return false;
    }

    /// <summary>判断是否为构筑模式的对战类型</summary>
    public static bool IsConstructedGameType(string gameType)
    {
        return gameType == "GT_RANKED"
            || gameType == "GT_STANDARD" || gameType == "GT_WILD"
            || gameType == "GT_VS_FRIEND";
    }

    /// <summary>GameType + FormatType → mode 映射</summary>
    public static string GameTypeToMode(string gameType, string formatType)
    {
        // BG 模式：前缀匹配，覆盖好友房
        if (gameType.StartsWith("GT_BATTLEGROUNDS"))
            return "BG";

        // 构筑模式：用 FormatType 区分标准/狂野
        if (formatType == "FT_WILD")
            return "WILD";

        switch (gameType)
        {
            case "GT_STANDARD": return "STD";
            case "GT_WILD": return "WILD";
            case "GT_RANKED": return "STD";
            case "GT_VS_FRIEND": return "STD";
            default: return "";
        }
    }

    // ── 正则表达式 ────────────────────────────────────

    private static readonly Regex ReCreateGame =
        new Regex(@"GameState\.DebugPrintPower\(\) - CREATE_GAME$", RegexOptions.Compiled);

    private static readonly Regex ReGameType =
        new Regex(@"GameType=(\w+)", RegexOptions.Compiled);

    private static readonly Regex ReFormatType =
        new Regex(@"FormatType=(\w+)", RegexOptions.Compiled);

    private static readonly Regex ReGameSeed =
        new Regex(@"tag=GAME_SEED value=(\d+)", RegexOptions.Compiled);

    private static readonly Regex RePlayerName =
        new Regex(@"PlayerID=(\d+),\s*PlayerName=(.+?)$", RegexOptions.Compiled);

    private static readonly Regex ReAccountId =
        new Regex(@"GameAccountId=\[hi=\d+ lo=(\d+)\]", RegexOptions.Compiled);

    private static readonly Regex ReHeroEntity =
        new Regex(@"TAG_CHANGE Entity=(.+?) tag=HERO_ENTITY value=(\d+)", RegexOptions.Compiled);

    public static readonly Regex ReFullEntity =
        new Regex(@"FULL_ENTITY - (?:Creating|Updating)\s+\[?entityName=(.+?)\s+id=(\d+)\s+zone=\w+(?:\s+zonePos=\d+)?"
            + @".*?cardId=(\w+).*?player=(\d+)\]?", RegexOptions.Compiled);

    private static readonly Regex ReLbEntity =
        new Regex(@"TAG_CHANGE Entity=\[entityName=(.+?) id=(\d+) zone=\w+(?:\s+zonePos=\d+)?"
            + @".*?cardId=(\w+).*?player=(\d+)\]\s+tag=PLAYER_LEADERBOARD_PLACE value=(\d+)",
            RegexOptions.Compiled);

    private static readonly Regex ReLbTag =
        new Regex(@"TAG_CHANGE Entity=(.+?) tag=PLAYER_LEADERBOARD_PLACE value=(\d+)\s*$", RegexOptions.Compiled);

    private static readonly Regex ReGraveyard =
        new Regex(@"TAG_CHANGE Entity=\[entityName=.+? id=\d+ zone=\w+(?:\s+zonePos=\d+)?"
            + @".*?cardId=(\w+).*?player=(\d+)\]\s+tag=ZONE value=GRAVEYARD",
            RegexOptions.Compiled);

    private static readonly Regex ReStep =
        new Regex(@"TAG_CHANGE Entity=GameEntity tag=STEP value=(\w+)", RegexOptions.Compiled);

    private static readonly Regex ReConcedePlayerTag =
        new Regex(@"TAG_CHANGE Entity=.+? tag=(3479|4356) value=1", RegexOptions.Compiled);

    private static readonly Regex ReConcedeGameTag =
        new Regex(@"TAG_CHANGE Entity=GameEntity tag=4302 value=1", RegexOptions.Compiled);

    private static readonly Regex ReGameStateComplete =
        new Regex(@"TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE", RegexOptions.Compiled);

    // BG 战斗阶段开始：GameTag 2022 从 1 变为 0
    private static readonly Regex ReBgsCombatStart =
        new Regex(@"TAG_CHANGE Entity=GameEntity tag=2022 value=0", RegexOptions.Compiled);

    // BG 阵容快照时机：GameTag 3533 从 1 变为 0（战斗开始前，对齐 HDT SnapshotCurrentBoard）
    private static readonly Regex ReBgsBoardSnapshot =
        new Regex(@"TAG_CHANGE Entity=GameEntity tag=3533 value=0", RegexOptions.Compiled);

    // 构筑模式：PLAYSTATE
    private static readonly Regex RePlayState =
        new Regex(@"TAG_CHANGE Entity=(.+?) tag=PLAYSTATE value=(\w+)", RegexOptions.Compiled);

    // 实体追踪：FULL_ENTITY 解析（简化格式：ID=123 CardID=ABC）
    private static readonly Regex ReFullEntitySimple =
        new Regex(@"FULL_ENTITY - (?:Creating|Updating)\s+ID=(\d+)\s+CardID=(\w*)", RegexOptions.Compiled);

    // 实体追踪：TAG_CHANGE 解析（带实体 ID）
    private static readonly Regex ReTagChangeWithId =
        new Regex(@"TAG_CHANGE Entity=\[.*?id=(\d+).*?\]\s+tag=(\w+)\s+value=(\w+)", RegexOptions.Compiled);

    // 实体追踪：TAG_CHANGE 解析（简化格式，tag 名称）
    private static readonly Regex ReTagChangeSimple =
        new Regex(@"TAG_CHANGE Entity=(\d+)\s+tag=(\w+)\s+value=(\w+)", RegexOptions.Compiled);

    // 实体追踪：FULL_ENTITY 块内的 tag= 行
    private static readonly Regex ReTagInFullEntity =
        new Regex(@"tag=(\w+)\s+value=(\w+)", RegexOptions.Compiled);

    // ── 核心方法 ──────────────────────────────────────

    /// <summary>
    /// 处理一行日志，返回 (事件, 新状态)。
    /// game / games 由外部持有，Parser 只负责读取和修改。
    /// 不调 HearthMirror —— 如需 HM 数据，Parser 发出事件，由 MainForm 取。
    /// </summary>
    public static (GameEvent evt, GameState state, Game game) ProcessLine(
        string line, Game game, GameState state, List<Game> games, EntityTracker entityTracker = null)
    {
        // ── CREATE_GAME ──
        // Power.log 中有两次 CREATE_GAME（GameState + PowerTaskList），
        // 第二次会重复触发。如果已在块内，跳过。
        if (ReCreateGame.IsMatch(line) && !state.InCreateBlock)
        {
            var pendingNewGame = game.IsActive ? game : null;
            var newGame = NewGame();
            state = GameState.CreateInitial();
            state.InCreateBlock = true;
            state.PendingNewGame = pendingNewGame;
            return (null, state, newGame);
        }

        // ── CREATE_GAME 块内 ──
        if (state.InCreateBlock)
        {
            if (line.Contains("tag=TURN value="))
            {
                state.CreateHasTurn = true;
                if (state.PendingNewGame != null)
                {
                    if (games.Count > 0 && games[games.Count - 1] == game)
                        games.RemoveAt(games.Count - 1);
                    game = state.PendingNewGame;
                    game.Reconnected = true;
                    game.ReconnectTimes.Add(DateTime.UtcNow.ToString("o"));
                    state.PendingNewGame = null;
                }
                return (null, state, game);
            }

            if (line.Contains("GameAccountId="))
            {
                var m = ReAccountId.Match(line);
                if (m.Success)
                {
                    var lo = ulong.Parse(m.Groups[1].Value);
                    if (lo != 0)
                    {
                        if (state.AccountIdLo == 0)
                            state.AccountIdLo = lo;
                        else if (state.OpponentAccountIdLo == 0)
                            state.OpponentAccountIdLo = lo;
                    }
                }
            }

            if (line.Contains("GAME_SEED"))
            {
                var m = ReGameSeed.Match(line);
                if (m.Success)
                    game.GameSeed = long.Parse(m.Groups[1].Value);
            }

            if (line.Contains("PowerTaskList.DebugDump()"))
            {
                state.InCreateBlock = false;
                if (!state.CreateHasTurn && state.PendingNewGame != null)
                {
                    var old = state.PendingNewGame;
                    old.IsActive = false;
                    old.EndTime = DateTime.Now.ToString("HH:mm:ss");
                    games.Add(old);
                    state.PendingNewGame = null;
                }
                // GameStartEvent 延迟到 GameType 确认后发出
                return (state.CreateHasTurn
                    ? (GameEvent)new ReconnectEvent()
                    : null,
                    state, game);
            }

            if (line.Contains("PowerTaskList.") && !line.Contains("PowerTaskList.DebugPrintPower()"))
                return (null, state, game);
        }

        // ── 只处理 GameState + PowerTaskList ──
        if (!line.Contains("GameState.") && !line.Contains("PowerTaskList.DebugPrintPower()"))
            return (null, state, game);

        if (!game.IsActive)
            return (null, state, game);

        if (line.Contains("PowerTaskList."))
            return (HandlePowerTaskList(line, game), state, game);

        return HandleGameState(line, game, state, games, entityTracker);
    }

    // ── GameState 处理 ────────────────────────────────

    private static (GameEvent evt, GameState state, Game game) HandleGameState(
        string line, Game game, GameState state, List<Game> games, EntityTracker entityTracker = null)
    {
        // FormatType（在 DebugPrintGame 块中，GameType 之后出现）
        var fm = ReFormatType.Match(line);
        if (fm.Success && line.Contains("DebugPrintGame()"))
        {
            state.FormatType = fm.Groups[1].Value;
            // FormatType 可能晚于 GameType 出现，更新 mode
            if (state.FormatType == "FT_WILD" && state.Mode == "STD")
                state.Mode = "WILD";
            // FT_STANDARD：Mode 已由 GameType 处理设好，无需更新
        }

        // GameType — 确认游戏类型后发出 GameStartEvent
        var m = ReGameType.Match(line);
        if (m.Success && line.Contains("DebugPrintGame()"))
        {
            var gt = m.Groups[1].Value;
            state.Mode = GameTypeToMode(gt, state.FormatType);

            // 重连：只更新 mode/isConstructed，不发 GameStartEvent（避免重置联赛状态）
            if (game.Reconnected)
            {
                if (IsConstructedGameType(gt))
                    state.IsConstructed = true;
                return (null, state, game);
            }

            if (IsConstructedGameType(gt))
            {
                state.IsConstructed = true;
                return (new ConstructedGameStartEvent
                {
                    GameSeed = game.GameSeed,
                    AccountIdLo = game.AccountIdLo,
                }, state, game);
            }
            if (gt.StartsWith("GT_BATTLEGROUNDS"))
            {
                return (new GameStartEvent
                {
                    GameSeed = game.GameSeed,
                    AccountIdLo = game.AccountIdLo,
                }, state, game);
            }
            EndGame(game, games);
            return (new NotBgEvent(), state, game);
        }

        // PlayerName
        m = RePlayerName.Match(line);
        if (m.Success && line.Contains("DebugPrintGame()"))
        {
            var name = m.Groups[2].Value.Trim();
            if (name == "古怪之德鲁伊" || name == "惊魂之武僧")
                return (null, state, game);

            var playerId = int.Parse(m.Groups[1].Value);

            if (string.IsNullOrEmpty(game.PlayerTag) && name.Contains("#"))
            {
                // 带 #number 的是本地玩家
                game.LocalPlayerId = playerId;
                game.PlayerTag = name;
                game.PlayerDisplayName = name.Substring(0, name.LastIndexOf("#"));
                return (new PlayerInfoEvent
                {
                    PlayerTag = game.PlayerTag,
                    PlayerDisplayName = game.PlayerDisplayName,
                }, state, game);
            }

            // 构筑模式：对手名（非 UNKNOWN、非本地名）
            if (state.IsConstructed && !string.IsNullOrEmpty(game.PlayerTag)
                && name != "UNKNOWN HUMAN PLAYER"
                && name != game.PlayerTag
                && name != game.PlayerDisplayName)
            {
                game.OpponentPlayerTag = name;
                game.OpponentDisplayName = name.Contains("#")
                    ? name.Substring(0, name.LastIndexOf("#")) : name;
                if (state.AccountIdLo != 0 && state.OpponentAccountIdLo != 0)
                    return (MakeConstructedCheckLeague(game, state), state, game);
            }
        }

        // HERO_ENTITY
        m = ReHeroEntity.Match(line);
        if (m.Success)
        {
            var entityName = m.Groups[1].Value.Trim();
            var heroEntityId = int.Parse(m.Groups[2].Value);
            if (entityName == game.PlayerTag)
            {
                game.HeroEntityId = heroEntityId;
                if (state.IsConstructed)
                {
                    var hero = FindHeroByEntity(game, heroEntityId);
                    if (hero != null)
                    {
                        game.HeroName = hero.HeroName;
                        game.HeroCardId = hero.CardId;
                        return (new HeroEntityEvent
                        {
                            HeroName = game.HeroName,
                            HeroCardId = game.HeroCardId,
                        }, state, game);
                    }
                }
                return (null, state, game);
            }
        }

        // FULL_ENTITY（GameState 中的）
        m = ReFullEntity.Match(line);
        if (m.Success)
        {
            var heroName = m.Groups[1].Value;
            var entityId = int.Parse(m.Groups[2].Value);
            var cardId = m.Groups[3].Value;
            var playerSlot = int.Parse(m.Groups[4].Value);

            if (IsHeroCard(cardId))
            {
                var key = (cardId, playerSlot);
                var isNew = !game.AllHeroes.ContainsKey(key);
                if (isNew)
                {
                    game.AllHeroes[key] = new GameHero
                    {
                        EntityId = entityId,
                        HeroName = heroName,
                        CardId = cardId,
                        PlayerSlot = playerSlot,
                    };
                }

                if (state.IsConstructed && string.IsNullOrEmpty(game.HeroName)
                    && (entityId == game.HeroEntityId
                        || (game.HeroEntityId == 0
                            && (playerSlot == 1
                                || heroName == game.PlayerTag
                                || heroName == game.PlayerDisplayName))))
                {
                    var h = game.AllHeroes[key];
                    game.HeroEntityId = entityId;
                    game.HeroName = h.HeroName;
                    game.HeroCardId = h.CardId;
                    return (new HeroEntityEvent
                    {
                        HeroName = game.HeroName,
                        HeroCardId = game.HeroCardId,
                    }, state, game);
                }

                // BG 模式：匹配 HERO_ENTITY 设置的 HeroEntityId
                if (!state.IsConstructed && string.IsNullOrEmpty(game.HeroName)
                    && game.HeroEntityId > 0 && entityId == game.HeroEntityId)
                {
                    var h = game.AllHeroes[key];
                    game.HeroName = h.HeroName;
                    game.HeroCardId = h.CardId;
                    return (new HeroEntityEvent
                    {
                        HeroName = game.HeroName,
                        HeroCardId = game.HeroCardId,
                    }, state, game);
                }

                // 构筑模式：追踪对手英雄
                if (string.IsNullOrEmpty(game.OpponentHeroCardId)
                    && game.LocalPlayerId > 0 && playerSlot != game.LocalPlayerId)
                {
                    game.OpponentHeroCardId = cardId;
                    game.OpponentHeroName = heroName;
                }
            }
            return (null, state, game);
        }

        // STEP
        m = ReStep.Match(line);
        if (m.Success)
        {
            var step = m.Groups[1].Value;
            if (step == "MAIN_START" || step == "MAIN_CLEANUP")
            {
                if (game.HeroEntityId > 0)
                {
                    var hero = FindHeroByEntity(game, game.HeroEntityId);
                    if (hero != null)
                    {
                        game.HeroName = hero.HeroName;
                        game.HeroCardId = hero.CardId;
                    }
                }
            }
            if (step == "MAIN_CLEANUP")
            {
                state.ReachedStep13 = true;
                // 构筑模式不依赖 STEP 13，在对手就绪时已触发 check-league
                if (!state.IsConstructed && !state.LoFetched && !state.IsScanning)
                {
                    state.LoFetched = true;
                    return (new CheckLeagueEvent
                    {
                        PlayerTag = game.PlayerTag,
                        AccountIdLo = game.AccountIdLo,
                    }, state, game);
                }
            }
            return (null, state, game);
        }

        // LEADERBOARD_PLACE
        m = ReLbEntity.Match(line);
        if (m.Success)
        {
            var entityId = int.Parse(m.Groups[2].Value);
            var cardId = m.Groups[3].Value;
            var playerSlot = int.Parse(m.Groups[4].Value);
            var placement = int.Parse(m.Groups[5].Value);

            var heroKey = (cardId, playerSlot);
            if (game.AllHeroes.ContainsKey(heroKey))
                game.AllHeroes[heroKey].Placement = placement;

            var matched = false;
            if (!string.IsNullOrEmpty(game.HeroCardId) && cardId == game.HeroCardId)
                matched = true;
            else if (game.HeroEntityId > 0 && entityId == game.HeroEntityId)
                matched = true;
            else if (game.LocalPlayerId > 0 && playerSlot == game.LocalPlayerId)
                matched = true;

            if (matched)
            {
                game.HeroPlacement = placement;
            }

            if (state.ConcedePending && placement == 8)
            {
                game.Conceded = true;
                game.PlacementConfirmed = true;
                game.HeroPlacement = 8;
                foreach (var h in game.AllHeroes.Values)
                {
                    if (h.Placement == 0)
                        h.Placement = 8;
                }
                EndGame(game, games);
                return (new ConcedeEvent { Placement = 8 }, state, game);
            }
            return (null, state, game);
        }

        // LEADERBOARD_PLACE 简写
        m = ReLbTag.Match(line);
        if (m.Success)
        {
            var tag = m.Groups[1].Value.Trim();
            var placement = int.Parse(m.Groups[2].Value);

            if (tag == game.PlayerTag)
            {
                game.HeroPlacement = placement;
            }
            else if (!state.IsConstructed && placement > 0 && game.HeroPlacement == 0)
            {
                Console.WriteLine($"[Parser] ⚠️ LB简写不匹配: tag=[{tag}], PlayerTag=[{game.PlayerTag}], placement={placement}");
            }

            if (state.ConcedePending && placement == 8)
            {
                game.Conceded = true;
                game.PlacementConfirmed = true;
                game.HeroPlacement = 8;
                foreach (var h in game.AllHeroes.Values)
                {
                    if (h.Placement == 0)
                        h.Placement = 8;
                }
                EndGame(game, games);
                return (new ConcedeEvent { Placement = 8 }, state, game);
            }
            return (null, state, game);
        }

        // ZONE=GRAVEYARD — 英雄被淘汰，标记为已淘汰
        m = ReGraveyard.Match(line);
        if (m.Success)
        {
            var cardId = m.Groups[1].Value;
            var playerSlot = int.Parse(m.Groups[2].Value);
            var key = (cardId, playerSlot);
            if (game.AllHeroes.TryGetValue(key, out var hero))
                hero.Eliminated = true;
            // 不要 return — 让后面的实体追踪代码更新 ZONE=GRAVEYARD
        }

        // 投降信号前兆
        if (ReConcedePlayerTag.IsMatch(line))
        {
            var tagMatch = Regex.Match(line, @"Entity=(.+?) tag=(?:3479|4356)");
            if (tagMatch.Success)
                state.ConcedeTag = tagMatch.Groups[1].Value.Trim();
            state.ConcedePending = true;
            return (null, state, game);
        }

        if (state.ConcedePending && ReConcedeGameTag.IsMatch(line))
            return (null, state, game);

        // BG 战斗阶段开始：GameTag 2022 从 1 变为 0
        if (!state.IsConstructed && ReBgsCombatStart.IsMatch(line))
        {
            return (new CombatStartEvent(), state, game);
        }

        // BG 阵容快照：GameTag 3533 从 1 变为 0（对齐 HDT SnapshotCurrentBoard 时机）
        if (!state.IsConstructed && ReBgsBoardSnapshot.IsMatch(line))
        {
            return (new BoardSnapshotEvent(), state, game);
        }

        // 构筑模式：PLAYSTATE 检测
        m = RePlayState.Match(line);
        if (m.Success && state.IsConstructed)
        {
            var entityName = m.Groups[1].Value.Trim();
            var playState = m.Groups[2].Value;

            // 对战模式和好友模式中，实体名可能是完整 BattleTag
            if (entityName.Contains("#"))
            {
                // 对手名解析（从 UNKNOWN HUMAN PLAYER → BattleTag）
                if (string.IsNullOrEmpty(game.OpponentPlayerTag)
                    && entityName != game.PlayerTag)
                {
                    game.OpponentPlayerTag = entityName;
                    game.OpponentDisplayName = entityName.Substring(0, entityName.LastIndexOf("#"));
                    if (state.AccountIdLo != 0 && state.OpponentAccountIdLo != 0)
                        return (MakeConstructedCheckLeague(game, state), state, game);
                }
            }

            // 本机结果
            if (entityName == game.PlayerTag)
            {
                if (playState == "LOST" || playState == "CONCEDED")
                {
                    game.PlayState = playState;
                    return (null, state, game);
                }
                if (playState == "WON")
                {
                    game.PlayState = "WON";
                }
                if (playState == "DRAW")
                {
                    game.PlayState = "DRAW";
                }
            }
        }

        // 构筑模式：从任意 TAG_CHANGE Entity 名称检测对手 BattleTag
        if (state.IsConstructed && string.IsNullOrEmpty(game.OpponentPlayerTag))
        {
            var entityMatch = Regex.Match(line, @"Entity=(.+?) tag=");
            if (entityMatch.Success)
            {
                var entityName = entityMatch.Groups[1].Value.Trim();
                if (entityName.Contains("#")
                    && entityName != game.PlayerTag
                    && !entityName.StartsWith("UNKNOWN"))
                {
                    game.OpponentPlayerTag = entityName;
                    game.OpponentDisplayName = entityName.Substring(0, entityName.LastIndexOf("#"));
                    if (state.AccountIdLo != 0 && state.OpponentAccountIdLo != 0)
                        return (MakeConstructedCheckLeague(game, state), state, game);
                }
            }
        }

        // 游戏结束
        if (ReGameStateComplete.IsMatch(line))
        {
            state.ConcedePending = false;
            if (!game.Conceded && game.HeroPlacement > 0)
                game.PlacementConfirmed = true;
            EndGame(game, games);

            // 诊断：如果 HeroPlacement 为 0，输出调试信息
            if (!state.IsConstructed && game.HeroPlacement == 0)
            {
                Console.WriteLine($"[Parser] ⚠️ HeroPlacement=0, HeroCardId={game.HeroCardId}, HeroEntityId={game.HeroEntityId}, PlayerTag={game.PlayerTag}");
                Console.WriteLine($"[Parser] AllHeroes: {game.AllHeroes.Count}");
                foreach (var h in game.AllHeroes)
                    Console.WriteLine($"  {h.Key}: EntityId={h.Value.EntityId}, CardId={h.Value.CardId}, Placement={h.Value.Placement}");
            }

            var endEvt = state.IsConstructed
                ? new GameEndEvent { IsConstructed = true, PlayState = !string.IsNullOrEmpty(game.PlayState) ? game.PlayState : (game.Conceded ? "CONCEDED" : "COMPLETE") }
                : new GameEndEvent { Placement = game.HeroPlacement };
            return (endEvt, state, game);
        }

        // ── 实体追踪 ──
        if (entityTracker != null)
        {
            // FULL_ENTITY - Creating/Updating: 创建或更新实体
            if (line.Contains("FULL_ENTITY"))
            {
                var mFull = ReFullEntitySimple.Match(line);
                if (mFull.Success)
                {
                    var entityId = int.Parse(mFull.Groups[1].Value);
                    var cardId = mFull.Groups[2].Value;
                    entityTracker.SetCardId(entityId, cardId);
                    _currentTrackingEntityId = entityId;
                }
                else
                {
                    _currentTrackingEntityId = 0;
                }
            }
            // FULL_ENTITY 块内的 tag= 行
            else if (_currentTrackingEntityId > 0 && line.Contains("tag="))
            {
                var mTag = ReTagInFullEntity.Match(line);
                if (mTag.Success)
                {
                    var tagName = mTag.Groups[1].Value;
                    var rawValue = mTag.Groups[2].Value;

                    int tagId = TagNameToId(tagName);
                    if (tagId > 0)
                    {
                        int value;
                        if (!int.TryParse(rawValue, out value))
                            value = TagValueToNumber(tagId, rawValue);

                        entityTracker.UpdateTag(_currentTrackingEntityId, tagId, value);
                    }
                }
            }
            // TAG_CHANGE with entity ID in brackets
            else if (line.Contains("TAG_CHANGE"))
            {
                _currentTrackingEntityId = 0;

                var mTag = ReTagChangeWithId.Match(line);
                if (!mTag.Success)
                    mTag = ReTagChangeSimple.Match(line);

                if (mTag.Success)
                {
                    var entityId = int.Parse(mTag.Groups[1].Value);
                    var tagName = mTag.Groups[2].Value;
                    var rawValue = mTag.Groups[3].Value;

                    int tagId = TagNameToId(tagName);
                    if (tagId > 0)
                    {
                        int value;
                        if (!int.TryParse(rawValue, out value))
                            value = TagValueToNumber(tagId, rawValue);

                        entityTracker.UpdateTag(entityId, tagId, value);
                    }
                }
            }
            else
            {
                _currentTrackingEntityId = 0;
            }
        }

        return (null, state, game);
    }

    // ── PowerTaskList 处理 ────────────────────────────

    private static GameEvent HandlePowerTaskList(string line, Game game)
    {
        var m = ReFullEntity.Match(line);
        if (!m.Success) return null;

        var heroName = m.Groups[1].Value;
        var entityId = int.Parse(m.Groups[2].Value);
        var cardId = m.Groups[3].Value;
        var playerSlot = int.Parse(m.Groups[4].Value);

        if (!IsHeroCard(cardId)) return null;

        var key = (cardId, playerSlot);
        if (game.AllHeroes.ContainsKey(key)) return null;

        game.AllHeroes[key] = new GameHero
        {
            EntityId = entityId,
            HeroName = heroName,
            CardId = cardId,
            PlayerSlot = playerSlot,
        };

        if (entityId == game.HeroEntityId)
        {
            game.HeroEntityId = entityId;
            game.HeroName = heroName;
            game.HeroCardId = cardId;
            return new HeroEntityEvent
            {
                HeroName = heroName,
                HeroCardId = cardId,
            };
        }

        // 构筑模式：追踪对手英雄
        if (game.LocalPlayerId > 0 && playerSlot != game.LocalPlayerId
            && string.IsNullOrEmpty(game.OpponentHeroCardId))
        {
            game.OpponentHeroCardId = cardId;
            game.OpponentHeroName = heroName;
        }

        return null;
    }

    // ── 辅助方法 ──────────────────────────────────────

    public static Game NewGame()
    {
        return new Game
        {
            IsActive = true,
            StartTime = DateTime.Now.ToString("HH:mm:ss"),
        };
    }

    public static void EndGame(Game game, List<Game> games)
    {
        if (!game.IsActive) return;
        game.IsActive = false;
        game.EndTime = DateTime.Now.ToString("HH:mm:ss");
        games.Add(game);
    }

    private static GameHero FindHeroByEntity(Game game, int entityId)
    {
        foreach (var hero in game.AllHeroes.Values)
        {
            if (hero.EntityId == entityId)
                return hero;
        }
        return null;
    }

    private static ConstructedCheckLeagueEvent MakeConstructedCheckLeague(Game game, GameState state)
    {
        return new ConstructedCheckLeagueEvent
        {
            PlayerTag = game.PlayerTag,
            AccountIdLo = state.AccountIdLo,
            OpponentAccountIdLo = state.OpponentAccountIdLo,
            OpponentPlayerTag = game.OpponentPlayerTag,
            OpponentDisplayName = game.OpponentDisplayName,
        };
    }

    /// <summary>标签名称转数字 ID（常用标签）</summary>
    private static int TagNameToId(string tagName)
    {
        switch (tagName)
        {
            case "ENTITY_ID": return 53;
            case "CONTROLLER": return 50;
            case "ZONE": return 49;
            case "CARDTYPE": return 202;
            case "ATK": return 47;
            case "HEALTH": return 45;
            case "DAMAGE": return 197;
            case "PREMIUM": return 364;
            case "TAUNT": return 190;
            case "DIVINE_SHIELD": return 194;
            case "POISONOUS": return 363;
            case "VENOMOUS": return 2853;
            case "WINDFURY": return 189;
            case "REBORN": return 1085;
            case "STEALTH": return 191;
            case "DEATHRATTLE": return 217;
            case "ZONE_POSITION": return 263;
            case "TECH_LEVEL": return 1440;
            case "PLAYER_ID": return 2;
            case "PLAYER_LEADERBOARD_PLACE": return 199;
            case "HERO_ENTITY": return 273;
            case "STEP": return 198;
            case "STATE": return 196;
            case "TURN": return 20;
            case "PLAYSTATE": return 17;
            // 数字标签直接解析
            default:
                if (int.TryParse(tagName, out int id))
                    return id;
                return 0;
        }
    }

    /// <summary>标签字符串值转数字（ZONE、CARDTYPE 等枚举）</summary>
    private static int TagValueToNumber(int tagId, string value)
    {
        switch (tagId)
        {
            case 49: // ZONE
                switch (value)
                {
                    case "PLAY": return 1;
                    case "SETASIDE": return 2;
                    case "HAND": return 3;
                    case "GRAVEYARD": return 4;
                    case "SECRET": return 5;
                    case "REMOVEDFROMGAME": return 6;
                    case "COSMETIC": return 7;
                    default: return 0;
                }
            case 202: // CARDTYPE
                switch (value)
                {
                    case "GAME": return 1;
                    case "PLAYER": return 2;
                    case "HERO": return 3;
                    case "MINION": return 4;
                    case "SPELL": return 5;
                    case "ENCHANTMENT": return 6;
                    case "WEAPON": return 7;
                    case "HERO_POWER": return 10;
                    case "BATTLEGROUND_SPELL": return 42;
                    case "BATTLEGROUND_TRINKET": return 43;
                    case "BATTLEGROUND_ANOMALY": return 44;
                    case "BATTLEGROUND_QUEST_REWARD": return 45;
                    default: return 0;
                }
            case 198: // STEP
                switch (value)
                {
                    case "INVALID": return 0;
                    case "BEGIN_FIRST": return 1;
                    case "BEGIN_SHUFFLE": return 2;
                    case "BEGIN_DRAW": return 3;
                    case "BEGIN_MULLIGAN": return 4;
                    case "MAIN_BEGIN": return 5;
                    case "MAIN_READY": return 6;
                    case "MAIN_RESOURCE": return 7;
                    case "MAIN_DRAW": return 8;
                    case "MAIN_START": return 9;
                    case "MAIN_ACTION": return 10;
                    case "MAIN_COMBAT": return 11;
                    case "MAIN_END": return 12;
                    case "MAIN_CLEANUP": return 13;
                    case "MAIN_NEXT": return 14;
                    case "FINAL_WRAPUP": return 15;
                    case "FINAL_GAMEOVER": return 16;
                    case "MAIN_START_TRIGGERS": return 17;
                    default: return 0;
                }
            case 196: // STATE
                switch (value)
                {
                    case "INVALID": return 0;
                    case "LOADING": return 1;
                    case "RUNNING": return 2;
                    case "COMPLETE": return 3;
                    default: return 0;
                }
            case 17: // PLAYSTATE
                switch (value)
                {
                    case "INVALID": return 0;
                    case "PLAYING": return 1;
                    case "WINNING": return 2;
                    case "LOSING": return 3;
                    case "WON": return 4;
                    case "LOST": return 5;
                    case "TIED": return 6;
                    case "DISCONNECTED": return 7;
                    case "CONCEDED": return 8;
                    default: return 0;
                }
            default:
                return 0;
        }
    }
}
}
