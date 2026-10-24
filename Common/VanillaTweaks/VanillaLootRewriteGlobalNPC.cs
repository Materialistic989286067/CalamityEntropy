using System.Collections.Generic;
using Terraria;
using Terraria.Chat;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>肉山魂、月亮领主碎片与夜明矿翻倍、石巨人首杀锯刃镐、克眼首杀排血月、月亮领主首杀播报</summary>
    public class VanillaLootRewriteGlobalNPC : GlobalNPC
    {
        public const int WallOfFleshSouls = 12;
        public const int MoonLordFragments = 20;

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) {
            return entity.type == NPCID.WallofFlesh || entity.type == NPCID.MoonLordCore
                || entity.type == NPCID.Golem || entity.type == NPCID.EyeofCthulhu;
        }

        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot) {
            switch (npc.type) {
                case NPCID.WallofFlesh:
                    npcLoot.Add(ItemDropRule.Common(ItemID.SoulofLight, 1, WallOfFleshSouls, WallOfFleshSouls));
                    npcLoot.Add(ItemDropRule.Common(ItemID.SoulofNight, 1, WallOfFleshSouls, WallOfFleshSouls));
                    break;
                case NPCID.MoonLordCore:
                    npcLoot.Add(ItemDropRule.Common(ItemID.FragmentSolar, 1, MoonLordFragments, MoonLordFragments));
                    npcLoot.Add(ItemDropRule.Common(ItemID.FragmentVortex, 1, MoonLordFragments, MoonLordFragments));
                    npcLoot.Add(ItemDropRule.Common(ItemID.FragmentNebula, 1, MoonLordFragments, MoonLordFragments));
                    npcLoot.Add(ItemDropRule.Common(ItemID.FragmentStardust, 1, MoonLordFragments, MoonLordFragments));
                    DoubleLunarOre(npcLoot);
                    break;
                case NPCID.Golem:
                    npcLoot.Add(ItemDropRule.ByCondition(new GolemFirstKillCondition(), ItemID.Picksaw));
                    break;
            }
        }

        public override void OnKill(NPC npc) {
            //此时 downedBoss1 尚未置位
            if (npc.type == NPCID.EyeofCthulhu && !NPC.downedBoss1) {
                EyeBloodMoonSystem.Arm();
            }
            //downedMoonlord 同样尚未置位
            if (npc.type == NPCID.MoonLordCore && !NPC.downedMoonlord) {
                AnnounceMoonLordFirstKill();
            }
        }

        /// <summary>OnKill 只在单人与服务端触发,联机时由服务端统一广播</summary>
        private static void AnnounceMoonLordFirstKill() {
            LocalizedText text = CalamityEntropy.Instance.GetLocalization("MoonLordFirstKillMessage");
            Color color = new Color(200, 160, 255);
            if (Main.netMode == NetmodeID.SinglePlayer) {
                Main.NewText(text.Value, color);
            }
            else if (Main.netMode == NetmodeID.Server) {
                ChatHelper.BroadcastChatMessage(text.ToNetworkText(), color);
            }
        }

        private static void DoubleLunarOre(NPCLoot npcLoot) {
            List<CommonDrop> ores = new List<CommonDrop>();
            foreach (IItemDropRule rule in npcLoot.Get(false)) {
                if (rule is CommonDrop drop && drop.itemId == ItemID.LunarOre) {
                    ores.Add(drop);
                }
            }
            foreach (CommonDrop old in ores) {
                npcLoot.Remove(old);
                int min = old.amountDroppedMinimum * 2;
                int max = old.amountDroppedMaximum * 2;
                if (old is ItemDropWithConditionRule conditional) {
                    npcLoot.Add(new ItemDropWithConditionRule(ItemID.LunarOre, old.chanceDenominator, min, max, conditional.condition, old.chanceNumerator));
                }
                else {
                    npcLoot.Add(new CommonDrop(ItemID.LunarOre, old.chanceDenominator, min, max, old.chanceNumerator));
                }
            }
        }
    }

    /// <summary>石巨人尚未被击败过</summary>
    public class GolemFirstKillCondition : IItemDropRuleCondition, IProvideItemConditionDescription
    {
        public bool CanDrop(DropAttemptInfo info) => !NPC.downedGolemBoss;
        public bool CanShowItemDropInUI() => !NPC.downedGolemBoss;
        public string GetConditionDescription() => Language.GetTextValue("Mods.CalamityEntropy.GolemFirstKillDrop");
    }
}
