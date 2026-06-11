using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattlegroundSpy.Objects;
using HackF5.UnitySpy;
using HackF5.UnitySpy.HearthstoneLib.Detail;

namespace BattlegroundSpy
{

/// <summary>
/// Main entry point for reading Hearthstone Battlegrounds data from process memory.
/// Uses UnitySpy (MIT) for Mono runtime navigation.
/// </summary>
public sealed class BattlegroundSpyReader : IDisposable
{
    private IAssemblyImage _image;
    private HearthstoneImage _hsImage;
    private bool _disposed;

    public BattlegroundSpyReader(int processId = 0)
    {
        if (processId == 0)
        {
            var hsProcess = FindHearthstoneProcess();
            if (hsProcess == null)
                throw new InvalidOperationException("Hearthstone process not found");
            processId = hsProcess.Id;
        }

        try
        {
            _image = AssemblyImageFactory.Create(processId, msg => Console.WriteLine($"[UnitySpy] {msg}"));
            _hsImage = new HearthstoneImage(_image);
            Console.WriteLine("[BGSpy] Initialized successfully via UnitySpy");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] Init failed: {ex}");
            throw new InvalidOperationException($"Failed to initialize UnitySpy: {ex.Message}", ex);
        }
    }

    private static Process FindHearthstoneProcess()
    {
        var processes = Process.GetProcessesByName("Hearthstone");
        return processes.Length > 0 ? processes[0] : null;
    }

    // ═══════════════════════════════════════
    //  BG Methods (HM compatible)
    // ═══════════════════════════════════════

    public BattleTag GetBattleTag()
    {
        try
        {
            var service = _image?["BnetPresenceMgr"]?["s_instance"];
            if (service == null) return null;

            dynamic battleTag = service["m_myPlayer"]?["m_account"]?["m_battleTag"];
            if (battleTag == null) return null;

            return new BattleTag
            {
                Name = (string)battleTag["m_name"] ?? "",
                Number = (string)battleTag["m_number"] ?? ""
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetBattleTag failed: {ex.Message}");
            return null;
        }
    }

    public AccountId GetAccountId()
    {
        try
        {
            var service = _image?["BnetPresenceMgr"]?["s_instance"];
            if (service == null) return null;

            dynamic entityId = service["m_myGameAccountId"]?["<EntityId>k__BackingField"];
            if (entityId == null) return null;

            return new AccountId
            {
                Hi = Convert.ToUInt64(entityId["high_"]),
                Lo = Convert.ToUInt64(entityId["low_"])
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetAccountId failed: {ex.Message}");
            return null;
        }
    }

    public MatchInfo GetMatchInfo()
    {
        try
        {
            if (_hsImage == null) return null;
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            var matchInfo = new MatchInfo();

            dynamic playerMap = gameState["m_playerMap"];
            if (playerMap != null)
            {
                try
                {
                    dynamic keySlots = playerMap["keySlots"];
                    dynamic valueSlots = playerMap["valueSlots"];
                    if (keySlots != null && valueSlots != null)
                    {
                        int count = GetCollectionSize(keySlots);
                        for (int i = 0; i < count; i++)
                        {
                            dynamic player = valueSlots[i];
                            if (player == null) continue;

                            int side = 0;
                            string name = "";
                            try { side = (int)(player["m_side"] ?? 0); } catch { }
                            try { name = (string)(player["m_name"] ?? ""); } catch { }

                            // Key 就是玩家 ID（不是 m_id）
                            int playerId = (int)(keySlots[i] ?? 0);

                            var playerInfo = new MatchInfo.PlayerInfo
                            {
                                Name = name,
                                Id = playerId,  // 用 Key 作为 Id
                            };

                            // m_side: 1 = FRIENDLY (local), 2 = OPPOSING
                            if (side == 1 && matchInfo.LocalPlayer == null)
                                matchInfo.LocalPlayer = playerInfo;
                            else if (side == 2 && matchInfo.OpposingPlayer == null)
                                matchInfo.OpposingPlayer = playerInfo;
                        }
                    }
                }
                catch { }
            }

            return matchInfo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetMatchInfo failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>诊断：输出 m_playerMap 详细信息</summary>
    public void DumpPlayerMap()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) { Console.WriteLine("[Dump] gameState null"); return; }

            dynamic playerMap = gameState["m_playerMap"];
            if (playerMap == null) { Console.WriteLine("[Dump] playerMap null"); return; }

            dynamic keySlots = playerMap["keySlots"];
            dynamic valueSlots = playerMap["valueSlots"];
            if (keySlots == null || valueSlots == null) { Console.WriteLine("[Dump] slots null"); return; }

            int count = GetCollectionSize(keySlots);
            Console.WriteLine($"[Dump] m_playerMap: {count} entries");

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic player = valueSlots[i];
                    if (player == null) { Console.WriteLine($"  [{i}] null"); continue; }

                    int id = (int)(player["m_id"] ?? 0);
                    int side = (int)(player["m_side"] ?? 0);
                    string name = (string)(player["m_name"] ?? "");

                    // 读取 tags
                    var tags = ReadTagDict(player["m_tags"]?["m_values"]);
                    int playerId = GetTagValue(tags, 2); // PLAYER_ID
                    int controller = GetTagValue(tags, 50); // CONTROLLER

                    Console.WriteLine($"  [{i}] Key={keySlots[i]}, Id={id}, Side={side}, Name={name}");
                    Console.WriteLine($"       PLAYER_ID={playerId}, CONTROLLER={controller}");

                    // 输出所有 tags（限制数量）
                    int tagCount = 0;
                    foreach (var kv in tags)
                    {
                        if (tagCount++ > 20) { Console.WriteLine("       ... (truncated)"); break; }
                        Console.WriteLine($"       Tag {kv.Key} = {kv.Value}");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"  [{i}] ERROR: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Dump] DumpPlayerMap failed: {ex.Message}"); }
    }

    /// <summary>诊断：输出 m_entityMap 中的英雄实体</summary>
    public void DumpHeroEntities()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) { Console.WriteLine("[Dump] gameState null"); return; }

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) { Console.WriteLine("[Dump] entityMap null"); return; }

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) { Console.WriteLine("[Dump] valueSlots null"); return; }

            int size = GetCollectionSize(valueSlots);
            Console.WriteLine($"[Dump] m_entityMap: {size} entries");

            int heroCount = 0;
            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202); // CARDTYPE
                    if (cardType != 3) continue; // 只要 HERO

                    heroCount++;
                    string cardId = (string)node["m_cardIdInternal"];
                    int entityId = GetTagValue(tags, 53); // ENTITY_ID
                    int playerId = GetTagValue(tags, 2); // PLAYER_ID
                    int controller = GetTagValue(tags, 50); // CONTROLLER
                    int zone = GetTagValue(tags, 49); // ZONE

                    Console.WriteLine($"  Hero: {cardId} (EntityId={entityId})");
                    Console.WriteLine($"    PLAYER_ID={playerId}, CONTROLLER={controller}, ZONE={zone}");

                    // 输出所有 tags（限制数量）
                    int tagCount = 0;
                    foreach (var kv in tags)
                    {
                        if (tagCount++ > 15) { Console.WriteLine("    ... (truncated)"); break; }
                        Console.WriteLine($"    Tag {kv.Key} = {kv.Value}");
                    }
                }
                catch { continue; }
            }

            if (heroCount == 0)
                Console.WriteLine("  未找到英雄实体");
        }
        catch (Exception ex) { Console.WriteLine($"[Dump] DumpHeroEntities failed: {ex.Message}"); }
    }

    /// <summary>诊断：输出 ZONE=PLAY 的英雄</summary>
    public void DumpPlayZoneHeroes()
    {
        try
        {
            var localController = GetLocalControllerId();
            Console.WriteLine($"[Dump] 本地 CONTROLLER: {localController}");

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return;
            int size = GetCollectionSize(valueSlots);

            Console.WriteLine($"[Dump] 扫描 {size} 个实体，查找 ZONE=1 的英雄...");

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202);
                    if (cardType != 3) continue;

                    int zone = GetTagValue(tags, 49);
                    if (zone != 1) continue; // ZONE_PLAY = 1

                    string cardId = (string)node["m_cardIdInternal"];
                    int controller = GetTagValue(tags, 50);
                    int entityId = GetTagValue(tags, 53);

                    bool isLocal = localController != null && controller == localController.Value;
                    string who = isLocal ? "【本地玩家】" : "【对手】";

                    Console.WriteLine($"  {who} {cardId} (EntityId={entityId}, CONTROLLER={controller})");
                }
                catch { continue; }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Dump] DumpPlayZoneHeroes failed: {ex.Message}"); }
    }

    public BattlegroundsLobbyInfo GetBattlegroundsLobbyInfo()
    {
        try
        {
            // 反编译 HearthMirror: GameState.s_instance.m_playerInfoMap.valueSlots
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            dynamic playerInfoMap = gameState["m_playerInfoMap"];
            if (playerInfoMap == null) return null;

            dynamic valueSlots = playerInfoMap["valueSlots"];
            if (valueSlots == null) return null;

            var lobbyInfo = new BattlegroundsLobbyInfo();

            // 读取 GameUuid
            try
            {
                dynamic gameEntity = gameState["m_gameEntity"];
                if (gameEntity != null)
                {
                    dynamic uuid = gameEntity["Uuid"];
                    if (uuid != null)
                        lobbyInfo.GameUuid = uuid.ToString();
                }
            }
            catch { }

            // 遍历 valueSlots（直接遍历，遇到 null 停止）
            for (int i = 0; i < 16; i++)
            {
                try
                {
                    dynamic playerInfo = valueSlots[i];
                    if (playerInfo == null) break;

                    var player = new BattlegroundsLobbyPlayer();

                    // AccountId: m_gameAccountId.low_ / high_（直接访问，不通过 EntityId）
                    dynamic gameAccountId = playerInfo["m_gameAccountId"];
                    if (gameAccountId != null)
                    {
                        ulong lo = 0, hi = 0;
                        try { lo = (ulong)(int)(gameAccountId["low_"] ?? 0); } catch { }
                        try { hi = (ulong)(int)(gameAccountId["high_"] ?? 0); } catch { }

                        // fallback: 尝试 <EntityId>k__BackingField
                        if (lo == 0)
                        {
                            try
                            {
                                var entityId = gameAccountId["<EntityId>k__BackingField"];
                                if (entityId != null)
                                {
                                    lo = (ulong)(int)(entityId["low_"] ?? 0);
                                    hi = (ulong)(int)(entityId["high_"] ?? 0);
                                }
                            }
                            catch { }
                        }

                        player.AccountId = new AccountId { Hi = hi, Lo = lo };
                    }

                    // HeroCardId: m_playerHero.m_cardIdInternal
                    dynamic playerHero = playerInfo["m_playerHero"];
                    if (playerHero != null)
                    {
                        player.HeroCardId = (string)(playerHero["m_cardIdInternal"]
                            ?? playerHero["m_staticEntityDef"]?["m_cardIdInternal"]
                            ?? "");
                    }

                    // Name: m_name
                    player.Name = (string)(playerInfo["m_name"] ?? "");

                    lobbyInfo.Players.Add(player);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BGSpy] Error reading lobby player {i}: {ex.Message}");
                }
            }

            // 构建 playerId → Lo 映射（从 m_playerMap）
            try
            {
                dynamic pMap = gameState["m_playerMap"];
                if (pMap != null)
                {
                    dynamic pKeySlots = pMap["keySlots"];
                    dynamic pValueSlots = pMap["valueSlots"];
                    if (pKeySlots != null && pValueSlots != null)
                    {
                        int pCount = GetCollectionSize(pKeySlots);
                        for (int i = 0; i < pCount; i++)
                        {
                            try
                            {
                                dynamic pPlayer = pValueSlots[i];
                                if (pPlayer == null) continue;
                                int playerId = (int)(pKeySlots[i] ?? 0);
                                if (playerId == 0) continue;

                                dynamic gameAccountId = pPlayer["m_gameAccountId"];
                                if (gameAccountId != null)
                                {
                                    ulong lo = 0;
                                    try { lo = (ulong)(int)(gameAccountId["low_"] ?? 0); } catch { }
                                    if (lo == 0)
                                    {
                                        try
                                        {
                                            var entityId = gameAccountId["<EntityId>k__BackingField"];
                                            if (entityId != null)
                                                lo = (ulong)(int)(entityId["low_"] ?? 0);
                                        }
                                        catch { }
                                    }
                                    if (lo != 0)
                                        lobbyInfo.PlayerIdToLo[playerId] = lo;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return lobbyInfo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetBattlegroundsLobbyInfo failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 读取 SceneMgr.s_instance.m_mode（HDT 方案：内存轮询检测场景变化）
    /// </summary>
    public int? GetSceneMode()
    {
        try
        {
            var sceneMgr = _image?["SceneMgr"]?["s_instance"];
            if (sceneMgr == null) return null;
            return (int)sceneMgr["m_mode"];
        }
        catch
        {
            // 场景切换时 ReadProcessMemory 可能部分失败，静默重试
            return null;
        }
    }

    /// <summary>
    /// 检测好友列表是否打开。
    /// 路径: ChatMgr.s_instance.m_friendListFrame != null
    /// </summary>
    public bool IsFriendsListVisible()
    {
        try
        {
            var chatMgr = _image?["ChatMgr"]?["s_instance"];
            if (chatMgr == null) return false;
            var friendListFrame = TryGetField(chatMgr, "m_friendListFrame");
            return friendListFrame != null;
        }
        catch
        {
            return false;
        }
    }

    public List<NameCardId> GetBattlegroundsHeroOptions() => null;
    public BattlegroundRatingInfo GetBattlegroundRatingInfo()
    {
        try
        {
            // 路径: GetService("NetCache") -> m_netCache.valueSlots -> [NetCacheBaconRatingInfo]
            dynamic netCache = GetService("NetCache");
            if (netCache == null) return null;

            dynamic slots = netCache["m_netCache"]?["valueSlots"];
            if (slots == null) return null;

            int size = GetCollectionSize(slots);
            for (int i = 0; i < size; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;

                // 直接尝试读取 Rating 字段来识别 NetCacheBaconRatingInfo
                try
                {
                    int rating = (int)(slot["<Rating>k__BackingField"] ?? slot["Rating"] ?? 0);
                    if (rating > 0)
                    {
                        return new BattlegroundRatingInfo
                        {
                            Rating = rating,
                            DuosRating = (int)(slot["<DuosRating>k__BackingField"] ?? slot["DuosRating"] ?? 0),
                        };
                    }
                }
                catch { continue; }
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetBattlegroundRatingInfo failed: {ex.Message}");
            return null;
        }
    }
    public RatingChangeData GetBaconRatingChangeData()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            var gameEntity = gameState["m_gameEntity"];
            if (gameEntity == null) return null;

            // 字段名是 <RatingChangeData>k__BackingField，直接访问 property 会抛异常
            dynamic ratingData = TryGetField(gameEntity, "<RatingChangeData>k__BackingField");
            if (ratingData == null) return null;

            int newRating = (int)(TryGetField(ratingData, "_NewRating")
                ?? TryGetField(ratingData, "<NewRating>k__BackingField")
                ?? TryGetField(ratingData, "NewRating") ?? 0);
            int ratingChange = (int)(TryGetField(ratingData, "_RatingChange")
                ?? TryGetField(ratingData, "<RatingChange>k__BackingField")
                ?? TryGetField(ratingData, "RatingChange") ?? 0);

            if (newRating == 0 && ratingChange == 0) return null;

            return new RatingChangeData
            {
                NewRating = newRating,
                OldRating = newRating - ratingChange,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetBaconRatingChangeData failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 读取游戏服务器地址和端口。
    /// 路径: GetService("Network") → m_state → LastGameServerInfo → Address / Port
    /// 只在游戏开始后（场景 4）才能获取到。
    /// </summary>
    public (string address, uint port)? GetServerInfo()
    {
        try
        {
            dynamic network = GetService("Network");
            if (network == null) return null;

            dynamic state = network["m_state"];
            if (state == null) return null;

            dynamic serverInfo = null;
            try { serverInfo = state["<LastGameServerInfo>k__BackingField"]; } catch { }
            if (serverInfo == null) return null;

            // Address 和 Port 是 property，需要读 backing field
            string address = null;
            uint port = 0;
            try { address = (string)serverInfo["<Address>k__BackingField"]; } catch { }
            try { port = (uint)serverInfo["<Port>k__BackingField"]; } catch { }

            if (string.IsNullOrEmpty(address) || port == 0)
                return null;

            return (address, port);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetServerInfo failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>安全读取字段，不存在时返回 null 而非抛异常</summary>
    private static dynamic TryGetField(dynamic obj, string fieldName)
    {
        try { return obj[fieldName]; }
        catch { return null; }
    }

    /// <summary>获取 PlayerLeaderboardManager.s_instance（用于探索排名相关字段）</summary>
    public dynamic GetPlayerLeaderboardManager()
    {
        try
        {
            return _image?["PlayerLeaderboardManager"]?["s_instance"];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取鼠标悬停的排行榜玩家的英雄卡牌 ID。
    /// 路径: PlayerLeaderboardManager.s_instance.m_currentlyMousedOverTile.m_entity.m_cardIdInternal
    /// </summary>
    public string GetLeaderboardHoveredHeroCardId()
    {
        try
        {
            dynamic leaderboard = _image?["PlayerLeaderboardManager"]?["s_instance"];
            if (leaderboard == null) return null;

            dynamic tile = leaderboard["m_currentlyMousedOverTile"];
            if (tile == null) return null;

            dynamic entity = tile["m_entity"];
            if (entity == null) return null;

            string cardId = null;
            try { cardId = (string)entity["m_cardIdInternal"]; } catch { }

            return string.IsNullOrEmpty(cardId) ? null : cardId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取鼠标悬停的排行榜玩家的 EntityId。
    /// 路径: PlayerLeaderboardManager.s_instance.m_currentlyMousedOverTile.m_entity
    /// </summary>
    public int GetLeaderboardHoveredEntityId()
    {
        try
        {
            dynamic leaderboard = _image?["PlayerLeaderboardManager"]?["s_instance"];
            if (leaderboard == null) return 0;

            dynamic tile = leaderboard["m_currentlyMousedOverTile"];
            if (tile == null) return 0;

            dynamic entity = tile["m_entity"];
            if (entity == null) return 0;

            // 尝试读取 m_entityId
            try { return (int)entity["m_entityId"]; } catch { }

            // 备用：从 tags 读取 ENTITY_ID (tag 53)
            var tags = ReadTagDict(entity["m_tags"]?["m_values"]);
            return GetTagValue(tags, 53);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 获取鼠标悬停的排行榜玩家的 PLAYER_ID。
    /// 优先直接从 tile.m_playerId 读取（唯一、不受畸变影响），
    /// fallback 到 entity tag 2，再 fallback 到排行榜 m_playerId 匹配，
    /// 最后才用 heroCardId 在 m_entityMap 中查找。
    /// </summary>
    public int GetLeaderboardHoveredPlayerId()
    {
        try
        {
            dynamic leaderboard = _image?["PlayerLeaderboardManager"]?["s_instance"];
            if (leaderboard == null) return 0;

            dynamic tile = leaderboard["m_currentlyMousedOverTile"];
            if (tile == null) return 0;

            // 尝试直接读取 tile.m_playerId（游戏版本可能已移除此字段）
            try
            {
                int directPlayerId = (int)tile["m_playerId"];
                if (directPlayerId != 0) return directPlayerId;
            }
            catch { }

            dynamic entity = tile["m_entity"];
            if (entity == null) return 0;

            // 从 entity tags 读取 PLAYER_ID
            var tags = ReadTagDict(entity["m_tags"]?["m_values"]);
            int playerId = GetTagValue(tags, 2);
            if (playerId != 0) return playerId;

            // 用 heroCardId 在排行榜条目中查找
            string heroCardId = null;
            try { heroCardId = (string)entity["m_cardIdInternal"]; } catch { }
            if (!string.IsNullOrEmpty(heroCardId))
            {
                var rankings = GetPlayerRankings();
                foreach (var r in rankings)
                {
                    if (string.Equals(r.heroCardId, heroCardId, StringComparison.OrdinalIgnoreCase) && r.playerId != 0)
                        return r.playerId;
                }
            }

            // 最后 fallback: m_entityMap 查找
            if (string.IsNullOrEmpty(heroCardId)) return 0;
            return GetPlayerIdByHeroCardId(heroCardId);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>诊断：输出悬停 tile 的所有字段</summary>
    public void DumpHoveredTile()
    {
        try
        {
            dynamic leaderboard = _image?["PlayerLeaderboardManager"]?["s_instance"];
            if (leaderboard == null) { Console.WriteLine("[Dump] leaderboard null"); return; }

            dynamic tile = leaderboard["m_currentlyMousedOverTile"];
            if (tile == null) { Console.WriteLine("[Dump] tile null (未悬停)"); return; }

            Console.WriteLine("[Dump] Hovered tile fields:");
            // 尝试读取常见字段
            try { Console.WriteLine($"  m_entity: {tile["m_entity"]}"); } catch { }
            try { Console.WriteLine($"  m_teamId: {tile["m_teamId"]}"); } catch { }
            try { Console.WriteLine($"  m_playerId: {tile["m_playerId"]}"); } catch { }

            dynamic entity = tile["m_entity"];
            if (entity != null)
            {
                Console.WriteLine("[Dump] Entity fields:");
                try { Console.WriteLine($"  m_cardIdInternal: {entity["m_cardIdInternal"]}"); } catch { }
                try { Console.WriteLine($"  m_entityId: {entity["m_entityId"]}"); } catch { }

                var tags = ReadTagDict(entity["m_tags"]?["m_values"]);
                Console.WriteLine($"  Tags count: {tags.Count}");
                int count = 0;
                foreach (var kv in tags)
                {
                    if (count++ > 20) { Console.WriteLine("  ... (truncated)"); break; }
                    Console.WriteLine($"  Tag {kv.Key} = {kv.Value}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Dump] DumpHoveredTile failed: {ex.Message}"); }
    }

    /// <summary>
    /// 获取所有玩家的实时排名信息。
    /// 返回: List of (heroCardId, placement, isDead, teamId, playerId)
    /// </summary>
    public List<(string heroCardId, int placement, bool isDead, int teamId, int playerId)> GetPlayerRankings()
    {
        var result = new List<(string heroCardId, int placement, bool isDead, int teamId, int playerId)>();
        try
        {
            dynamic leaderboard = _image?["PlayerLeaderboardManager"]?["s_instance"];
            if (leaderboard == null) return result;

            dynamic teams = leaderboard["m_teams"];
            if (teams == null) return result;

            dynamic items = null;
            int size = 0;
            try { items = teams["_items"]; } catch { }
            try { size = (int)teams["_size"]; } catch { }
            if (size == 0)
                try { size = (int)teams["Count"]; } catch { }

            if (items == null || size <= 0) return result;

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var team = items[i];
                    if (team == null) continue;

                    // 读取 m_teamId
                    int teamId = 0;
                    try { teamId = (int)team["m_teamId"]; } catch { }

                    dynamic members = team["m_teamMembers"];
                    if (members == null) continue;

                    dynamic memberItems = null;
                    int memberCount = 0;
                    try { memberItems = members["_items"]; } catch { }
                    try { memberCount = (int)members["_size"]; } catch { }
                    if (memberCount == 0)
                        try { memberCount = (int)members["Count"]; } catch { }

                    if (memberItems == null || memberCount <= 0) continue;

                    var member = memberItems[0];
                    if (member == null) continue;

                    dynamic entry = member["<Entry>k__BackingField"];
                    if (entry == null) continue;

                    // 读取 m_dead
                    bool isDead = false;
                    try { isDead = (bool)entry["m_dead"]; } catch { }

                    // 读取 m_playerId（用于匹配 LobbyPlayers）
                    int playerId = 0;
                    try { playerId = (int)entry["m_playerId"]; } catch { }

                    // 读取 m_entity
                    dynamic entity = entry["m_entity"];
                    if (entity == null) continue;

                    // 读取英雄卡牌 ID
                    string heroCardId = null;
                    try { heroCardId = (string)entity["m_cardIdInternal"]; } catch { }
                    if (string.IsNullOrEmpty(heroCardId)) continue;

                    // 读取排名
                    int placement = 0;
                    try { placement = (int)entity["m_realTimePlayerLeaderboardPlace"]; } catch { }

                    result.Add((heroCardId, placement, isDead, teamId, playerId));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetPlayerRankings failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>读取当前回合数（TURN tag = 20 on GameEntity）</summary>
    public int? GetTurnNumber()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            dynamic gameEntity = gameState["m_gameEntity"];
            if (gameEntity == null) return null;

            dynamic tags = gameEntity["m_tags"]?["m_values"];
            if (tags == null) return null;

            dynamic entries = tags["_entries"];
            int count = (int)(tags["_count"] ?? 0);
            if (entries == null || count <= 0) return null;

            // TURN = tag 20
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                int key = (int)(entry["key"] ?? 0);
                if (key == 20) // TURN
                    return (int)(entry["value"] ?? 0);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取战斗伤害上限（BACON_COMBAT_DAMAGE_CAP = 2089, ENABLED = 3403）</summary>
    public int GetDamageCap()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic gameEntity = gameState["m_gameEntity"];
            if (gameEntity == null) return 0;

            dynamic tags = gameEntity["m_tags"]?["m_values"];
            if (tags == null) return 0;

            dynamic entries = tags["_entries"];
            int count = (int)(tags["_count"] ?? 0);
            if (entries == null || count <= 0) return 0;

            int capEnabled = 0;
            int capValue = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                int key = (int)(entry["key"] ?? 0);
                if (key == 3403) // BACON_COMBAT_DAMAGE_CAP_ENABLED
                    capEnabled = (int)(entry["value"] ?? 0);
                else if (key == 2089) // BACON_COMBAT_DAMAGE_CAP
                    capValue = (int)(entry["value"] ?? 0);
            }

            return capEnabled > 0 ? capValue : 0;
        }
        catch
        {
            return 0;
        }
    }

    public List<int> GetAvailableBattlegroundsRaces()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            dynamic container = gameState["m_availableRacesInBattlegroundsExcludingAmalgam"];
            if (container == null) return null;

            dynamic items = container["_items"];
            int size = (int)(container["_size"] ?? 0);
            if (items == null || size <= 0) return null;

            var races = new List<int>();
            for (int i = 0; i < size; i++)
            {
                int val = (int)(items[i] ?? 0);
                if (val > 0) races.Add(val);
            }
            return races.Count > 0 ? races : null;
        }
        catch
        {
            return null;
        }
    }
    public SelectedBattlegroundsGameMode GetSelectedBattlegroundsGameMode() => SelectedBattlegroundsGameMode.Standard;
    public int? GetBattlegroundsLeaderboardHoveredEntityId() => null;
    public BattlegroundsTeammateBoardState GetBattlegroundsTeammateBoardState() => null;
    public OpponentBoardState GetOpponentBoardState()
    {
        try
        {
            // 路径: ZoneMgr.s_instance.m_zones._items -> [ZonePlay where m_Side==2]
            var zonePlay = FindZonePlay(2); // m_Side=2 = opponent
            if (zonePlay == null) return null;

            var result = new OpponentBoardState();
            dynamic cards = zonePlay["m_cards"]?["_items"];
            if (cards == null) return result;

            // 通过 m_entityMap 获取对手的英雄 cardId
            result.HeroCardId = GetOpponentHeroCardId();

            int size = GetCollectionSize(cards);
            int skippedCount = 0;
            for (int i = 0; i < size; i++)
            {
                var card = cards[i];
                if (card == null) continue;

                string cardId = (string)card["m_entity"]?["m_cardIdInternal"];
                if (string.IsNullOrEmpty(cardId)) continue;

                var tags = ReadTagDict(card["m_entity"]?["m_tags"]?["m_values"]);
                int entityId = GetTagValue(tags, 53); // ENTITY_ID
                int zonePos = (int)(card["m_zonePosition"] ?? 0);
                int cardType = GetTagValue(tags, 202); // CARDTYPE: 3=HERO, 4=MINION

                int zone = GetTagValue(tags, 49); // ZONE: 1=PLAY, 2=DECK, 4=GRAVEYARD

                // 识别对手英雄 (CARDTYPE = 3)
                if (cardType == 3 && string.IsNullOrEmpty(result.HeroCardId))
                {
                    result.HeroCardId = cardId;
                }
                else if (cardType == 4 && zone == 1) // MINION + ZONE_PLAY
                {
                    result.BoardCards.Add(new BoardCard
                    {
                        CardId = cardId,
                        EntityId = entityId,
                        ZonePosition = zonePos,
                        Attack = GetTagValue(tags, 47),    // ATK
                        Health = GetTagValue(tags, 45),     // HEALTH
                        Golden = GetTagValue(tags, 364) > 0, // PREMIUM
                        Taunt = GetTagValue(tags, 190) > 0,
                        DivineShield = GetTagValue(tags, 194) > 0,
                        Poisonous = GetTagValue(tags, 363) > 0,
                        Venomous = GetTagValue(tags, 2853) > 0,
                        Windfury = GetTagValue(tags, 189) > 0,
                        Reborn = GetTagValue(tags, 1085) > 0,
                        Stealth = GetTagValue(tags, 191) > 0,
                        Deathrattle = GetTagValue(tags, 217) > 0,
                        TechLevel = GetTagValue(tags, 1440), // TECH_LEVEL
                    });
                }
                else
                {
                    // 记录被跳过的实体
                    skippedCount++;
                    Console.WriteLine($"[BGSpy] ZonePlay(2) 跳过: {cardId} cardType={cardType}");
                }
            }
            result.BoardCards.Sort((a, b) => a.ZonePosition.CompareTo(b.ZonePosition));

            // 从 m_entityMap 查找对手英雄的 PLAYER_ID（ZonePlay 的 hero entity 可能没有这个 tag）
            if (!string.IsNullOrEmpty(result.HeroCardId))
            {
                result.PlayerId = GetPlayerIdByHeroCardId(result.HeroCardId);
            }

            Console.WriteLine($"[BGSpy] GetOpponentBoardState: hero={result.HeroCardId}, playerId={result.PlayerId}, minions={result.BoardCards.Count}, skipped={skippedCount}, total={size}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetOpponentBoardState failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 通过英雄卡牌 ID 在 m_entityMap 中查找 PLAYER_ID。
    /// 对齐 HDT: _game.Entities.Values.FirstOrDefault(x => x.CardId == heroCardId).GetTag(GameTag.PLAYER_ID)
    /// </summary>
    private int GetPlayerIdByHeroCardId(string heroCardId)
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return 0;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return 0;

            int size = GetCollectionSize(valueSlots);
            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    string cardId = (string)node["m_cardIdInternal"];
                    if (cardId != heroCardId) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202); // CARDTYPE
                    if (cardType != 3) continue; // 只要 HERO

                    return GetTagValue(tags, 2); // PLAYER_ID
                }
                catch { continue; }
            }
        }
        catch { }
        return 0;
    }

    /// <summary>获取对手英雄的 EntityId（从 ZONE=PLAY 的英雄中找非本地玩家的）</summary>
    public int GetOpponentEntityId()
    {
        try
        {
            var localController = GetLocalControllerId();
            if (localController == null) return 0;

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return 0;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return 0;
            int size = GetCollectionSize(valueSlots);

            // 查找 ZONE=1 (PLAY) 的英雄，排除本地玩家
            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202); // CARDTYPE
                    if (cardType != 3) continue; // 只要 HERO

                    int zone = GetTagValue(tags, 49); // ZONE
                    if (zone != 1) continue; // ZONE_PLAY = 1

                    int controller = GetTagValue(tags, 50); // CONTROLLER
                    if (controller == localController.Value) continue; // 跳过本地玩家

                    int entityId = GetTagValue(tags, 53); // ENTITY_ID
                    if (entityId > 0)
                    {
                        string cardId = (string)node["m_cardIdInternal"];
                        Console.WriteLine($"[BGSpy] GetOpponentEntityId: {cardId} entityId={entityId}");
                        return entityId;
                    }
                }
                catch { continue; }
            }

            Console.WriteLine("[BGSpy] GetOpponentEntityId: no opponent hero in ZONE_PLAY");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetOpponentEntityId failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>获取对手英雄的 cardId（从 ZONE=PLAY 的英雄中找非本地玩家的）</summary>
    public string GetOpponentHeroCardId()
    {
        try
        {
            var localController = GetLocalControllerId();
            if (localController == null) return null;

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return null;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return null;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return null;
            int size = GetCollectionSize(valueSlots);

            // 查找 ZONE=1 (PLAY) 的英雄，排除本地玩家和鲍勃
            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202); // CARDTYPE
                    if (cardType != 3) continue; // 只要 HERO

                    int zone = GetTagValue(tags, 49); // ZONE
                    if (zone != 1) continue; // ZONE_PLAY = 1

                    int controller = GetTagValue(tags, 50); // CONTROLLER
                    if (controller == localController.Value) continue; // 跳过本地玩家

                    string cardId = (string)node["m_cardIdInternal"];
                    if (string.IsNullOrEmpty(cardId)) continue;

                    // 跳过鲍勃（酒馆老板）
                    if (cardId == "TB_BaconShopBob") continue;

                    Console.WriteLine($"[BGSpy] GetOpponentHeroCardId: {cardId}");
                    return cardId;
                }
                catch { continue; }
            }

            Console.WriteLine("[BGSpy] GetOpponentHeroCardId: no opponent hero in ZONE_PLAY");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetOpponentHeroCardId failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取当前战斗对手的 PLAYER_ID（直接从 ZONE_PLAY 的英雄实体读取 tag 2）。
    /// 不依赖 heroCardId 查找，畸变时也能正确识别唯一对手。
    /// </summary>
    public int GetOpponentPlayerIdInPlay()
    {
        try
        {
            var localController = GetLocalControllerId();
            if (localController == null) return 0;

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return 0;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return 0;
            int size = GetCollectionSize(valueSlots);

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    int cardType = GetTagValue(tags, 202); // CARDTYPE
                    if (cardType != 3) continue; // 只要 HERO

                    int zone = GetTagValue(tags, 49); // ZONE
                    if (zone != 1) continue; // ZONE_PLAY = 1

                    int controller = GetTagValue(tags, 50); // CONTROLLER
                    if (controller == localController.Value) continue; // 跳过本地玩家

                    string cardId = (string)node["m_cardIdInternal"];
                    if (string.IsNullOrEmpty(cardId)) continue;
                    if (cardId == "TB_BaconShopBob") continue;

                    int playerId = GetTagValue(tags, 2); // PLAYER_ID
                    Console.WriteLine($"[BGSpy] GetOpponentPlayerIdInPlay: playerId={playerId} (hero={cardId})");
                    return playerId;
                }
                catch { continue; }
            }

            Console.WriteLine("[BGSpy] GetOpponentPlayerIdInPlay: no opponent hero in ZONE_PLAY");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetOpponentPlayerIdInPlay failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>读取当前玩家的饰品列表（从 m_entityMap 读取，BACON_TRINKET=3407）</summary>
    public List<string> GetPlayerTrinkets()
    {
        var result = new List<string>();
        try
        {
            // 先获取本地玩家的 controller ID
            var localController = GetLocalControllerId();
            if (localController == null) return result;

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return result;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return result;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return result;
            int size = GetCollectionSize(valueSlots);
            if (size <= 0) return result;

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    string cardId = (string)(node["m_cardIdInternal"] ?? "");
                    if (string.IsNullOrEmpty(cardId)) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    // BACON_TRINKET = 3407, CONTROLLER = 50
                    if (GetTagValue(tags, 3407) > 0 && GetTagValue(tags, 50) == localController.Value)
                        result.Add(cardId);
                }
                catch { continue; }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetPlayerTrinkets failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>读取饰品详细信息（调试用，返回每个饰品的关键 tag）</summary>
    public List<string> GetPlayerTrinketsDetailed()
    {
        var result = new List<string>();
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return result;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return result;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return result;
            int size = GetCollectionSize(valueSlots);
            if (size <= 0) return result;

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    string cardId = (string)(node["m_cardIdInternal"] ?? "");
                    if (string.IsNullOrEmpty(cardId)) continue;

                    var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                    if (GetTagValue(tags, 3407) > 0) // BACON_TRINKET
                    {
                        int entityId = GetTagValue(tags, 53);   // ENTITY_ID
                        int controller = GetTagValue(tags, 50);  // CONTROLLER
                        int zone = GetTagValue(tags, 49);        // ZONE
                        int zonePos = GetTagValue(tags, 263);    // ZONE_POSITION
                        int baconTrinket = GetTagValue(tags, 3407); // BACON_TRINKET

                        result.Add($"{cardId} | entityId={entityId} controller={controller} zone={zone} zonePos={zonePos} baconTrinket={baconTrinket}");
                    }
                }
                catch { continue; }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetPlayerTrinketsDetailed failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>读取本地玩家的英雄实体 ID（从 m_entityMap 中查找英雄卡牌）</summary>
    public int GetLocalHeroEntityId()
    {
        try
        {
            var localController = GetLocalControllerId();
            if (localController == null) return 0;

            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) return 0;

            dynamic valueSlots = entityMap["valueSlots"];
            if (valueSlots == null) return 0;
            int size = GetCollectionSize(valueSlots);
            if (size <= 0) return 0;

            for (int i = 0; i < size; i++)
            {
                try
                {
                    var node = valueSlots[i];
                    if (node == null) continue;

                    string cardId = (string)(node["m_cardIdInternal"] ?? "");
                    if (string.IsNullOrEmpty(cardId)) continue;

                    // 英雄卡牌以 TB_BaconShop_HERO_ 或 BG 开头，且 CONTROLLER=本地玩家
                    if ((cardId.StartsWith("TB_BaconShop_HERO_") || cardId.StartsWith("BG"))
                        && cardId.Contains("HERO"))
                    {
                        var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                        if (GetTagValue(tags, 50) == localController.Value) // CONTROLLER
                        {
                            int entityId = GetTagValue(tags, 53); // ENTITY_ID
                            if (entityId > 0) return entityId;
                        }
                    }
                }
                catch { continue; }
            }
        }
        catch { }
        return 0;
    }

    /// <summary>读取畸变 DBF ID（BACON_GLOBAL_ANOMALY_DBID = 2897，从 GameEntity tags 读取）</summary>
    public int GetAnomalyDbfId()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return 0;

            dynamic gameEntity = gameState["m_gameEntity"];
            if (gameEntity == null) return 0;

            var tags = ReadTagDict(gameEntity["m_tags"]?["m_values"]);
            return GetTagValue(tags, 2897); // BACON_GLOBAL_ANOMALY_DBID
        }
        catch { return 0; }
    }

    public bool IsGameOver()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return false;
            return (bool)(gameState["m_gameOver"] ?? false);
        }
        catch
        {
            return false;
        }
    }
    public bool IsMulligan() => false;
    public List<BoardMinion> GetPlayerBoardMinions()
    {
        try
        {
            // 路径: ZoneMgr.s_instance.m_zones._items -> [ZonePlay where m_Side==1]
            var zonePlay = FindZonePlay(1); // m_Side=1 = local player
            if (zonePlay == null) return null;

            var result = new List<BoardMinion>();
            dynamic cards = zonePlay["m_cards"]?["_items"];
            if (cards == null) return result;

            int size = GetCollectionSize(cards);
            for (int i = 0; i < size; i++)
            {
                var card = cards[i];
                if (card == null) continue;

                string cardId = (string)card["m_entity"]?["m_cardIdInternal"];
                if (string.IsNullOrEmpty(cardId)) continue;

                var tags = ReadTagDict(card["m_entity"]?["m_tags"]?["m_values"]);
                int cardType = GetTagValue(tags, 202); // CARDTYPE
                if (cardType != 4) continue; // 只要 MINION

                int entityId = GetTagValue(tags, 53); // ENTITY_ID
                int zonePos = (int)(card["m_zonePosition"] ?? 0);

                result.Add(new BoardMinion
                {
                    CardId = cardId,
                    EntityId = entityId,
                    ZonePosition = zonePos,
                    Attack = GetTagValue(tags, 47),    // ATK
                    Health = GetTagValue(tags, 45),     // HEALTH
                    MaxAttack = GetTagValue(tags, 47),
                    MaxHealth = GetTagValue(tags, 45),
                    Golden = GetTagValue(tags, 364) > 0,    // PREMIUM
                    Taunt = GetTagValue(tags, 190) > 0,
                    DivineShield = GetTagValue(tags, 194) > 0,
                    Poisonous = GetTagValue(tags, 363) > 0,
                    Venomous = GetTagValue(tags, 2853) > 0,
                    Windfury = GetTagValue(tags, 189) > 0,
                    Reborn = GetTagValue(tags, 1085) > 0,
                    Stealth = GetTagValue(tags, 191) > 0,
                    Deathrattle = GetTagValue(tags, 217) > 0,
                    TechLevel = GetTagValue(tags, 1440), // TECH_LEVEL
                    Tags = tags,
                });
            }
            result.Sort((a, b) => a.ZonePosition.CompareTo(b.ZonePosition));
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetPlayerBoardMinions failed: {ex.Message}");
            return null;
        }
    }
    public HeroPowerState GetHeroPowerState() => null;
    public GameSummary GetGameSummary() => null;
    public List<BattlegroundsPlayer> GetBattlegroundsPlayers() => null;

    // ═══════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════

    /// <summary>从 tag 字典中读取指定 tag 的值</summary>
    private static int GetTagValue(Dictionary<int, int> tags, int tagKey)
    {
        return tags.TryGetValue(tagKey, out var val) ? val : 0;
    }

    /// <summary>从 m_tags.m_values 读取 tag 字典</summary>
    private static Dictionary<int, int> ReadTagDict(dynamic tagValues)
    {
        var result = new Dictionary<int, int>();
        if (tagValues == null) return result;

        try
        {
            int size = GetCollectionSize(tagValues);
            if (size == 0) return result;

            for (int i = 0; i < size; i++)
            {
                var entry = tagValues[i];
                if (entry == null) continue;
                int key = (int)(entry["key"] ?? 0);
                int value = (int)(entry["value"] ?? 0);
                if (key > 0) result[key] = value;
            }
        }
        catch
        {
            // 回退: 尝试 _entries 方式（某些字典结构不同）
            try
            {
                dynamic entries = tagValues["_entries"];
                int count = (int)(tagValues["_count"] ?? 0);
                if (entries != null && count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var entry = entries[i];
                        if (entry == null) continue;
                        int key = (int)(entry["key"] ?? 0);
                        int value = (int)(entry["value"] ?? 0);
                        if (key > 0) result[key] = value;
                    }
                }
            }
            catch { }
        }

        // 最终兜底：如果以上都失败，尝试直接读取单个 tag
        if (result.Count == 0)
        {
            try
            {
                // 尝试直接读取 BACON_TRINKET = 3407
                var val = tagValues[3407];
                if (val != null) result[3407] = (int)val;
            }
            catch { }
        }

        return result;
    }

    /// <summary>诊断 ServiceLocator 路径</summary>
    public string DiagnoseServiceLocator()
    {
        try
        {
            dynamic jobs = _image?["Hearthstone.HearthstoneJobs"]?["s_dependencyBuilder"];
            if (jobs == null) return "s_dependencyBuilder not found";

            dynamic items = jobs["_items"];
            if (items == null) return "_items not found";

            dynamic serviceLocator = items[0]?["m_serviceLocator"];
            if (serviceLocator == null) return "m_serviceLocator not found";

            dynamic services = serviceLocator["m_services"];
            if (services == null) return "m_services not found";

            dynamic entries = services["_entries"];
            int count = (int)(services["_count"] ?? 0);
            if (entries == null) return $"_entries not found, _count={count}";
            if (count <= 0) return $"_count={count}";

            // 读取第一个非空 entry 的字段名
            for (int i = 0; i < count && i < 3; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                var value = entry["value"];
                if (value == null) continue;

                // 尝试读取各种可能的字段名
                string[] fieldNames = { "ServiceTypeName", "<ServiceTypeName>k__BackingField", "Service", "<Service>k__BackingField" };
                var parts = new List<string>();
                foreach (var fn in fieldNames)
                {
                    try
                    {
                        var val = value[fn];
                        parts.Add($"{fn}={val}");
                    }
                    catch { parts.Add($"{fn}=N/A"); }
                }
                return $"entries[0]: {string.Join(", ", parts)}";
            }
            return $"_count={count} but no valid entries";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>获取指定服务</summary>
    private dynamic GetService(string name)
    {
        // 路径 1: HearthstoneJobs.s_dependencyBuilder（原有路径，NetCache 等服务可用）
        dynamic result = GetServiceViaHearthstoneJobs(name);
        if (result != null) return result;

        // 路径 2: Blizzard.T5.Services.ServiceManager（HearthMirror 路径，Network 等服务可用）
        return GetServiceViaServiceManager(name);
    }

    /// <summary>通过 HearthstoneJobs 获取服务</summary>
    private dynamic GetServiceViaHearthstoneJobs(string name)
    {
        try
        {
            dynamic jobs = _image?["Hearthstone.HearthstoneJobs"]?["s_dependencyBuilder"];
            if (jobs == null) return null;

            dynamic items = jobs["_items"];
            if (items == null) return null;

            dynamic serviceLocator = items[0]?["m_serviceLocator"];
            if (serviceLocator == null) return null;

            dynamic services = serviceLocator["m_services"];
            if (services == null) return null;

            return FindServiceByName(services, name);
        }
        catch { return null; }
    }

    /// <summary>通过 ServiceManager 获取服务（HearthMirror 路径）</summary>
    private dynamic GetServiceViaServiceManager(string name)
    {
        try
        {
            dynamic serviceMgr = _image?["Blizzard.T5.Services.ServiceManager"];
            if (serviceMgr == null) return null;

            // 尝试 s_runtimeServices
            dynamic container = null;
            try { container = serviceMgr["s_runtimeServices"]; } catch { }

            // 尝试 s_dynamicServices.m_serviceLocator
            if (container == null)
            {
                try
                {
                    dynamic dynamicServices = serviceMgr["s_dynamicServices"];
                    if (dynamicServices != null)
                        container = dynamicServices["m_serviceLocator"];
                }
                catch { }
            }
            if (container == null) return null;

            dynamic services = container["m_services"];
            if (services == null) return null;

            return FindServiceByName(services, name);
        }
        catch { return null; }
    }

    /// <summary>从 services 字典中按名称查找服务</summary>
    private dynamic FindServiceByName(dynamic services, string name)
    {
        dynamic entries = services["_entries"];
        int count = (int)(services["_count"] ?? 0);
        if (entries == null || count <= 0) return null;

        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            if (entry == null) continue;
            var value = entry["value"];
            if (value == null) continue;

            // 尝试 property 和 backing field 两种方式
            string typeName = null;
            try { typeName = (string)value["ServiceTypeName"]; } catch { }
            if (typeName == null)
                try { typeName = (string)value["<ServiceTypeName>k__BackingField"]; } catch { }

            if (typeName == name)
            {
                dynamic service = null;
                try { service = value["Service"]; } catch { }
                if (service == null)
                    try { service = value["<Service>k__BackingField"]; } catch { }
                return service;
            }
        }
        return null;
    }

    /// <summary>诊断 m_gameEntity 的 RatingChangeData 字段</summary>
    public string DiagnoseRatingChangeData()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) return "GameState.s_instance not found";

            var gameEntity = gameState["m_gameEntity"];
            if (gameEntity == null) return "m_gameEntity not found";

            // dump m_gameEntity 的 TypeDefinition 字段列表
            var typeDef = gameEntity.TypeDefinition;
            if (typeDef != null)
            {
                var fields = typeDef.Fields;
                var fieldNames = new List<string>();
                for (int i = 0; i < fields.Count; i++)
                {
                    try
                    {
                        var f = fields[i];
                        var name = f?.Name ?? "?";
                        // 只输出包含 Rating 或 rating 的字段
                        if (name.Contains("ating") || name.Contains("hange"))
                            fieldNames.Add(name);
                    }
                    catch { }
                }
                if (fieldNames.Count > 0)
                    return $"m_gameEntity rating fields: {string.Join(", ", fieldNames)}";
                return "m_gameEntity has no rating-related fields";
            }
            return "TypeDefinition not available";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>诊断 ZoneMgr 路径</summary>
    public string DiagnoseZoneMgr()
    {
        try
        {
            var zoneMgr = _image?["ZoneMgr"]?["s_instance"];
            if (zoneMgr == null) return "ZoneMgr.s_instance not found";

            var zones = zoneMgr["m_zones"];
            if (zones == null) return "m_zones not found";

            var items = zones["_items"];
            if (items == null) return "_items not found";

            int size = GetCollectionSize(items);
            if (size == 0) return "zones empty";

            var parts = new List<string>();
            for (int i = 0; i < size && i < 10; i++)
            {
                var zone = items[i];
                if (zone == null) { parts.Add($"[{i}]=null"); continue; }
                try
                {
                    int side = (int)(zone["m_Side"] ?? -1);
                    var cards = zone["m_cards"];
                    int cardCount = 0;
                    if (cards != null)
                    {
                        var items2 = cards["_items"];
                        cardCount = items2 != null ? GetCollectionSize(items2) : 0;
                    }
                    parts.Add($"[{i}] side={side} cards={cardCount}");
                }
                catch { parts.Add($"[{i}] error"); }
            }
            return $"zones({size}): {string.Join(", ", parts)}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>从 ZoneMgr 查找指定 Side 的 ZonePlay（参考 HearthMirror.GetOpponentBoardStateInternal）</summary>
    private dynamic FindZonePlay(int side)
    {
        dynamic zones = _image?["ZoneMgr"]?["s_instance"]?["m_zones"]?["_items"];
        if (zones == null) return null;

        int size = GetCollectionSize(zones);
        for (int i = 0; i < size; i++)
        {
            var zone = zones[i];
            if (zone == null) continue;
            if ((int)(zone["m_Side"] ?? -1) != side) continue;
            // ZonePlay 有 m_mousedOverSlot 字段（其他 Zone 类型没有）
            try
            {
                var _ = zone["m_mousedOverSlot"];
                return zone;
            }
            catch { continue; }
        }
        return null;
    }

    /// <summary>获取集合长度，兼容 UnitySpy size() 和 .NET Array.Length</summary>
    private static int GetCollectionSize(dynamic collection)
    {
        try { return (int)collection.size(); } catch { }
        try { return (int)collection.Length; } catch { }
        try { return (int)collection["_size"]; } catch { }
        try { return (int)collection["_count"]; } catch { }
        return 0;
    }

    /// <summary>获取本地玩家的 CONTROLLER ID（测试用）</summary>
    public int? GetLocalControllerIdPublic()
    {
        return GetLocalControllerId();
    }

    /// <summary>获取本地玩家的 CONTROLLER ID</summary>
    private int? GetLocalControllerId()
    {
        var gameState = _image?["GameState"]?["s_instance"];
        if (gameState == null) return null;

        dynamic playerMap = gameState["m_playerMap"];
        if (playerMap == null) return null;

        dynamic keySlots = playerMap["keySlots"];
        dynamic valueSlots = playerMap["valueSlots"];
        if (keySlots == null || valueSlots == null) return null;

        int count = GetCollectionSize(keySlots);
        if (count <= 0) return null;

        for (int i = 0; i < count; i++)
        {
            dynamic player = valueSlots[i];
            if (player == null) continue;

            // Side.FRIENDLY = 1 标识本地玩家
            int side = (int)(player["m_side"] ?? -1);
            if (side != 1) continue;

            // key 就是 controller ID
            return (int)(keySlots[i] ?? 0);
        }
        return null;
    }

    /// <summary>
    /// 获取当前战斗对手的英雄卡牌 ID（通过 NEXT_OPPONENT_PLAYER_ID 标签）。
    /// 路径: GameState.s_instance.m_entityMap -> 找到 PLAYER_ID 匹配的 player entity -> 读取 NEXT_OPPONENT_PLAYER_ID 标签
    /// </summary>
    public string GetNextOpponentHeroCardId()
    {
        try
        {
            var gameState = _image?["GameState"]?["s_instance"];
            if (gameState == null) { Console.WriteLine("[BGSpy] GetNextOpponent: gameState null"); return null; }

            // 1. 获取本地玩家的 PLAYER_ID
            int localPlayerId = 0;
            dynamic playerMap = gameState["m_playerMap"];
            if (playerMap != null)
            {
                dynamic keySlots = playerMap["keySlots"];
                dynamic valueSlots = playerMap["valueSlots"];
                if (keySlots != null && valueSlots != null)
                {
                    int count = GetCollectionSize(keySlots);
                    for (int i = 0; i < count; i++)
                    {
                        dynamic player = valueSlots[i];
                        if (player == null) continue;
                        int side = (int)(player["m_side"] ?? -1);
                        if (side == 1) // FRIENDLY
                        {
                            localPlayerId = (int)(keySlots[i] ?? 0);
                            break;
                        }
                    }
                }
            }

            if (localPlayerId == 0) { Console.WriteLine("[BGSpy] GetNextOpponent: localPlayerId=0"); return null; }
            Console.WriteLine($"[BGSpy] GetNextOpponent: localPlayerId={localPlayerId}");

            // 2. 在 m_entityMap 中找到本地玩家的 player entity
            dynamic entityMap = gameState["m_entityMap"];
            if (entityMap == null) { Console.WriteLine("[BGSpy] GetNextOpponent: entityMap null"); return null; }

            dynamic slots = entityMap["valueSlots"];
            if (slots == null) { Console.WriteLine("[BGSpy] GetNextOpponent: slots null"); return null; }
            int size = GetCollectionSize(slots);
            if (size <= 0) { Console.WriteLine("[BGSpy] GetNextOpponent: size=0"); return null; }

            for (int i = 0; i < size; i++)
            {
                var node = slots[i];
                if (node == null) continue;

                var tags = ReadTagDict(node["m_tags"]?["m_values"]);
                int playerId = GetTagValue(tags, 2); // PLAYER_ID = tag 2
                if (playerId != localPlayerId) continue;

                // 3. 读取 NEXT_OPPONENT_PLAYER_ID 标签 (tag 1427)
                int nextOpponentId = GetTagValue(tags, 1427);
                Console.WriteLine($"[BGSpy] GetNextOpponent: found player entity, NEXT_OPPONENT={nextOpponentId}");
                if (nextOpponentId == 0) return null;

                // 4. 找到对手的 hero entity，获取其 cardId
                for (int j = 0; j < size; j++)
                {
                    var oppNode = slots[j];
                    if (oppNode == null) continue;

                    var oppTags = ReadTagDict(oppNode["m_tags"]?["m_values"]);
                    int oppPlayerId = GetTagValue(oppTags, 2); // PLAYER_ID
                    if (oppPlayerId != nextOpponentId) continue;

                    // 检查是否是英雄 (CARDTYPE = 3)
                    int cardType = GetTagValue(oppTags, 202);
                    if (cardType != 3) continue; // HERO = 3

                    string cardId = (string)oppNode["m_cardIdInternal"];
                    if (!string.IsNullOrEmpty(cardId))
                    {
                        Console.WriteLine($"[BGSpy] GetNextOpponent: found hero {cardId}");
                        return cardId;
                    }
                }
            }

            Console.WriteLine("[BGSpy] GetNextOpponent: no match found");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BGSpy] GetNextOpponentHeroCardId failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>读取指定 ZONE 中的所有 entity</summary>
    private List<(string cardId, Dictionary<int, int> tags, int entityId, int zonePos)> ReadEntitiesInZone(int zoneId)
    {
        var result = new List<(string, Dictionary<int, int>, int, int)>();
        var gameState = _image?["GameState"]?["s_instance"];
        if (gameState == null) return result;

        dynamic entityMap = gameState["m_entityMap"];
        if (entityMap == null) return result;

        dynamic slots = entityMap["valueSlots"];
        int size = (int)(entityMap["_size"] ?? 0);
        if (slots == null || size <= 0) return result;

        for (int i = 0; i < size; i++)
        {
            var node = slots[i];
            if (node == null) continue;

            string cardId = (string)node["m_cardIdInternal"];
            if (string.IsNullOrEmpty(cardId)) continue;

            var tags = ReadTagDict(node["m_tags"]?["m_values"]);
            int zone = GetTagValue(tags, 49); // ZONE
            if (zone != zoneId) continue;

            int entityId = GetTagValue(tags, 53); // ENTITY_ID
            int zonePos = GetTagValue(tags, 263); // ZONE_POSITION

            result.Add((cardId, tags, entityId, zonePos));
        }
        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hsImage?.Dispose();
            _image?.Dispose();
            _disposed = true;
        }
    }
}
}
