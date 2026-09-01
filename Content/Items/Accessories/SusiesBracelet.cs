using CalamityEntropy.Common;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Content.Items.Accessories
{
    // 2026-08-31 平衡案重做:获取改为海龟25%掉落;升级阶段重置为11档,
    // 全程免疫击退,近战伤害/暴击随击败Boss成长(终阶18%伤害/5%暴击)。
    public class SusiesBracelet : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 28;
            Item.accessory = true;
            Item.value = Item.buyPrice(gold: 20);
            Item.rare = ItemRarityID.Yellow;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.noKnockback = true;
            player.GetDamage(DamageClass.Melee) += AddMeleeDamage;
            player.GetCritChance(DamageClass.Melee) += AddMeleeCrit;
        }

        public int Level = 0;
        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(Level);
        }
        public override void NetReceive(BinaryReader reader)
        {
            Level = reader.ReadInt32();
        }
        public override void SaveData(TagCompound tag)
        {
            if (Level > 0)
                tag["Level"] = Level;
        }
        public override void LoadData(TagCompound tag)
        {
            if (tag.TryGet<int>("Level", out int lv))
            {
                Level = lv;
            }
        }
        public int GetLevel()
        {
            CheckUpdate();
            return Level;
        }

        public void CheckUpdate()
        {
            void Check(bool f, int lv)
            {
                if (lv > Level && f)
                {
                    Level = lv;
                }
            }
            Check(NPC.downedSlimeKing, 1);
            Check(NPC.downedBoss1, 2);
            Check(NPC.downedBoss2, 3);
            Check(NPC.downedBoss3, 4);
            Check(Main.hardMode, 5);
            Check(NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3, 6);
            Check(NPC.downedPlantBoss, 7);
            Check(NPC.downedGolemBoss, 8);
            Check(EDownedBosses.downedProphet, 9);
            Check(NPC.downedMoonlord, 10);
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            int index = 0;
            for (int i = 0; i < tooltips.Count; i++)
            {
                if (tooltips[i].Name.Contains("Tooltip"))
                {
                    index = i;
                }
            }
            index++;
            if (GetLevel() < 10)
            {
                tooltips.Add(new TooltipLine(Mod, $"Tooltip{index}", GetLt($"Trial", "Trials").Value + $"{GetLevel() + 1} - " + GetLt($"t{GetLevel()}", "Trials").Value) { OverrideColor = Color.Yellow });
            }
            else
            {
                tooltips.Add(new TooltipLine(Mod, $"Tooltip{index}", GetLt("t10", "Trials").Value) { OverrideColor = Color.Yellow });
            }

            tooltips.Add(new TooltipLine(Mod, $"Tooltip{index}", GetLt($"l{GetLevel()}").Value) { OverrideColor = Color.Pink });

            tooltips.Replace("[DMG]", AddMeleeDamage.ToPercent().ToString());
            tooltips.Replace("[CRIT]", AddMeleeCrit.ToString());
        }
        public float AddMeleeDamage => GetLevel() switch
        {
            0 => 0.01f,
            1 => 0.02f,
            2 => 0.03f,
            3 => 0.04f,
            4 => 0.05f,
            5 => 0.06f,
            6 => 0.07f,
            7 => 0.08f,
            8 => 0.10f,
            9 => 0.12f,
            _ => 0.18f
        };
        public int AddMeleeCrit => GetLevel() switch
        {
            0 => 0,
            1 => 0,
            2 => 0,
            3 => 1,
            4 => 1,
            5 => 2,
            6 => 2,
            7 => 3,
            8 => 4,
            9 => 4,
            _ => 5
        };
        public static LocalizedText GetLt(string n, string h = "Lores")
        {
            return Language.GetText($"Mods.CalamityEntropy.LegendaryAbility.SusiesBracelet.{h}.{n}");
        }
    }

    /// <summary>苏西腕带掉落:海龟 25%。</summary>
    public class SusiesBraceletDropGNPC : GlobalNPC
    {
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            if (npc.type == NPCID.SeaTurtle)
            {
                npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<SusiesBracelet>(), 4));
            }
        }
    }
}
