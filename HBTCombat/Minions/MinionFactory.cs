using System;
using System.Collections.Generic;
using BattlegroundDB;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 随从工厂 - 从 BattlegroundDB 创建 Minion 对象
    /// 参考 BobsBuddy.MinionFactory 设计
    /// </summary>
    public class MinionFactory
    {
        // === 已知的 Cleave 随从 ===
        public static readonly HashSet<string> CardIdsWithCleave = new HashSet<string>
        {
            "BG21_000", // Cave Hydra
            "BG23_000", // Frenzied Lefthander (placeholder)
            "BG26_000", // Tidal Empress (placeholder)
            "BG28_000", // Djinn-Bound Scarab (placeholder)
        };

        // === 已知的超级风怒随从 ===
        public static readonly HashSet<string> CardIdsWithMegaWindfury = new HashSet<string>();

        // === 特殊行为注册表 ===
        private static readonly Dictionary<string, Func<Minion, bool, List<IOnStartOfCombat>>> StartOfCombatRegistry
            = new Dictionary<string, Func<Minion, bool, List<IOnStartOfCombat>>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IDeathrattle>>> DeathrattleRegistry
            = new Dictionary<string, Func<Minion, bool, List<IDeathrattle>>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IOnAfterAttack>>> AfterAttackRegistry
            = new Dictionary<string, Func<Minion, bool, List<IOnAfterAttack>>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IOnFriendlyMinionDied>>> FriendlyMinionDiedRegistry
            = new Dictionary<string, Func<Minion, bool, List<IOnFriendlyMinionDied>>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IOnFriendlyMinionSummoned>>> FriendlyMinionSummonedRegistry
            = new Dictionary<string, Func<Minion, bool, List<IOnFriendlyMinionSummoned>>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IAvenge>>> AvengeRegistry
            = new Dictionary<string, Func<Minion, bool, List<IAvenge>>>();

        private static readonly Dictionary<string, Func<Minion, bool, IPassiveAttackBonus>> PassiveAttackBonusRegistry
            = new Dictionary<string, Func<Minion, bool, IPassiveAttackBonus>>();

        private static readonly Dictionary<string, Func<Minion, bool, IPassiveHealthBonus>> PassiveHealthBonusRegistry
            = new Dictionary<string, Func<Minion, bool, IPassiveHealthBonus>>();

        private static readonly Dictionary<string, Func<Minion, bool, IRebornBehavior>> RebornBehaviorRegistry
            = new Dictionary<string, Func<Minion, bool, IRebornBehavior>>();

        private static readonly Dictionary<string, Func<Minion, bool, List<IDeathrattleEffect>>> DeathrattleEffectRegistry
            = new Dictionary<string, Func<Minion, bool, List<IDeathrattleEffect>>>();

        // === 注册行为的方法 ===

        public static void RegisterStartOfCombat(string cardId, Func<Minion, bool, List<IOnStartOfCombat>> factory)
        {
            StartOfCombatRegistry[cardId] = factory;
        }

        public static void RegisterDeathrattle(string cardId, Func<Minion, bool, List<IDeathrattle>> factory)
        {
            DeathrattleRegistry[cardId] = factory;
        }

        public static void RegisterAfterAttack(string cardId, Func<Minion, bool, List<IOnAfterAttack>> factory)
        {
            AfterAttackRegistry[cardId] = factory;
        }

        public static void RegisterFriendlyMinionDied(string cardId, Func<Minion, bool, List<IOnFriendlyMinionDied>> factory)
        {
            FriendlyMinionDiedRegistry[cardId] = factory;
        }

        public static void RegisterFriendlyMinionSummoned(string cardId, Func<Minion, bool, List<IOnFriendlyMinionSummoned>> factory)
        {
            FriendlyMinionSummonedRegistry[cardId] = factory;
        }

        public static void RegisterAvenge(string cardId, Func<Minion, bool, List<IAvenge>> factory)
        {
            AvengeRegistry[cardId] = factory;
        }

        public static void RegisterPassiveAttackBonus(string cardId, Func<Minion, bool, IPassiveAttackBonus> factory)
        {
            PassiveAttackBonusRegistry[cardId] = factory;
        }

        public static void RegisterPassiveHealthBonus(string cardId, Func<Minion, bool, IPassiveHealthBonus> factory)
        {
            PassiveHealthBonusRegistry[cardId] = factory;
        }

        public static void RegisterRebornBehavior(string cardId, Func<Minion, bool, IRebornBehavior> factory)
        {
            RebornBehaviorRegistry[cardId] = factory;
        }

        public static void RegisterDeathrattleEffect(string cardId, Func<Minion, bool, List<IDeathrattleEffect>> factory)
        {
            DeathrattleEffectRegistry[cardId] = factory;
        }

        /// <summary>
        /// 从 CardId 创建 Minion（使用 BattlegroundDB 数据）
        /// </summary>
        public Minion CreateFromCardId(string cardId, bool controlledByPlayer)
        {
            var card = Cards.GetByCardId(cardId);
            if (card == null)
            {
                // 未知卡牌，创建最小化 Minion
                return new Minion
                {
                    CardId = cardId,
                    Name = cardId,
                    ControlledByPlayer = controlledByPlayer,
                };
            }

            var minion = CreateFromCard(card, controlledByPlayer);
            AttachBehaviors(minion, card, controlledByPlayer);
            return minion;
        }

        /// <summary>
        /// 从 CardId 创建 Minion（使用 BattlegroundDB 数据），同时设置当前面板状态
        /// </summary>
        public Minion CreateFromCardId(string cardId, bool controlledByPlayer,
            int currentAttack, int currentHealth, int maxHealth,
            bool taunt, bool divineShield, bool poisonous, bool venomous,
            bool windfury, bool megaWindfury, bool stealth, bool reborn,
            bool golden, int tier, int scriptDataNum1 = 0, int scriptDataNum2 = 0)
        {
            var card = Cards.GetByCardId(cardId);
            var minion = card != null
                ? CreateFromCard(card, controlledByPlayer)
                : new Minion { CardId = cardId, Name = cardId, ControlledByPlayer = controlledByPlayer };

            // 覆盖为实际面板状态
            minion.BaseAttack = currentAttack;
            minion.BaseHealth = maxHealth;
            minion.MaxHealth = maxHealth;
            minion.CurrentHealth = currentHealth;
            minion.Taunt = taunt;
            minion.DivineShield = divineShield;
            minion.Poisonous = poisonous;
            minion.Venomous = venomous;
            minion.Windfury = windfury;
            minion.MegaWindfury = megaWindfury;
            minion.Stealth = stealth;
            minion.Reborn = reborn;
            minion.Golden = golden;
            minion.Tier = tier;
            minion.ScriptDataNum1 = scriptDataNum1;
            minion.ScriptDataNum2 = scriptDataNum2;

            if (card != null)
                AttachBehaviors(minion, card, controlledByPlayer);

            return minion;
        }

        /// <summary>
        /// 从 BgdbCard 创建基础 Minion
        /// </summary>
        private Minion CreateFromCard(BgdbCard card, bool controlledByPlayer)
        {
            int attack = card.Attack;
            int health = card.Health;

            return new Minion
            {
                CardId = card.CardId,
                Name = card.NameZh ?? card.Name,
                BaseAttack = attack,
                BaseHealth = health,
                MaxHealth = health,
                CurrentHealth = health,
                Tier = card.Tier ?? 1,
                PrimaryRace = card.MinionType,
                ControlledByPlayer = controlledByPlayer,
                Taunt = card.HasKeyword("Taunt"),
                DivineShield = card.HasKeyword("Divine Shield"),
                Poisonous = card.HasKeyword("Poisonous"),
                Venomous = card.HasKeyword("Venomous"),
                Windfury = card.HasKeyword("Windfury"),
                MegaWindfury = card.HasKeyword("Mega-Windfury"),
                Reborn = card.HasKeyword("Reborn"),
                Cleave = CardIdsWithCleave.Contains(card.CardId),
            };
        }

        /// <summary>
        /// 附加行为接口到 Minion
        /// </summary>
        private void AttachBehaviors(Minion minion, BgdbCard card, bool controlledByPlayer)
        {
            string cardId = card.CardId;

            // 从注册表附加行为
            if (StartOfCombatRegistry.TryGetValue(cardId, out var socFactory))
                minion.StartOfCombatTriggers = socFactory(minion, controlledByPlayer);

            if (DeathrattleRegistry.TryGetValue(cardId, out var drFactory))
                minion.Deathrattles = drFactory(minion, controlledByPlayer);

            if (AfterAttackRegistry.TryGetValue(cardId, out var aaFactory))
                minion.AfterAttackTriggers = aaFactory(minion, controlledByPlayer);

            if (FriendlyMinionDiedRegistry.TryGetValue(cardId, out var fmdFactory))
                minion.FriendlyMinionDiedTriggers = fmdFactory(minion, controlledByPlayer);

            if (FriendlyMinionSummonedRegistry.TryGetValue(cardId, out var fmsFactory))
                minion.FriendlyMinionSummonedTriggers = fmsFactory(minion, controlledByPlayer);

            if (AvengeRegistry.TryGetValue(cardId, out var aFactory))
                minion.AvengeTriggers = aFactory(minion, controlledByPlayer);

            if (PassiveAttackBonusRegistry.TryGetValue(cardId, out var pabFactory))
                minion.PassiveAttackBonuses.Add(pabFactory(minion, controlledByPlayer));

            if (PassiveHealthBonusRegistry.TryGetValue(cardId, out var phbFactory))
                minion.PassiveHealthBonuses.Add(phbFactory(minion, controlledByPlayer));

            if (RebornBehaviorRegistry.TryGetValue(cardId, out var rbFactory))
                minion.RebornBehavior = rbFactory(minion, controlledByPlayer);

            if (DeathrattleEffectRegistry.TryGetValue(cardId, out var deFactory))
                minion.DeathrattleEffects = deFactory(minion, controlledByPlayer);

            // 数据驱动行为：从 BattlegroundDB 的 Keywords 推断基础亡语
            if (card.HasKeyword("Deathrattle") && minion.Deathrattles.Count == 0)
            {
                var dataDrivenDeathrattle = DataDrivenBehaviors.CreateDeathrattle(cardId, controlledByPlayer);
                if (dataDrivenDeathrattle != null)
                    minion.Deathrattles.Add(dataDrivenDeathrattle);
            }
        }

        /// <summary>
        /// 从附魔列表创建额外的亡语/触发器
        /// </summary>
        public void AttachEnchantments(Minion minion, List<EnchantmentInfo> enchantments)
        {
            if (enchantments == null) return;

            foreach (var ench in enchantments)
            {
                switch (ench.CardId)
                {
                    // 磁力附魔
                    case "BG21_030e": // Replicating Menace normal
                        minion.Deathrattles.Add(new GenericDeathrattle(
                            golden: false,
                            summonCardIds: new List<string> { "BG21_030t", "BG21_030t", "BG21_030t" }
                        ));
                        break;
                    case "BG21_030_Ge": // Replicating Menace golden
                        minion.Deathrattles.Add(new GenericDeathrattle(
                            golden: true,
                            summonCardIds: new List<string> { "BG21_030t", "BG21_030t", "BG21_030t", "BG21_030t", "BG21_030t", "BG21_030t" }
                        ));
                        break;
                    // 其他附魔可以在这里添加
                }
            }
        }
    }

    /// <summary>
    /// 附魔信息（从游戏状态读取）
    /// </summary>
    public class EnchantmentInfo
    {
        public string CardId { get; set; }
        public int ScriptDataNum1 { get; set; }
        public int ScriptDataNum2 { get; set; }
    }
}
