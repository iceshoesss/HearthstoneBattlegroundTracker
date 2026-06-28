using System.Collections.Generic;

namespace HBTCombat
{
    /// <summary>
    /// 战斗状态 - 传递给触发器接口的上下文
    /// </summary>
    public class CombatState
    {
        /// <summary>己方随从列表</summary>
        public List<Minion> PlayerBoard { get; set; } = new List<Minion>();

        /// <summary>对方随从列表</summary>
        public List<Minion> OpponentBoard { get; set; } = new List<Minion>();

        /// <summary>当前是否为己方回合</summary>
        public bool IsPlayerTurn { get; set; }

        /// <summary>当前回合数</summary>
        public int RoundNumber { get; set; }

        /// <summary>可用种族</summary>
        public HashSet<string> AvailableRaces { get; set; } = new HashSet<string>();

        /// <summary>
        /// 获取指定随从所在阵营的随从列表
        /// </summary>
        public List<Minion> GetFriendlyBoard(Minion minion)
        {
            return minion.ControlledByPlayer ? PlayerBoard : OpponentBoard;
        }

        /// <summary>
        /// 获取指定随从的敌方随从列表
        /// </summary>
        public List<Minion> GetEnemyBoard(Minion minion)
        {
            return minion.ControlledByPlayer ? OpponentBoard : PlayerBoard;
        }
    }
}
