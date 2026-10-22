using CalamityEntropy.Common;
using CalamityEntropy.Content.Items;
using System.Collections.Generic;
using Terraria;
using Terraria.Chat;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Events.VoidDestroyerAftermath
{
    /// <summary>敌怪强化倍率,先乘在 SetDefaults 的基础值上,专家/大师缩放照常叠在后面,KnockBackResist 为绝对值,负数不改</summary>
    internal readonly record struct VDStatBoost(float Damage, float Life, float Defense = 1f, float KnockBackResist = -1f);

    /// <summary>必掉或按概率掉的新材料,Chance 为分母</summary>
    internal readonly record struct VDMaterialDrop(int Chance, int Min, int Max);

    /// <summary>
    /// 虚空驱逐舰击败后的世界变化,类似血肉墙后的困难模式
    /// 数值表来自「驱逐舰后改动」策划稿,强化与掉落都按 NPC 类型查表
    /// </summary>
    internal static class VDAftermath
    {
        public static bool Active => EDownedBosses.downedVoidDestroyer;

        public static readonly Dictionary<int, VDStatBoost> StatBoosts = new();
        public static readonly Dictionary<int, VDMaterialDrop> SunlightDrops = new();
        public static readonly Dictionary<int, VDMaterialDrop> StarSpiritDrops = new();

        static VDAftermath() {
            //神圣之地普通敌怪
            VDStatBoost hallow = new(2f, 20f);
            AddHallow(NPCID.Unicorn, new(2f, 20f, 1f, 0.05f), 1, 1, 1);
            AddHallow(NPCID.Gastropod, hallow, 3, 1, 1);
            AddHallow(NPCID.Pixie, hallow, 3, 1, 1);
            AddHallow(NPCID.LightMummy, hallow, 3, 1, 1);
            AddHallow(NPCID.RainbowSlime, hallow, 1, 1, 3);
            AddHallow(NPCID.IlluminantSlime, hallow, 3, 1, 1);
            AddHallow(NPCID.IlluminantBat, hallow, 3, 1, 1);
            AddHallow(NPCID.DesertGhoulHallow, hallow, 3, 1, 1);
            AddHallow(NPCID.PigronHallow, hallow, 1, 2, 3);
            //神圣之地特殊敌怪
            AddHallow(NPCID.EnchantedSword, new(1.5f, 15f), 1, 1, 3);
            AddHallow(NPCID.ChaosElemental, new(2.5f, 15f), 1, 1, 3);
            AddHallow(NPCID.BigMimicHallow, new(1.66f, 6f, 2f), 1, 12, 16);

            //地牢花前敌怪
            VDStatBoost dungeonEarly = new(3f, 30f);
            foreach (int type in new int[] { NPCID.AngryBones, NPCID.AngryBonesBig, NPCID.AngryBonesBigMuscle, NPCID.AngryBonesBigHelmet,
                NPCID.DarkCaster, NPCID.CursedSkull }) {
                AddDungeon(type, dungeonEarly, 2, 1, 1);
            }
            AddDungeon(NPCID.DungeonSlime, dungeonEarly, 1, 2, 3);

            //地牢花后骷髅
            VDStatBoost dungeonLate = new(2f, 12f);
            foreach (int type in new int[] {
                NPCID.BlueArmoredBones, NPCID.BlueArmoredBonesMace, NPCID.BlueArmoredBonesNoPants, NPCID.BlueArmoredBonesSword,
                NPCID.RustyArmoredBonesAxe, NPCID.RustyArmoredBonesFlail, NPCID.RustyArmoredBonesSword, NPCID.RustyArmoredBonesSwordNoArmor,
                NPCID.HellArmoredBones, NPCID.HellArmoredBonesSpikeShield, NPCID.HellArmoredBonesMace, NPCID.HellArmoredBonesSword }) {
                AddDungeon(type, dungeonLate, 3, 1, 1);
            }

            //地牢花后法师单位
            foreach (int type in new int[] { NPCID.Necromancer, NPCID.NecromancerArmored, NPCID.RaggedCaster, NPCID.RaggedCasterOpenCoat,
                NPCID.DiabolistRed, NPCID.DiabolistWhite }) {
                AddDungeon(type, dungeonLate, 1, 1, 1);
            }
            AddDungeon(NPCID.GiantCursedSkull, dungeonLate, 2, 1, 1);
            AddDungeon(NPCID.DungeonSpirit, dungeonLate, 2, 1, 1);

            //地牢特殊敌怪,水球只强化伤害不掉落
            StatBoosts[NPCID.WaterSphere] = new(1.33f, 1f);
            AddDungeon(NPCID.BoneLee, new(1.33f, 6f), 1, 1, 3);
            AddDungeon(NPCID.SkeletonCommando, new(1.2f, 10f), 1, 1, 2);
            AddDungeon(NPCID.TacticalSkeleton, new(1.2f, 10f), 1, 1, 2);
            AddDungeon(NPCID.SkeletonSniper, new(1f, 10f), 1, 1, 1);
            AddDungeon(NPCID.Paladin, new(1.5f, 4f), 1, 12, 16);
        }

        private static void AddHallow(int type, VDStatBoost boost, int chance, int min, int max) {
            StatBoosts[type] = boost;
            SunlightDrops[type] = new(chance, min, max);
        }

        private static void AddDungeon(int type, VDStatBoost boost, int chance, int min, int max) {
            StatBoosts[type] = boost;
            StarSpiritDrops[type] = new(chance, min, max);
        }

        /// <summary>首次击败虚空驱逐舰时在聊天栏播报三行,只在服务端或单人调用</summary>
        public static void Announce() {
            Broadcast("Sun", new Color(255, 96, 64));
            Broadcast("StarMoon", new Color(96, 150, 255));
            Broadcast("Beast", new Color(180, 96, 255));
        }

        private static void Broadcast(string key, Color color) {
            LocalizedText text = CalamityEntropy.Instance.GetLocalization("VDAftermath." + key);
            if (Main.netMode == NetmodeID.SinglePlayer) {
                Main.NewText(text.Value, color);
            }
            else if (Main.netMode == NetmodeID.Server) {
                ChatHelper.BroadcastChatMessage(text.ToNetworkText(), color);
            }
        }

        public static int SunlightType => ModContent.ItemType<BlazingSunlight>();
        public static int StarSpiritType => ModContent.ItemType<WraithSoulEssence>();
    }

    /// <summary>雕像刷出的敌怪不掉,免得独角兽雕像刷材料</summary>
    public class PostVoidDestroyerCondition : IItemDropRuleCondition, IProvideItemConditionDescription
    {
        public bool CanDrop(DropAttemptInfo info) => VDAftermath.Active && (info.npc == null || !info.npc.SpawnedFromStatue);
        public bool CanShowItemDropInUI() => true;
        public string GetConditionDescription() => CalamityEntropy.Instance.GetLocalization("VDAftermath.DropPost").Value;
    }

    /// <summary>原版掉落规则在驱逐舰后被替换时,旧规则挂在这个条件下</summary>
    public class PreVoidDestroyerCondition : IItemDropRuleCondition, IProvideItemConditionDescription
    {
        public bool CanDrop(DropAttemptInfo info) => !VDAftermath.Active;
        public bool CanShowItemDropInUI() => !VDAftermath.Active;
        public string GetConditionDescription() => CalamityEntropy.Instance.GetLocalization("VDAftermath.DropPre").Value;
    }
}
