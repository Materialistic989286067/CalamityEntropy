using CalamityEntropy.Common;
using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using CalamityEntropy.Content.Rarities;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 虚空掠食者召唤杖(void-invasion.md §5.4):事件掉落召唤武器(掠食者头 5% / 虚熵魔物 20%)。
    /// 召唤迷你掠食者(掠食者贴图 ×0.4,头 + 3 节 + 尾程序化蛇形)环绕玩家;
    /// 攻击时小型门袭演出(VoidPortal 缩小版)后俯冲穿过敌人。
    /// 伤害定标:每 ~1.8s 一轮门袭俯冲 ×420(穿透群伤),名义低于四面体持续流,群伤补位,偏下成立。
    /// </summary>
    public class VoidPredatorStaff : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.GamepadWholeScreenUseRange[Item.type] = true;
            ItemID.Sets.LockOnIgnoresCollision[Item.type] = true;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.IntegrateHotkey(CEKeybinds.CommandMinions);
        }

        public override void SetDefaults()
        {
            Item.damage = 420;
            Item.DamageType = DamageClass.Summon;
            Item.width = 110;
            Item.height = 110;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.knockBack = 4f;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.shoot = ModContent.ProjectileType<VoidPredatorMinion>();
            Item.shootSpeed = 2f;
            Item.value = Item.buyPrice(platinum: 2);
            Item.autoReuse = true;
            Item.UseSound = SoundID.Item44;
            Item.noMelee = true;
            Item.mana = 10;
            Item.buffType = ModContent.BuffType<VoidPredatorBuff>();
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override bool Shoot(Player player, Terraria.DataStructures.EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            player.AddBuff(Item.buffType, 3);
            int projectile = Projectile.NewProjectile(source, Main.MouseWorld, Vector2.Zero, type, Item.damage, knockback, player.whoAmI);
            Main.projectile[projectile].originalDamage = Item.damage;
            return false;
        }
    }

    /// <summary>
    /// 迷你掠食者随从:头实体 + 位置历史推 3 节体节与尾(×0.4 缩放,镜像掠食者绘制约定:画布上方 = 朝向)。
    /// 状态:0 环绕(无判定)→ 1 门袭前摇(锁定目标,身前减速)→ 2 俯冲(判定活跃,穿过敌人)→ 回 0。
    /// 门袭演出 = VoidPortal 缩小版(所有者端生成,原生同步);portalPos/state 走 SendExtraAI 手动同步。
    /// </summary>
    public class VoidPredatorMinion : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head")]
        private static Asset<Texture2D> headTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/body", 1, 2, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] bodyTexs;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/tail")]
        private static Asset<Texture2D> tailTex;

        public const float Scale = 0.4f;
        public const int SegmentCount = 3;
        public const float SegmentSpacing = 30f;
        public const float SeekRange = 800f;
        public const int WindupTime = 26;
        public const int DiveTime = 26;
        public const int AttackCooldown = 82;

        public byte state = 0;
        public int stateTimer = 0;
        public Vector2 portalPos = Vector2.Zero;
        private int attackCd = 40;
        private int targetIndex = -1;
        private readonly List<Vector2> history = new();

        public override void SetStaticDefaults()
        {
            Main.projFrames[Projectile.type] = 1;
            ProjectileID.Sets.MinionSacrificable[Projectile.type] = true;
            ProjectileID.Sets.MinionTargettingFeature[Projectile.type] = true;
        }

        public override void SetDefaults()
        {
            Projectile.DamageType = DamageClass.Summon;
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = true;
            Projectile.aiStyle = -1;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 120;
            Projectile.tileCollide = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 18;
            Projectile.netImportant = true;
        }

        public override bool? CanCutTiles() => false;

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(state);
            writer.WriteVector2(portalPos);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = reader.ReadByte();
            portalPos = reader.ReadVector2();
        }

        /// <summary>只有俯冲段有判定(门袭是它的攻击动词)。</summary>
        public override bool? CanDamage() => state == 2 ? null : false;

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (player.dead || !player.active)
            {
                return;
            }
            if (player.HasBuff(ModContent.BuffType<VoidPredatorBuff>()))
            {
                Projectile.timeLeft = 3;
            }

            NPC target = FindTarget(player);

            if (state == 0)
            {
                //环绕:圆轨 + 蛇形正弦摆
                float ang = (float)Main.timeForVisualEffects * 0.03f + Projectile.minionPos * 2.4f;
                Vector2 want = player.MountedCenter + ang.ToRotationVector2() * 96f
                    + (ang * 3f).ToRotationVector2() * 18f;
                Projectile.velocity = (want - Projectile.Center) * 0.14f;
                if (CEUtils.getDistance(Projectile.Center, player.Center) > 1600)
                {
                    Projectile.Center = want;
                    history.Clear();
                }
                attackCd--;
                if (attackCd <= 0 && target != null && Main.myPlayer == Projectile.owner)
                {
                    //门袭:目标侧向开小型门,门后俯冲(§5.4)
                    state = 1;
                    stateTimer = 0;
                    targetIndex = target.whoAmI;
                    Vector2 flank = CEUtils.randomRot().ToRotationVector2();
                    portalPos = target.Center + flank * 270f;
                    Projectile.NewProjectile(Projectile.GetSource_FromAI(), portalPos,
                        (target.Center - portalPos).SafeNormalize(Vector2.UnitX) * 0.02f,
                        ModContent.ProjectileType<VoidPortal>(), 0, 0, Projectile.owner, WindupTime + DiveTime + 14, 0.55f);
                    Projectile.netUpdate = true;
                }
            }
            else if (state == 1)
            {
                //前摇:身体收拢减速,门开满即穿门
                stateTimer++;
                Projectile.velocity *= 0.9f;
                if (stateTimer >= WindupTime)
                {
                    NPC t = targetIndex >= 0 && targetIndex < Main.maxNPCs ? Main.npc[targetIndex] : null;
                    Vector2 aim = t != null && t.active ? t.Center : Projectile.Center + Projectile.velocity;
                    if (!Main.dedServ)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                                CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), Color.White, 0.8f);
                            v.Opacity = 0.5f;
                        }
                        SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.7f, Pitch = 0.2f }, portalPos);
                    }
                    //穿门:蛇身从门位重新展开
                    Projectile.Center = portalPos;
                    history.Clear();
                    Projectile.velocity = (aim - portalPos).SafeNormalize(Vector2.UnitX) * 21f;
                    state = 2;
                    stateTimer = 0;
                    Projectile.netUpdate = true;
                }
            }
            else
            {
                //俯冲:直线穿过目标,末段减速回归
                stateTimer++;
                if (stateTimer >= DiveTime)
                {
                    state = 0;
                    stateTimer = 0;
                    attackCd = AttackCooldown;
                    Projectile.netUpdate = true;
                }
            }

            //位置历史(体节推导)
            if (history.Count == 0 || CEUtils.getDistance(history[^1], Projectile.Center) > 2f)
            {
                history.Add(Projectile.Center);
                if (history.Count > 90)
                {
                    history.RemoveAt(0);
                }
            }
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.3f, 0.12f, 0.5f);
        }

        private NPC FindTarget(Player player)
        {
            if (player.MinionAttackTargetNPC >= 0 && Main.npc[player.MinionAttackTargetNPC].active
                && Main.npc[player.MinionAttackTargetNPC].CanBeChasedBy(Projectile))
            {
                return Main.npc[player.MinionAttackTargetNPC];
            }
            return CEUtils.FindTarget_HomingProj(Projectile, player.Center, SeekRange);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 120, 2, 600, 20);
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.NPCHit4 with { Volume = 0.6f, Pitch = 0.4f }, target.Center);
            }
        }

        /// <summary>沿位置历史按弧长取体节位置(头 → 节 → 尾)。</summary>
        private List<(Vector2 pos, float rot)> GetSegments()
        {
            var segs = new List<(Vector2, float)>();
            Vector2 prev = Projectile.Center;
            float need = SegmentSpacing;
            int hi = history.Count - 1;
            Vector2 lastPlaced = Projectile.Center;
            for (int s = 0; s < SegmentCount + 1 && hi > 0; s++)
            {
                while (hi > 0)
                {
                    float d = CEUtils.getDistance(prev, history[hi - 1]);
                    if (d >= need)
                    {
                        Vector2 pos = Vector2.Lerp(prev, history[hi - 1], need / d);
                        segs.Add((pos, (lastPlaced - pos).ToRotation()));
                        lastPlaced = pos;
                        prev = pos;
                        need = SegmentSpacing;
                        break;
                    }
                    need -= d;
                    prev = history[hi - 1];
                    hi--;
                }
            }
            return segs;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float alpha = state == 1 ? 1f - stateTimer / (float)WindupTime * 0.6f : 1f;
            var segs = GetSegments();
            //尾 → 节 → 头的次序压叠绘制(掠食者约定:画布上方 = 朝向,+PiOver2)
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                Texture2D tex = i == segs.Count - 1 && segs.Count > SegmentCount ? tailTex.Value : bodyTexs[i % 2];
                Main.spriteBatch.Draw(tex, segs[i].pos - Main.screenPosition, null, lightColor * alpha,
                    segs[i].rot + MathHelper.PiOver2, tex.Size() / 2, Scale, SpriteEffects.None, 0);
            }
            Texture2D head = headTex.Value;
            Main.spriteBatch.Draw(head, Projectile.Center - Main.screenPosition, null, lightColor * alpha,
                Projectile.rotation + MathHelper.PiOver2, head.Size() / 2, Scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
