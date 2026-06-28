namespace HBTCombat
{
    /// <summary>
    /// 模拟结果
    /// </summary>
    public class SimulationOutput
    {
        /// <summary>己方胜率</summary>
        public float WinRate { get; set; }

        /// <summary>平局率</summary>
        public float TieRate { get; set; }

        /// <summary>己方败率</summary>
        public float LossRate { get; set; }

        /// <summary>模拟次数</summary>
        public int SimulationCount { get; set; }

        // === 获胜时造成的伤害范围 ===
        public int PlayerDamageMin { get; set; }
        public int PlayerDamageMax { get; set; }
        public float PlayerDamageAvg { get; set; }

        // === 失败时受到的伤害范围 ===
        public int OpponentDamageMin { get; set; }
        public int OpponentDamageMax { get; set; }
        public float OpponentDamageAvg { get; set; }

        // === 致胜概率 ===
        /// <summary>己方全灭对方的概率</summary>
        public float PlayerLethalRate { get; set; }

        /// <summary>对方全灭己方的概率</summary>
        public float OpponentLethalRate { get; set; }
    }
}
