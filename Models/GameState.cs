namespace HBT
{

/// <summary>
/// Parser 的外部解析状态。
/// 不包含逻辑，由 MainForm 持有并在每次 ProcessLine 后更新。
/// </summary>
public class GameState
{
    public bool InCreateBlock { get; set; }
    public bool CreateHasTurn { get; set; }
    public Game PendingNewGame { get; set; }
    public bool LoFetched { get; set; }
    public bool ReachedStep13 { get; set; }
    public bool ConcedePending { get; set; }
    public string ConcedeTag { get; set; } = "";
    public bool IsScanning { get; set; }
    public bool IsConstructed { get; set; }
    public string Mode { get; set; } = "";        // "BG"/"STD"/"WILD"
    public string FormatType { get; set; } = "";   // "FT_STANDARD"/"FT_WILD"

    // CREATE_GAME 块中获取的 Lo（不随 Game 对象重置）
    public ulong AccountIdLo { get; set; }
    public ulong OpponentAccountIdLo { get; set; }

    /// <summary>创建新局的初始状态</summary>
    public static GameState CreateInitial()
    {
        return new GameState
        {
            InCreateBlock = false,
            CreateHasTurn = false,
            PendingNewGame = null,
            LoFetched = false,
            ReachedStep13 = false,
            ConcedePending = false,
            ConcedeTag = "",
            IsScanning = false,
            IsConstructed = false,
        };
    }
}

}
