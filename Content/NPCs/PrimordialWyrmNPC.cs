using CalamityEntropy.Common;
using CalamityEntropy.Content.Items;
using CalamityEntropy.Content.Projectiles.Cruiser;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace CalamityEntropy.Content.NPCs
{
    [AutoloadHead]
    public class PrimordialWyrmNPC : ModNPC
    {
        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Clothier];
            NPCID.Sets.ExtraFramesCount[Type] = NPCID.Sets.ExtraFramesCount[NPCID.Clothier];
            NPCID.Sets.AttackFrameCount[Type] = NPCID.Sets.AttackFrameCount[NPCID.Clothier];
            NPCID.Sets.DangerDetectRange[Type] = 1000;
            NPCID.Sets.AttackType[Type] = NPCID.Sets.AttackType[NPCID.Clothier];
            NPCID.Sets.AttackTime[Type] = 50;
            NPCID.Sets.AttackAverageChance[Type] = 1;
            NPCID.Sets.MagicAuraColor[base.NPC.type] = Color.Purple;
        }
        public override void SetDefaults()
        {
            NPC.townNPC = true;
            NPC.friendly = true;
            NPC.width = 22;
            NPC.height = 32;
            NPC.aiStyle = 7;
            NPC.damage = 10;
            NPC.defense = 105;
            NPC.lifeMax = 7200000;
            NPC.HitSound = SoundID.NPCHit1;
            // 死亡音效就近取巡游者死亡爆发音
            NPC.DeathSound = CEUtils.GetSound("VoidAttack");
            NPC.knockBackResist = 0f;
            AnimationType = NPCID.Clothier;
        }
        public override bool PreAI()
        {
            dcd--;
            return base.PreAI();
        }
        public override bool CanTownNPCSpawn(int numTownNPCs)
        {
            // 入住条件改为击败巡游者（进度表：原渊海灾虫槽位并入巡游者）
            if (EDownedBosses.downedCruiser)
            {
                return true;
            }
            return false;
        }

        public override string GetChat()
        {
            WeightedRandom<string> chat = new WeightedRandom<string>();
            {
                if (Main.rand.NextBool(6))
                {
                    string dns = "";
                    var lc = new List<string>();
                    foreach (string s in Donators.Donors)
                    {
                        lc.Add(s);
                    }
                    for (int i = 0; i < 16; i++)
                    {
                        int d = Main.rand.Next(lc.Count);
                        dns += lc[d];
                        if (i < 15)
                        {
                            dns += ", ";
                        }
                        lc.RemoveAt(d);
                    }
                    chat.Add(Mod.GetLocalization("WyrmChatDonors").Value.Replace("[0]", dns));
                    return chat;
                }
                if (!Main.bloodMoon && !Main.eclipse)
                {
                    if (NPC.homeless)
                    {
                        chat.Add(Mod.GetLocalization("WyrmChatNoHome").Value);
                    }
                    else
                    {
                        chat.Add(Mod.GetLocalization("WyrmChat" + Main.rand.Next(1, 12).ToString()).Value);
                        if (Main.raining)
                            chat.Add(Mod.GetLocalization("WyrmChatRain" + Main.rand.Next(1, 4).ToString()).Value);
                    }
                }
                else
                {
                    if (Main.eclipse)
                    {
                        chat.Add(Mod.GetLocalization("WyrmChatEclipse1").Value);
                        chat.Add(Mod.GetLocalization("WyrmChatEclipse2").Value);
                    }
                    if (Main.bloodMoon)
                    {
                        chat.Add(Mod.GetLocalization("WyrmChatBloodMoon1").Value);
                        chat.Add(Mod.GetLocalization("WyrmChatBloodMoon2").Value);
                    }
                }
                return chat;
            }
        }
        public override void SetChatButtons(ref string button, ref string button2)
        {
            button = Language.GetTextValue("LegacyInterface.28");
            button2 = Mod.GetLocalization("SpecialThanks").Value;
        }
        public override void OnChatButtonClicked(bool firstButton, ref string shopName)
        {

            if (firstButton)
            {
                shopName = ShopName;
            }
            else
            {
                string chat = "";
                string dns = "";
                var lc = new List<string>();
                foreach (string s in Donators.Donors)
                {
                    lc.Add(s);
                }
                for (int i = 0; i < 25; i++)
                {
                    int d = Main.rand.Next(lc.Count);
                    dns += lc[d];
                    if (i < 24)
                    {
                        dns += ", ";
                    }
                    lc.RemoveAt(d);
                }
                chat = Mod.GetLocalization("WyrmChatDonors").Value.Replace("[0]", dns);
                Main.npcChatText = chat;
            }
        }
        public static string ShopName = "Shop";
        public override void AddShops()
        {
            // 货架全部换为自有与原版商品（杂项处置表 §三 定稿清单）
            var npcShop = new NPCShop(Type, ShopName)
                .Add<WyrmTooth>()
                .Add<VoidBar>()
                .Add<NihilityFragments>()
                .Add<WraithSoulEssence>()
                .Add(ItemID.LunarOre)
                .Add(ItemID.SuperHealingPotion)
                .Add(ItemID.Celeb2)
                .Add(ItemID.LastPrism)
                .Add(ItemID.LunarFlareBook)
                .Add(ItemID.PaladinsHammer)
                .Add(ItemID.BoneTorch)
                .Add(new Item(ItemID.FossilOre, 50))
                .Add(ItemID.SharkToothNecklace)
                .Add(ItemID.StaticHook)
                .Add(ItemID.MusicBoxBoss5);
            npcShop.Register();
        }

        public override void ModifyActiveShop(string shopName, Item[] items)
        {
            foreach (Item item in items)
            {
                if (item == null || item.type == ItemID.None)
                {
                    continue;
                }

                int value = item.shopCustomPrice ?? item.value;
                item.shopCustomPrice = value / 8;
            }
        }
        public override void TownNPCAttackStrength(ref int damage, ref float knockback)
        {
            damage = Main.zenithWorld ? 2000 : 700;

            knockback = 3f;

        }
        public override void TownNPCAttackCooldown(ref int cooldown, ref int randExtraCooldown)
        {
            cooldown = 30;
            randExtraCooldown = 15;
        }

        public int dcd = 0;
        public override void TownNPCAttackProj(ref int projType, ref int attackDelay)
        {
            // 攻击弹幕统一改自有巡游者激光（zenith 分支同用），尖啸音沿用自有 he 系列
            projType = ModContent.ProjectileType<CruiserLaser2>();
            attackDelay = 4;
            if (dcd <= 0)
            {
                var sd = CEUtils.GetSound("he" + (Main.rand.NextBool() ? 1 : 3).ToString());
                sd.MaxInstances = 6;
                SoundEngine.PlaySound(in sd, NPC.Center);
                dcd = 59;
            }
        }
        public override void PostAI()
        {
            // 巡游者激光默认敌对且以 ai[0] 绑定所有者 NPC；城镇攻击 AI 生成后在此改挂
            // 本模组统一友好化通道 ToFriendly（每帧强制转友方并随 ExtraAI 同步），并解除所有者绑定
            foreach (Projectile proj in Main.ActiveProjectiles)
            {
                if (proj.type == ModContent.ProjectileType<CruiserLaser2>() && proj.npcProj && !proj.Entropy().ToFriendly)
                {
                    proj.Entropy().ToFriendly = true;
                    // 驯服弹幕默认吃 16 倍增伤（FriendFinder 宠物专用），城镇攻击保持面板伤害，须关掉
                    proj.Entropy().dmgUpFrd = false;
                    proj.ai[0] = -1;
                    proj.netUpdate = true;
                }
            }
        }
        public override void TownNPCAttackProjSpeed(ref float multiplier, ref float gravityCorrection, ref float randomOffset)
        {
            multiplier = 14.5f;
            if (Main.zenithWorld)
            {
                multiplier = 32;
            }
            gravityCorrection = 0f;
            randomOffset = 0f;
        }
        public override void TownNPCAttackMagic(ref float auraLightMultiplier)
        {
            auraLightMultiplier = 2f;
        }
    }
}
