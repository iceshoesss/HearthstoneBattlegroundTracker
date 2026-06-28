using System.Collections.Generic;

namespace HBTCombat
{
    /// <summary>
    /// 战斗模拟输入
    /// </summary>
    public class SimulationInput
    {
        /// <summary>己方随从列表</summary>
        public List<Minion> PlayerBoard { get; set; } = new List<Minion>();

        /// <summary>对方随从列表</summary>
        public List<Minion> OpponentBoard { get; set; } = new List<Minion>();

        /// <summary>己方英雄血量</summary>
        public int PlayerHealth { get; set; }

        /// <summary>对方英雄血量</summary>
        public int OpponentHealth { get; set; }

        /// <summary>己方酒馆等级</summary>
        public int PlayerTier { get; set; }

        /// <summary>对方酒馆等级</summary>
        public int OpponentTier { get; set; }

        /// <summary>当前回合数</summary>
        public int Turn { get; set; }

        /// <summary>伤害上限（0=无上限）</summary>
        public int DamageCap { get; set; }

        /// <summary>可用种族</summary>
        public HashSet<string> AvailableRaces { get; set; } = new HashSet<string>();
    }
}
