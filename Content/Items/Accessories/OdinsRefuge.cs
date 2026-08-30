using System;
using CalamityEntropy.Content.Buffs.PortsDoT;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ID;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Content.Items.Accessories
{
    [AutoloadEquip(EquipType.Shield)]
    public class OdinsRefuge : ModItem
    {
        // 盾撞档位:对照灾厄阿斯加德神盾(1000/12帧),本饰品位于终局(虚渺锭为击败巡游者后产物),高于VoidCore的800
        public const int ShieldSlamDamage = 1000;
        public const float ShieldSlamKnockback = 8f;
        public const int ShieldSlamIFrames = 16;
        // 圣光冲击波:每次冲刺首次撞击绽放,基础300吃最强职业加成(对照灾厄RamExplosionDamage=300)
        public const int HolyBurstDamage = 300;
        public const float HolyBurstRadius = 160f;
        // 圣佑窗口:盾撞命中后的短暂额外减伤
        public const int WardDuration = 90;
        public const float WardDR = 0.15f;

        public override void SetDefaults()
        {
            Item.width = 86;
            Item.height = 86;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.accessory = true;
            Item.defense = 24;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().holyMantle = true;
            // 脱离灾厄:原委托灾厄阿斯加德神盾/神明壁垒效果,改为等价自有防御包(表外裁定,数值供收尾实测调)
            player.noKnockback = true;
            player.fireWalk = true;
            player.buffImmune[BuffID.OnFire] = true;
            player.buffImmune[BuffID.OnFire3] = true;
            player.endurance += 0.05f;
            player.lifeRegen += 4;
            // 盾冲走自研引擎,与VoidCore同一接入姿势;dashType=0压掉原版盾冲避免叠加
            player.GetModPlayer<CEShieldDashPlayer>().ActiveDash = OdinsRefugeDash.Instance;
            player.dashType = 0;

            //Panic Necklace effect if enabled
            player.panic = panicNecklaceEnabled;
        }
        #region Toggleable Panic Necklace

        bool panicNecklaceEnabled = true;
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.FindAndReplace("[TOGGLE]", this.GetLocalizedValue("ToggleEffect" + (panicNecklaceEnabled ? "On" : "Off")));
        }
        public override bool CanRightClick() => Main.keyState.PressingShift();
        public override void RightClick(Player player)
        {
            panicNecklaceEnabled = !panicNecklaceEnabled;
            Item.NetStateChanged();
        }
        public override bool ConsumeItem(Player player) => false;
        public override void SaveData(TagCompound tag)
        {
            tag.Add("panic", panicNecklaceEnabled);
        }

        public override void LoadData(TagCompound tag)
        {
            panicNecklaceEnabled = tag.GetBool("panic");
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(panicNecklaceEnabled);
        }

        public override void NetReceive(BinaryReader reader)
        {
            panicNecklaceEnabled = reader.ReadBoolean();
        }

        public override void PostDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            CEUtils.DrawInventoryDot(spriteBatch, position, new Vector2(16, 16) * Main.inventoryScale, panicNecklaceEnabled);
        }
        #endregion
        public override void AddRecipes()
        {
            // 脱离灾厄:两件灾厄盾饰原料改为原版顶级防御饰品(表外裁定,档位由虚空井/虚空锭把关)
            CreateRecipe().
                AddIngredient(ItemID.AnkhShield, 1).
                AddIngredient(ItemID.PaladinsShield, 1).
                AddIngredient(ModContent.ItemType<HolyMantle>(), 1).
                AddIngredient(ModContent.ItemType<VoidBar>(), 10).
                AddTile(ModContent.TileType<VoidWellTile>()).
                Register();
        }
    }

    /// <summary>
    /// 圣佑冲锋(上神之佑盾冲,自研):金白圣光残影拖尾;每次冲刺的首次撞击绽放圣光冲击波
    /// (基础伤害吃最强职业加成,点燃神圣之火);盾撞命中后给短暂圣佑减伤窗口。
    /// 运行时字段仅服务本地玩家(引擎在 PreUpdateMovement 对非本地玩家早退,同 VoidCoreDash 约束)。
    /// </summary>
    public class OdinsRefugeDash : CEShieldDashEffect
    {
        public static readonly OdinsRefugeDash Instance = new();

        public int Time;

        public bool PostHit;

        public static string ID => "OdinsRefugeDash";
        public override string DashID => ID;

        public override float CalculateDashSpeed(Player player)
        {
            // 档位:灾厄阿斯加德23.3/VoidCore26/Azafure22,防御盾取居中偏稳的24
            return 24f;
        }

        public override void OnDashEffects(Player player)
        {
            Time = 0;
            PostHit = false;
            CEUtils.PlaySound("light_bolt", 1.5f, player.Center, 4, 0.55f);
            SpawnAfterimage(player);
        }

        public override void MidDashEffects(Player player, ref float dashSpeed, ref float dashSpeedDecelerationFactor, ref float runSpeedDecelerationFactor)
        {
            Time += 2;
            if (Time > 40)
            {
                player.velocity.X *= 0.95f;
            }
            if (Time < 60)
            {
                // 圣光残影:每6帧留一道玩家虚像,是与三件套区分的主视觉
                if (Time % 12 == 0)
                    SpawnAfterimage(player);

                // 金白光点与线光织成拖尾
                for (int i = 0; i < 4; i++)
                {
                    PRTLoader.NewParticle<PRT_GlowSpark>(CEUtils.randomPointInCircle(16) + player.Center - player.velocity * Main.rand.NextFloat(),
                        -player.velocity.RotatedByRandom(0.26f) * Main.rand.NextFloat(0.3f, 0.55f),
                        Color.Lerp(Color.Gold, Color.White, Main.rand.NextFloat(0.5f)),
                        Main.rand.NextFloat(0.09f, 0.13f)).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, -player.velocity.ToRotation(), 15);
                }
                for (int i = 0; i < 2; i++)
                {
                    PRTLoader.NewParticle<PRT_LineCal>(CEUtils.randomPointInCircle(18) + player.Center - player.velocity * Main.rand.NextFloat(),
                        -player.velocity * Main.rand.NextFloat(0.35f, 0.55f),
                        Color.Lerp(Color.LightGoldenrodYellow, Color.White, Main.rand.NextFloat()),
                        Main.rand.NextFloat(0.6f, 1f)).Configure(false, 10);
                }
                dashSpeed = 17f;
            }
        }

        public override void OnHitEffects(Player player, NPC npc, ref CEDashHitContext hitContext)
        {
            // 首次撞击:屏震+圣光冲击波(小范围AoE,基础300吃最强职业加成,挂神圣之火)
            if (!PostHit)
            {
                PostHit = true;
                ScreenShaker.AddShake(new ScreenShaker.ScreenShake(Vector2.Zero, 6));
                CEUtils.PlaySound("angel_blast1", 1.25f, npc.Center, 4, 0.35f);

                int burstDamage = (int)player.GetBestClassDamage().ApplyTo(OdinsRefuge.HolyBurstDamage);
                var burst = CEUtils.SpawnExplotionFriendly(player.GetSource_FromThis(), player, npc.Center, burstDamage, OdinsRefuge.HolyBurstRadius, DamageClass.Generic);
                burst.Entropy().applyBuffs.Add(ModContent.BuffType<HolyFlames>());

                // 冲击波演出:双层金白脉冲环+闪光(终值按HowlingCannon既有比例校准:半径120对应0.8)
                PRTLoader.NewParticle<PRT_PulseRing>(npc.Center, Vector2.Zero, Color.Gold, 0.12f).Configure(1.05f, 26);
                PRTLoader.NewParticle<PRT_PulseRing>(npc.Center, Vector2.Zero, Color.White, 0.08f).Configure(0.7f, 20);
                PRTLoader.NewParticle<PRT_ShineParticle>(npc.Center, Vector2.Zero, Color.Gold, 1.4f).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, 0, 12);
                PRTLoader.NewParticle<PRT_ShineParticle>(npc.Center, Vector2.Zero, Color.White, 0.8f).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, 0, 12);
                for (int i = 0; i < 14; i++)
                {
                    PRTLoader.NewParticle<PRT_LineCal>(npc.Center,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(6f, 14f),
                        Color.Lerp(Color.Gold, Color.White, Main.rand.NextFloat()),
                        Main.rand.NextFloat(0.8f, 1.5f)).Configure(false, Main.rand.Next(20, 28));
                }
            }
            CEUtils.PlaySound("LightHit", Main.rand.NextFloat(1f, 1.2f), npc.Center, 6, 0.5f);

            // 圣佑窗口:命中即刷新短暂额外减伤(每玩家状态入EModPlayer,不进static)
            player.Entropy().OdinWardTime = OdinsRefuge.WardDuration;

            hitContext.HitDirection = player.velocity.X != 0f ? Math.Sign(player.velocity.X) : player.direction;
            hitContext.PlayerImmunityFrames = OdinsRefuge.ShieldSlamIFrames;
            hitContext.damageClass = DamageClass.Melee;
            hitContext.BaseDamage = OdinsRefuge.ShieldSlamDamage;
            hitContext.BaseKnockback = OdinsRefuge.ShieldSlamKnockback;
        }

        private static void SpawnAfterimage(Player player)
        {
            var shadow = PRTLoader.NewParticle<PRT_PlayerShadow>(player.position, new Vector2(-Math.Sign(player.velocity.X == 0 ? player.direction : player.velocity.X) * 2, 0), Color.White, 1)
                .Configure(1, true, PRTDrawModeEnum.AlphaBlend, 0, 18);
            shadow.plr = player;
            // 残影上再叠一撮金光,把虚像染出圣光感
            for (int i = 0; i < 3; i++)
            {
                PRTLoader.NewParticle<PRT_GlowSpark>(player.Center + CEUtils.randomPointInCircle(14), Vector2.Zero,
                    Color.Gold, Main.rand.NextFloat(0.07f, 0.1f)).Configure(0.8f, true, PRTDrawModeEnum.AdditiveBlend, 0, 14);
            }
        }
    }
}
