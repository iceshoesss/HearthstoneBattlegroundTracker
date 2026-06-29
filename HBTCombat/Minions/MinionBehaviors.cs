using System;
using System.Collections.Generic;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 随从行为注册中心 - 全面覆盖版
    /// 参考 BobBuddy 实现，覆盖所有当前在池的随从
    /// </summary>
    public static class MinionBehaviors
    {
        /// <summary>
        /// 注册所有已知的随从行为
        /// </summary>
        public static void RegisterAll()
        {
            RegisterDeathrattleEffects();
            RegisterOnFriendlyMinionDied();
            RegisterStartOfCombat();
            RegisterAfterAttack();
            RegisterOnFriendlyMinionSummoned();
            RegisterAvenge();
            RegisterPassiveBonuses();
            RegisterSpecialDeathrattles();
            RegisterTimewarpedMinions();
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 1 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier1()
        {
            // Sneed's New Shredder (T1): summons highest-health minion from hand with Divine Shield
            MinionFactory.RegisterDeathrattle("BG21_HERO_030t", (m, p) => new List<IDeathrattle>
            {
                new SneedsNewShredderDeathrattle()
            });

            // Twilight Hatchling (T1): summons a 3/3 that attacks immediately
            MinionFactory.RegisterDeathrattle("BG34_630", (m, p) => new List<IDeathrattle>
            {
                new TwilightHatchlingDeathrattle()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 2 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier2()
        {
            // Alert Alarmist (T2): Deathrattle: Summon a 2/2 Mech with Taunt
            MinionFactory.RegisterDeathrattle("BG35_340", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG35_340t" }, 1, 2)
            });

            // Baneling (T2): deals damage equal to its attack to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG31_HERO_811t5", (m, p) => new List<IDeathrattleEffect>
            {
                new BanelingDeathrattleEffect()
            });

            // Scarlet Skull (T2): +1/+2 to a friendly Undead
            MinionFactory.RegisterDeathrattleEffect("BG25_022", (m, p) => new List<IDeathrattleEffect>
            {
                new ScarletSkullDeathrattleEffect()
            });

            // Coldlight Diver (T2): Deathrattle: Give a friendly Murloc +2/+2
            MinionFactory.RegisterDeathrattleEffect("BG33_894", (m, p) => new List<IDeathrattleEffect>
            {
                new ColdlightDiverDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 3 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier3()
        {
            // Glowing Cinder (T3): Deathrattle: Deal 2 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG32_842", (m, p) => new List<IDeathrattleEffect>
            {
                new GlowingCinderDeathrattleEffect()
            });

            // Mummifier (T3): Deathrattle: Give a different friendly Undead Reborn
            MinionFactory.RegisterDeathrattleEffect("BG28_309", (m, p) => new List<IDeathrattleEffect>
            {
                new MummifierDeathrattleEffect()
            });

            // Prickly Piper (T3): Deathrattle: Deal 1 damage to all enemies
            MinionFactory.RegisterDeathrattleEffect("BG26_160", (m, p) => new List<IDeathrattleEffect>
            {
                new PricklyPiperDeathrattleEffect()
            });

            // Scourfin (T3): Deathrattle: Deal 4 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG26_360", (m, p) => new List<IDeathrattleEffect>
            {
                new ScourfinDeathrattleEffect()
            });

            // Sly Raptor (T3): summons a random beast with stats set to 6/6
            MinionFactory.RegisterDeathrattle("BG25_806", (m, p) => new List<IDeathrattle>
            {
                new SlyRaptorDeathrattle()
            });

            // Handless Forsaken (T3): summons a 2/1 with Reborn
            MinionFactory.RegisterDeathrattle("BG25_010", (m, p) => new List<IDeathrattle>
            {
                new HandlessForsakenDeathrattle()
            });

            // Waveling (T3): Deathrattle: Deal 1 damage to all minions
            MinionFactory.RegisterDeathrattleEffect("BG34_856", (m, p) => new List<IDeathrattleEffect>
            {
                new WavelingDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 4 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier4()
        {
            // Friendly Geist (T4): Deathrattle: Deal 3 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG32_880", (m, p) => new List<IDeathrattleEffect>
            {
                new FriendlyGeistDeathrattleEffect()
            });

            // Plaguerunner (T4): +1/+1 for each minion that died this combat
            MinionFactory.RegisterDeathrattleEffect("BG34_690", (m, p) => new List<IDeathrattleEffect>
            {
                new PlaguerunnerDeathrattleEffect()
            });

            // Silent Enforcer (T4): deals 2 damage to all non-demon minions
            MinionFactory.RegisterDeathrattleEffect("BG33_156", (m, p) => new List<IDeathrattleEffect>
            {
                new SilentEnforcerDeathrattleEffect()
            });

            // Tunnel Blaster (T4): deals 3 damage to all minions
            MinionFactory.RegisterDeathrattleEffect("BG_DAL_775", (m, p) => new List<IDeathrattleEffect>
            {
                new TunnelBlasterDeathrattleEffect()
            });

            // Leyline Surfacer (T4): Deathrattle: Give a friendly Elemental +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG35_881", (m, p) => new List<IDeathrattleEffect>
            {
                new LeylineSurfacerDeathrattleEffect()
            });

            // Shifty Snake (T4): Deathrattle: Deal 3 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BGDUO31_203", (m, p) => new List<IDeathrattleEffect>
            {
                new ShiftySnakeDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 5 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier5()
        {
            // Kangor's Apprentice (T5): summons copies of the first 2 mechs that died
            MinionFactory.RegisterDeathrattle("BGS_012", (m, p) => new List<IDeathrattle>
            {
                new KangorsApprenticeDeathrattle()
            });

            // Leeroy the Reckless (T5): destroys the minion that killed it
            MinionFactory.RegisterDeathrattleEffect("BG23_318", (m, p) => new List<IDeathrattleEffect>
            {
                new LeeroyDeathrattleEffect()
            });

            // Spiked Savior (T5): +1 health to all friendly minions AND deals 1 damage to each
            MinionFactory.RegisterDeathrattleEffect("BG29_808", (m, p) => new List<IDeathrattleEffect>
            {
                new SpikedSaviorDeathrattleEffect()
            });

            // Barrens Conjurer (T5): Deathrattle: Summon a copy of this minion
            MinionFactory.RegisterDeathrattle("BG29_862", (m, p) => new List<IDeathrattle>
            {
                new BarrensConjurerDeathrattle()
            });

            // Dancing Barnstormer (T5): Deathrattle: Give your Beasts +3/+3
            MinionFactory.RegisterDeathrattleEffect("BG26_162", (m, p) => new List<IDeathrattleEffect>
            {
                new DancingBarnstormerDeathrattleEffect()
            });

            // Draconic Warden (T5): Deathrattle: Summon a 8/8 Dragon
            MinionFactory.RegisterDeathrattle("BG34_633", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_633t" }, 1, 2)
            });

            // Ingenious Inventor (T5): +1/+1 for each friendly minion that died
            MinionFactory.RegisterDeathrattleEffect("BG35_890", (m, p) => new List<IDeathrattleEffect>
            {
                new IngeniousInventorDeathrattleEffect()
            });

            // Nightmare Par-tea Guest (T5): Deathrattle: Summon a 5/5 Nightmare
            MinionFactory.RegisterDeathrattle("BG32_111", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG32_111t" }, 1, 2)
            });

            // Scrap Scraper (T5): Deathrattle: Give a friendly Mech +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG26_148", (m, p) => new List<IDeathrattleEffect>
            {
                new ScrapScraperDeathrattleEffect()
            });

            // Sewer Lord (T5): Deathrattle: Summon two 2/3 Rats with Taunt
            MinionFactory.RegisterDeathrattle("BG35_604", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG35_604t" }, 2, 4)
            });

            // Shadowdancer (T5): Deathrattle: Deal 4 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG32_891", (m, p) => new List<IDeathrattleEffect>
            {
                new ShadowdancerDeathrattleEffect()
            });

            // Shipwrecked Rascal (T5): Deathrattle: Summon a 6/6 Pirate
            MinionFactory.RegisterDeathrattle("BG33_821", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG33_821t" }, 1, 2)
            });

            // Three Lil' Quilboar (T5): Deathrattle: Summon three 3/3 Quilboar
            MinionFactory.RegisterDeathrattle("BG26_867", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG26_867t", "BG26_867t2", "BG26_867t3" }, 1, 2)
            });

            // Turquoise Skitterer (T5): Deathrattle: Summon three 1/1 Beasts
            MinionFactory.RegisterDeathrattle("BG31_809", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG31_809t" }, 3, 6)
            });

            // Twilight Broodmother (T5): Deathrattle: Summon two 3/3 Dragons with Taunt
            MinionFactory.RegisterDeathrattle("BG34_731", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_731t" }, 2, 4)
            });

            // Wintergrasp Ghoul (T5): Deathrattle: Give your Undead +2/+2
            MinionFactory.RegisterDeathrattleEffect("BG34_694", (m, p) => new List<IDeathrattleEffect>
            {
                new WintergraspGhoulDeathrattleEffect()
            });

            // Magnanimoose (T5): Deathrattle: Summon a copy of a friendly minion with 1 Health
            MinionFactory.RegisterDeathrattle("BGDUO_105", (m, p) => new List<IDeathrattle>
            {
                new MagnanimooseDeathrattle()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 6 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier6()
        {
            // Deathly Striker (T6): Deathrattle: Summon a 8/8 Undead
            MinionFactory.RegisterDeathrattle("BG31_835", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG31_835t" }, 1, 2)
            });

            // Eternal Summoner (T6): Deathrattle: Summon two 5/5 Undead with Reborn
            MinionFactory.RegisterDeathrattle("BG25_009", (m, p) => new List<IDeathrattle>
            {
                new EternalSummonerDeathrattle()
            });

            // Goldrinn, the Great Wolf (T6): +8/+8 to all friendly Beasts
            MinionFactory.RegisterDeathrattleEffect("BGS_018", (m, p) => new List<IDeathrattleEffect>
            {
                new GoldrinnDeathrattleEffect()
            });

            // Ruthless Queensguard (T6): Deathrattle: Summon two 4/4 Dragons
            MinionFactory.RegisterDeathrattle("BG34_926", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_926t" }, 2, 4)
            });

            // Silky Shimmermoth (T6): Deathrattle: Give your Beasts +5/+5
            MinionFactory.RegisterDeathrattleEffect("BG32_204", (m, p) => new List<IDeathrattleEffect>
            {
                new SilkyShimmermothDeathrattleEffect()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Tier 7 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTier7()
        {
            // Champion of Sargeras (T7): Deathrattle: Summon two 6/6 Demons
            MinionFactory.RegisterDeathrattle("BG27_016", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG27_016t" }, 2, 4)
            });

            // Highkeeper Ra (T7): Deathrattle: Deal 8 damage to all enemies
            MinionFactory.RegisterDeathrattleEffect("BG34_319", (m, p) => new List<IDeathrattleEffect>
            {
                new HighkeeperRaDeathrattleEffect()
            });

            // Sanguine Champion (T7): Deathrattle: Give your Quilboar +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG23_017", (m, p) => new List<IDeathrattleEffect>
            {
                new SanguineChampionDeathrattleEffect()
            });

            // Stitched Salvager (T7): Deathrattle: Destroy leftmost friendly, summon copy
            MinionFactory.RegisterDeathrattle("BG31_999", (m, p) => new List<IDeathrattle>
            {
                new StitchedSalvagerDeathrattle()
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Timewarped 随从
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterTimewarpedMinions()
        {
            // Timewarped Leapfrogger (T3): +1/+1 to a friendly Beast AND passes deathrattle
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_031", (m, p) => new List<IDeathrattleEffect>
            {
                new LeapfroggerDeathrattleEffect()
            });

            // Timewarped Pillager (T3): Deal 2 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_204", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedPillagerDeathrattleEffect()
            });

            // Timewarped Sapper (T3): Deal 3 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_304", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedSapperDeathrattleEffect()
            });

            // Timewarped Sporebat (T3): Give a friendly minion +2/+2
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_582", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedSporebatDeathrattleEffect()
            });

            // Timewarped Festergut (T3): Deal 1 damage to all enemies
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_590", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedFestergutDeathrattleEffect()
            });

            // Timewarped Kil'rek (T3): Give a friendly Demon +3/+3
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_584", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedKilrekDeathrattleEffect()
            });

            // Timewarped Bassgill (T3): Summon a 4/4 Murloc
            MinionFactory.RegisterDeathrattle("BG34_Giant_071", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_071t" }, 1, 2)
            });

            // Timewarped Busker (T3): Give a friendly Pirate +2/+2
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_001", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedBuskerDeathrattleEffect()
            });

            // Timewarped Scourfin (T3): Deal 4 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_017", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedScourfinDeathrattleEffect()
            });

            // Timewarped Jazzer (T3): Give a friendly Quilboar +2/+2
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_306", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedJazzerDeathrattleEffect()
            });

            // Timewarped Thorncaller (T3): Summon two 1/1 Quilboar
            MinionFactory.RegisterDeathrattle("BG34_Giant_078", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_078t" }, 2, 4)
            });

            // Timewarped Warghoul (T5): Trigger adjacent minion's Deathrattle
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_331", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedWarghoulDeathrattleEffect()
            });

            // Timewarped Radio Star (T5): Copy the enemy minion that killed it
            MinionFactory.RegisterDeathrattle("BG34_Giant_330", (m, p) => new List<IDeathrattle>
            {
                new TimewarpedRadioStarDeathrattle()
            });

            // Timewarped Geist (T5): Deal 3 damage to a random enemy
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_034", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedGeistDeathrattleEffect()
            });

            // Timewarped Caretaker (T5): Summon a 5/5 Undead
            MinionFactory.RegisterDeathrattle("BG34_Giant_618", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_618t" }, 1, 2)
            });

            // Timewarped Icky Imp (T5): Summon three 1/1 Imps
            MinionFactory.RegisterDeathrattle("BG34_Giant_674", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_674t" }, 3, 6)
            });

            // Timewarped Lil' Quilboar (T5): Summon three 3/3 Quilboar
            MinionFactory.RegisterDeathrattle("BG34_Giant_608", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_608t", "BG34_Giant_608t2", "BG34_Giant_608t3" }, 1, 2)
            });

            // Timewarped Nest Swarmer (T5): Summon three 1/1 Beasts
            MinionFactory.RegisterDeathrattle("BG34_Giant_687", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_687t" }, 3, 6)
            });

            // Timewarped Stormcloud (T5): Deal 2 damage to all enemies
            MinionFactory.RegisterDeathrattleEffect("BG34_PreMadeChamp_031", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedStormcloudDeathrattleEffect()
            });

            // Timewarped Calligrapher (T5): Give all friendly minions +1/+1
            MinionFactory.RegisterDeathrattleEffect("BG34_PreMadeChamp_091", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedCalligrapherDeathrattleEffect()
            });

            // Timewarped Plunderer (T5): Give a friendly Pirate +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG34_PreMadeChamp_067", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedPlundererDeathrattleEffect()
            });

            // Timewarped Riplash (T5): Give a friendly Naga +4/+4
            MinionFactory.RegisterDeathrattleEffect("BG34_Giant_325", (m, p) => new List<IDeathrattleEffect>
            {
                new TimewarpedRiplashDeathrattleEffect()
            });

            // Timewarped Tide Razor (T5): Summon three 3/3 Pirates
            MinionFactory.RegisterDeathrattle("BG34_Giant_328", (m, p) => new List<IDeathrattle>
            {
                new GenericDeathrattle(m.Golden, new List<string> { "BG34_Giant_328t" }, 3, 6)
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 其他注册方法
        // ═══════════════════════════════════════════════════════════════

        private static void RegisterDeathrattleEffects()
        {
            RegisterTier1();
            RegisterTier2();
            RegisterTier3();
            RegisterTier4();
            RegisterTier5();
            RegisterTier6();
            RegisterTier7();
        }

        private static void RegisterOnFriendlyMinionDied()
        {
            // Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
            MinionFactory.RegisterFriendlyMinionDied("BG21_044", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new ScavengingHyenaTrigger()
            });

            // Junkbot: 当友方机械死亡时获得 +2/+2
            MinionFactory.RegisterFriendlyMinionDied("BG21_045", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new JunkbotTrigger()
            });

            // Flesheating Ghoul: 当任意随从死亡时获得 +1 攻击
            MinionFactory.RegisterFriendlyMinionDied("BG21_046", (m, p) => new List<IOnFriendlyMinionDied>
            {
                new FlesheatingGhoulTrigger()
            });
        }

        private static void RegisterStartOfCombat()
        {
            // Red Whelp: 战斗开始时每有一条龙造成 1 伤害
            MinionFactory.RegisterStartOfCombat("BG21_050", (m, p) => new List<IOnStartOfCombat>
            {
                new RedWhelpTrigger()
            });

            // Spirit of Air: gives a random friendly minion Windfury, Divine Shield, and Taunt
            MinionFactory.RegisterStartOfCombat("TB_BaconShop_HERO_76_Buddy", (m, p) => new List<IOnStartOfCombat>
            {
                new SpiritOfAirTrigger()
            });
        }

        private static void RegisterAfterAttack()
        {
            // Monstrous Macaw: 攻击后触发友方随从的亡语
            MinionFactory.RegisterAfterAttack("BG21_060", (m, p) => new List<IOnAfterAttack>
            {
                new MonstrousMacawTrigger()
            });
        }

        private static void RegisterOnFriendlyMinionSummoned()
        {
            // Mama Bear: 召唤野兽时给予 +4/+4
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_061", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new MamaBearTrigger()
            });

            // Pack Leader: 召唤野兽时给予 +3 攻击
            MinionFactory.RegisterFriendlyMinionSummoned("BG21_062", (m, p) => new List<IOnFriendlyMinionSummoned>
            {
                new PackLeaderTrigger()
            });
        }

        private static void RegisterAvenge() { }

        private static void RegisterPassiveBonuses()
        {
            // Mal'Ganis: 友方恶魔获得 +2/+2
            MinionFactory.RegisterPassiveAttackBonus("BG21_063", (m, p) => new MalGanisAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_063", (m, p) => new MalGanisHealthBonus());

            // Kalecgos: 友方龙获得 +1/+1
            MinionFactory.RegisterPassiveAttackBonus("BG21_064", (m, p) => new KalecgosAttackBonus());
            MinionFactory.RegisterPassiveHealthBonus("BG21_064", (m, p) => new KalecgosHealthBonus());
        }

        private static void RegisterSpecialDeathrattles() { }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 亡语效果实现 - 按功能分类
    // ═══════════════════════════════════════════════════════════════════════

    // ── 伤害类亡语 ──

    public class BanelingDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? source.BaseAttack * 2 : source.BaseAttack;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class GlowingCinderDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 4 : 2;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class PricklyPiperDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 2 : 1;
            foreach (var m in state.GetEnemyBoard(source)) m.TakeDamage(damage);
        }
    }

    public class ScourfinDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 8 : 4;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class WavelingDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 2 : 1;
            foreach (var m in state.PlayerBoard) m.TakeDamage(damage);
            foreach (var m in state.OpponentBoard) m.TakeDamage(damage);
        }
    }

    public class FriendlyGeistDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class ShadowdancerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 8 : 4;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class ShiftySnakeDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class HighkeeperRaDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 16 : 8;
            foreach (var m in state.GetEnemyBoard(source)) m.TakeDamage(damage);
        }
    }

    public class TimewarpedPillagerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 4 : 2;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class TimewarpedSapperDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class TimewarpedScourfinDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 8 : 4;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class TimewarpedFestergutDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 2 : 1;
            foreach (var m in state.GetEnemyBoard(source)) m.TakeDamage(damage);
        }
    }

    public class TimewarpedGeistDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class TimewarpedStormcloudDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 4 : 2;
            foreach (var m in state.GetEnemyBoard(source)) m.TakeDamage(damage);
        }
    }

    // ── Buff 类亡语 ──

    public class GoldrinnDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 16 : 8;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                if (m.PrimaryRace == "Beast")
                {
                    m.BaseAttack += bonus;
                    m.MaxHealth += bonus;
                    m.CurrentHealth += bonus;
                }
            }
        }
    }

    public class ScarletSkullDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int atk = source.Golden ? 2 : 1;
            int hp = source.Golden ? 4 : 2;
            var undead = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Undead" && m != source) undead.Add(m);
            if (undead.Count > 0)
            {
                var t = undead[Rng.Next(undead.Count)];
                t.BaseAttack += atk; t.MaxHealth += hp; t.CurrentHealth += hp;
            }
        }
    }

    public class ColdlightDiverDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 4 : 2;
            var murlocs = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Murloc" && m != source) murlocs.Add(m);
            if (murlocs.Count > 0)
            {
                var t = murlocs[Rng.Next(murlocs.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class MummifierDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            var undead = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Undead" && m != source && !m.Reborn) undead.Add(m);
            if (undead.Count > 0) undead[Rng.Next(undead.Count)].Reborn = true;
        }
    }

    public class LeylineSurfacerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 8 : 4;
            var elementals = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Elemental" && m != source) elementals.Add(m);
            if (elementals.Count > 0)
            {
                var t = elementals[Rng.Next(elementals.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class ScrapScraperDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 8 : 4;
            var mechs = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Mech" && m != source) mechs.Add(m);
            if (mechs.Count > 0)
            {
                var t = mechs[Rng.Next(mechs.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class DancingBarnstormerDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 6 : 3;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                if (m.PrimaryRace == "Beast")
                {
                    m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
                }
            }
        }
    }

    public class SilkyShimmermothDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 10 : 5;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                if (m.PrimaryRace == "Beast")
                {
                    m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
                }
            }
        }
    }

    public class WintergraspGhoulDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 4 : 2;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                if (m.PrimaryRace == "Undead")
                {
                    m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
                }
            }
        }
    }

    public class SanguineChampionDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 8 : 4;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                if (m.PrimaryRace == "Quilboar")
                {
                    m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
                }
            }
        }
    }

    public class TimewarpedSporebatDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 4 : 2;
            var friendly = state.GetFriendlyBoard(source);
            var candidates = new List<Minion>();
            foreach (var m in friendly) if (m != source) candidates.Add(m);
            if (candidates.Count > 0)
            {
                var t = candidates[Rng.Next(candidates.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedKilrekDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 6 : 3;
            var demons = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Demon" && m != source) demons.Add(m);
            if (demons.Count > 0)
            {
                var t = demons[Rng.Next(demons.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedBuskerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 4 : 2;
            var pirates = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Pirate" && m != source) pirates.Add(m);
            if (pirates.Count > 0)
            {
                var t = pirates[Rng.Next(pirates.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedJazzerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 4 : 2;
            var quilboar = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Quilboar" && m != source) quilboar.Add(m);
            if (quilboar.Count > 0)
            {
                var t = quilboar[Rng.Next(quilboar.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedPlundererDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 8 : 4;
            var pirates = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Pirate" && m != source) pirates.Add(m);
            if (pirates.Count > 0)
            {
                var t = pirates[Rng.Next(pirates.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedRiplashDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 8 : 4;
            var naga = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Naga" && m != source) naga.Add(m);
            if (naga.Count > 0)
            {
                var t = naga[Rng.Next(naga.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
            }
        }
    }

    public class TimewarpedCalligrapherDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 2 : 1;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
            }
        }
    }

    public class PlaguerunnerDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int deaths = source.ScriptDataNum1;
            int bonus = source.Golden ? deaths * 2 : deaths;
            var friendly = state.GetFriendlyBoard(source);
            foreach (var m in friendly)
            {
                m.BaseAttack += bonus; m.MaxHealth += bonus; m.CurrentHealth += bonus;
            }
        }
    }

    public class IngeniousInventorDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int deaths = source.ScriptDataNum1;
            int bonus = source.Golden ? deaths * 2 : deaths;
            source.BaseAttack += bonus; source.MaxHealth += bonus; source.CurrentHealth += bonus;
        }
    }

    public class TunnelBlasterDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 6 : 3;
            foreach (var m in state.PlayerBoard) m.TakeDamage(damage);
            foreach (var m in state.OpponentBoard) m.TakeDamage(damage);
        }
    }

    public class SilentEnforcerDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int damage = source.Golden ? 4 : 2;
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace != "Demon") m.TakeDamage(damage);
            foreach (var m in state.GetEnemyBoard(source)) m.TakeDamage(damage);
        }
    }

    public class SpikedSaviorDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            int hpBonus = source.Golden ? 2 : 1;
            int damage = source.Golden ? 2 : 1;
            foreach (var m in state.GetFriendlyBoard(source))
            {
                m.MaxHealth += hpBonus; m.CurrentHealth += hpBonus;
                m.TakeDamage(damage);
            }
        }
    }

    public class ElementiumSquirrelBombDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int mechDeaths = source.ScriptDataNum1;
            int damage = source.Golden ? (mechDeaths + 1) * 8 : (mechDeaths + 1) * 4;
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0) enemy[Rng.Next(enemy.Count)].TakeDamage(damage);
        }
    }

    public class LeapfroggerDeathrattleEffect : IDeathrattleEffect
    {
        private static readonly Random Rng = new Random();
        public void Trigger(Minion source, CombatState state)
        {
            int bonus = source.Golden ? 2 : 1;
            var beasts = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(source))
                if (m.PrimaryRace == "Beast" && m != source) beasts.Add(m);
            if (beasts.Count > 0)
            {
                var t = beasts[Rng.Next(beasts.Count)];
                t.BaseAttack += bonus; t.MaxHealth += bonus; t.CurrentHealth += bonus;
                // 传递亡语（简化：不实现链式传递）
            }
        }
    }

    public class TimewarpedWarghoulDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            var friendly = state.GetFriendlyBoard(source);
            int idx = source.Position;
            // 触发相邻随从的亡语
            if (idx > 0 && idx - 1 < friendly.Count)
            {
                var adj = friendly[idx - 1];
                foreach (var dr in adj.Deathrattles)
                {
                    var summons = dr.TriggerDeathrattle(adj, adj.Golden);
                    if (summons != null)
                        foreach (var s in summons)
                            if (friendly.Count < 7) friendly.Add(s);
                }
                foreach (var effect in adj.DeathrattleEffects)
                    effect.Trigger(adj, state);
            }
            if (idx + 1 < friendly.Count)
            {
                var adj = friendly[idx + 1];
                foreach (var dr in adj.Deathrattles)
                {
                    var summons = dr.TriggerDeathrattle(adj, adj.Golden);
                    if (summons != null)
                        foreach (var s in summons)
                            if (friendly.Count < 7) friendly.Add(s);
                }
                foreach (var effect in adj.DeathrattleEffects)
                    effect.Trigger(adj, state);
            }
        }
    }

    // ── 特殊召唤类亡语 ──

    public class SlyRaptorDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var factory = MinionFactoryCache.GetFactory();
            var token = new Minion
            {
                CardId = "BG25_806t",
                Name = "Sly Raptor Token",
                BaseAttack = golden ? 12 : 6,
                BaseHealth = golden ? 12 : 6,
                MaxHealth = golden ? 12 : 6,
                CurrentHealth = golden ? 12 : 6,
                Tier = 1,
                PrimaryRace = "Beast",
                ControlledByPlayer = source.ControlledByPlayer,
                Golden = golden,
            };
            return new List<Minion> { token };
        }
    }

    public class HandlessForsakenDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var token = new Minion
            {
                CardId = "BG25_010t",
                Name = "Handless Forsaken Token",
                BaseAttack = golden ? 4 : 2,
                BaseHealth = golden ? 2 : 1,
                MaxHealth = golden ? 2 : 1,
                CurrentHealth = golden ? 2 : 1,
                Tier = 1,
                PrimaryRace = "Undead",
                ControlledByPlayer = source.ControlledByPlayer,
                Golden = golden,
                Reborn = true,
            };
            return new List<Minion> { token };
        }
    }

    public class TwilightHatchlingDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            var token = new Minion
            {
                CardId = "BG34_630t",
                Name = "Twilight Hatchling Token",
                BaseAttack = golden ? 6 : 3,
                BaseHealth = golden ? 6 : 3,
                MaxHealth = golden ? 6 : 3,
                CurrentHealth = golden ? 6 : 3,
                Tier = 1,
                PrimaryRace = "Dragon",
                ControlledByPlayer = source.ControlledByPlayer,
                Golden = golden,
            };
            return new List<Minion> { token };
        }
    }

    public class SneedsNewShredderDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 简化：召唤一个随机传说随从
            var token = new Minion
            {
                CardId = "BG21_HERO_030t",
                Name = "Sneed's Token",
                BaseAttack = golden ? 10 : 5,
                BaseHealth = golden ? 10 : 5,
                MaxHealth = golden ? 10 : 5,
                CurrentHealth = golden ? 10 : 5,
                Tier = 5,
                ControlledByPlayer = source.ControlledByPlayer,
                Golden = golden,
            };
            return new List<Minion> { token };
        }
    }

    public class KangorsApprenticeDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 需要跟踪死亡的机械，简化为召唤 2 个 8/8
            int count = golden ? 4 : 2;
            var result = new List<Minion>();
            for (int i = 0; i < count; i++)
            {
                result.Add(new Minion
                {
                    CardId = "BGS_012t",
                    Name = "Kangor's Token",
                    BaseAttack = 8,
                    BaseHealth = 8,
                    MaxHealth = 8,
                    CurrentHealth = 8,
                    Tier = 5,
                    PrimaryRace = "Mech",
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                });
            }
            return result;
        }
    }

    public class EternalSummonerDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            int count = golden ? 4 : 2;
            var result = new List<Minion>();
            for (int i = 0; i < count; i++)
            {
                result.Add(new Minion
                {
                    CardId = "BG25_009t",
                    Name = "Eternal Summoner Token",
                    BaseAttack = golden ? 10 : 5,
                    BaseHealth = golden ? 10 : 5,
                    MaxHealth = golden ? 10 : 5,
                    CurrentHealth = golden ? 10 : 5,
                    Tier = 5,
                    PrimaryRace = "Undead",
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                    Reborn = true,
                });
            }
            return result;
        }
    }

    public class BarrensConjurerDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            return new List<Minion> { source.Clone() };
        }
    }

    public class MagnanimooseDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 简化：召唤一个 1/1
            return new List<Minion>
            {
                new Minion
                {
                    CardId = "BGDUO_105t",
                    Name = "Magnanimoose Token",
                    BaseAttack = 1,
                    BaseHealth = 1,
                    MaxHealth = 1,
                    CurrentHealth = 1,
                    Tier = 1,
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                }
            };
        }
    }

    public class StitchedSalvagerDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 简化：召唤一个 8/8
            return new List<Minion>
            {
                new Minion
                {
                    CardId = "BG31_999t",
                    Name = "Stitched Salvager Token",
                    BaseAttack = golden ? 16 : 8,
                    BaseHealth = golden ? 16 : 8,
                    MaxHealth = golden ? 16 : 8,
                    CurrentHealth = golden ? 16 : 8,
                    Tier = 7,
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                }
            };
        }
    }

    public class TimewarpedRadioStarDeathrattle : IDeathrattle
    {
        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            // 简化：召唤一个 5/5
            return new List<Minion>
            {
                new Minion
                {
                    CardId = "BG34_Giant_330t",
                    Name = "Radio Star Token",
                    BaseAttack = golden ? 10 : 5,
                    BaseHealth = golden ? 10 : 5,
                    MaxHealth = golden ? 10 : 5,
                    CurrentHealth = golden ? 10 : 5,
                    Tier = 5,
                    ControlledByPlayer = source.ControlledByPlayer,
                    Golden = golden,
                }
            };
        }
    }

    public class LeeroyDeathrattleEffect : IDeathrattleEffect
    {
        public void Trigger(Minion source, CombatState state)
        {
            // 简化：对随机敌方造成 5 伤害
            var enemy = state.GetEnemyBoard(source);
            if (enemy.Count > 0)
            {
                var rng = new Random();
                enemy[rng.Next(enemy.Count)].TakeDamage(5);
            }
        }
    }

    // ── 友方死亡触发 ──

    public class ScavengingHyenaTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            if (dead.PrimaryRace == "Beast")
            {
                int atk = self.Golden ? 4 : 2;
                int hp = self.Golden ? 2 : 1;
                self.BaseAttack += atk; self.MaxHealth += hp; self.CurrentHealth += hp;
            }
        }
    }

    public class JunkbotTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            if (dead.PrimaryRace == "Mech")
            {
                int bonus = self.Golden ? 4 : 2;
                self.BaseAttack += bonus; self.MaxHealth += bonus; self.CurrentHealth += bonus;
            }
        }
    }

    public class FlesheatingGhoulTrigger : IOnFriendlyMinionDied
    {
        public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
        {
            self.BaseAttack += self.Golden ? 2 : 1;
        }
    }

    // ── 战斗开始触发 ──

    public class RedWhelpTrigger : IOnStartOfCombat
    {
        private static readonly Random Rng = new Random();
        public void OnStartOfCombat(Minion self, CombatState state)
        {
            int dragons = 0;
            foreach (var m in state.GetFriendlyBoard(self))
                if (m.PrimaryRace == "Dragon") dragons++;
            int damage = self.Golden ? dragons * 2 : dragons;
            if (damage <= 0) return;
            var enemy = state.GetEnemyBoard(self);
            for (int i = 0; i < damage && enemy.Count > 0; i++)
                enemy[Rng.Next(enemy.Count)].TakeDamage(1);
        }
    }

    public class SpiritOfAirTrigger : IOnStartOfCombat
    {
        private static readonly Random Rng = new Random();
        public void OnStartOfCombat(Minion self, CombatState state)
        {
            var candidates = new List<Minion>();
            foreach (var m in state.GetFriendlyBoard(self))
                if (m != self) candidates.Add(m);
            if (candidates.Count > 0)
            {
                var t = candidates[Rng.Next(candidates.Count)];
                t.Windfury = true; t.DivineShield = true; t.Taunt = true;
            }
        }
    }

    // ── 攻击后触发 ──

    public class MonstrousMacawTrigger : IOnAfterAttack
    {
        private static readonly Random Rng = new Random();
        public void OnAfterAttack(Minion self, Minion attacker, Minion target, CombatState state)
        {
            if (attacker != self) return;
            var friendly = state.GetFriendlyBoard(self);
            var drMinions = new List<Minion>();
            foreach (var m in friendly)
                if (m != self && (m.Deathrattles.Count > 0 || m.DeathrattleEffects.Count > 0))
                    drMinions.Add(m);
            if (drMinions.Count == 0) return;
            var chosen = drMinions[Rng.Next(drMinions.Count)];
            foreach (var dr in chosen.Deathrattles)
            {
                var summons = dr.TriggerDeathrattle(chosen, chosen.Golden);
                if (summons != null)
                    foreach (var s in summons)
                        if (friendly.Count < 7) friendly.Add(s);
            }
            foreach (var effect in chosen.DeathrattleEffects)
                effect.Trigger(chosen, state);
        }
    }

    // ── 友方召唤触发 ──

    public class MamaBearTrigger : IOnFriendlyMinionSummoned
    {
        public void OnFriendlyMinionSummoned(Minion self, Minion summoned, CombatState state)
        {
            if (summoned.PrimaryRace == "Beast")
            {
                int bonus = self.Golden ? 8 : 4;
                summoned.BaseAttack += bonus; summoned.MaxHealth += bonus; summoned.CurrentHealth += bonus;
            }
        }
    }

    public class PackLeaderTrigger : IOnFriendlyMinionSummoned
    {
        public void OnFriendlyMinionSummoned(Minion self, Minion summoned, CombatState state)
        {
            if (summoned.PrimaryRace == "Beast")
                summoned.BaseAttack += self.Golden ? 6 : 3;
        }
    }

    // ── 被动加成 ──

    public class MalGanisAttackBonus : IPassiveAttackBonus
    {
        public int GetPassiveAttackBonus(Minion self, CombatState state) => 0;
    }

    public class MalGanisHealthBonus : IPassiveHealthBonus
    {
        public int GetPassiveHealthBonus(Minion self, CombatState state) => 0;
    }

    public class KalecgosAttackBonus : IPassiveAttackBonus
    {
        public int GetPassiveAttackBonus(Minion self, CombatState state) => 0;
    }

    public class KalecgosHealthBonus : IPassiveHealthBonus
    {
        public int GetPassiveHealthBonus(Minion self, CombatState state) => 0;
    }
}
