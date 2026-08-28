using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Core;
using CalamityEntropy.Core.Weapons;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 虚神魔镰(void-invasion.md §5.4):教皇掉落的近战镰,BaseSwing 三段连斩。
    /// 蓄势(命中计数 10)满 → 释放"魔镰冲斩":朝指针短冲 + 二连旋斩(教皇 P1-4 玩家版,
    /// 旋斩复用 PopeScytheSlash 判定与刀光姿势的友方版)。
    /// 伤害定标:TheDeadCut 275@16t(深渊亡魂档)↔ Nemesis 360@18t(终局合成挥砍,带弹幕补伤)
    /// 之间,纯弧面单判取 460@20t 偏下成立。
    /// </summary>
    public class VoidGodScythe : ModItem, ICEChargeWeapon
    {
        //蓄势:近战连斩走命中计数(10 次命中就绪),冲斩伤害乘数 1.5
        public CEChargeProfile ChargeProfile => CEChargeProfile.HitCount(10, 1.5f);

        private int comboIndex;

        public override void SetDefaults()
        {
            Item.height = 150;
            Item.width = 150;
            Item.damage = 460;
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = Item.useTime = 20;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 6.5f;
            Item.UseSound = null;
            Item.autoReuse = true;
            Item.maxStack = 1;
            Item.value = Item.buyPrice(platinum: 2, gold: 20);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.shootSpeed = 16f;
            Item.SetKnifeHeld<VoidGodScytheHeld>();
            comboIndex = 0;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            if (CEChargeWeapon.TryConsume(player, Item))
            {
                //蓄势满:魔镰冲斩(held 侧 ai[0]=1 分支)
                int p = Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI, 1f, 0f);
                if (p.WithinBounds(Main.maxProjectiles))
                {
                    CEChargeWeapon.Empower(p);
                }
                comboIndex = 0;
                return false;
            }
            //三段连斩:0 上挥 / 1 下挥 / 2 重挥(§5.4)
            Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI, 0f, comboIndex);
            if (++comboIndex > 2)
            {
                comboIndex = 0;
            }
            return false;
        }
    }

    internal class VoidGodScytheHeld : BaseKnife
    {
        public override int TargetID => ModContent.ItemType<VoidGodScythe>();
        public override string trailTexturePath => EffectLoader.AssetPath + "MotionTrail3";
        public override string gradientTexturePath => EffectLoader.AssetPath + "Extra/llBar2";

        /// <summary>大招模式(魔镰冲斩)</summary>
        public bool UltMode => Projectile.ai[0] == 1;
        /// <summary>连斩段位(0/1/2)</summary>
        public int ComboIndex => (int)Projectile.ai[1];

        //冲斩节拍(以 updateCount 为单位的逻辑帧)
        private const int DashStart = 2;
        private const int DashEnd = 12;
        private const int SpinBeat1 = 8;
        private const int SpinBeat2 = 22;
        private const float DashSpeed = 17f;

        private Vector2 dashDir = Vector2.UnitX;
        private int spinCounter = 0;

        public override void SetKnifeProperty()
        {
            Projectile.width = Projectile.height = 160;
            overOffsetCachesRoting = MathHelper.ToRadians(6);
            IgnoreImpactBoxSize = true;
            drawTrailHighlight = false;
            canDrawSlashTrail = true;
            Incandescence = true;
            drawTrailBtommWidth = 56;
            drawTrailTopWidth = 120;
            distanceToOwner = 105;
            unitOffsetDrawZkMode = -8;
            SwingData.starArg = 58;
            SwingData.baseSwingSpeed = 4.2f;
            ShootSpeed = 16;
            Length = 112;
        }

        public override void KnifeInitialize()
        {
            //连斩段位配置只在初始化做一次(SwingData.starArg 是累加语义,禁止逐帧改,镜像 UpAndDown 的一次性翻转)
            if (UltMode)
            {
                dashDir = (InMousePos - Owner.Center).SafeNormalize(Vector2.UnitX * Owner.direction);
                maxSwingTime = 30;
                SwingData.maxSwingTime = 30;
                OtherMeleeSize = 1.3f;
                SwingData.baseSwingSpeed = 6f;
                SoundEngine.PlaySound(SoundID.Item71 with { Volume = 1.1f, Pitch = -0.2f }, Owner.Center);
            }
            else if (ComboIndex == 1)
            {
                //第二段:反向下挥(镜像 BaseKnife UpAndDown 的翻转姿势)
                inDrawFlipdiagonally = true;
                SwingData.starArg += 120;
                SwingData.baseSwingSpeed = -4.2f;
            }
            else if (ComboIndex == 2)
            {
                //第三段:重挥,更大更快(§5.4 三段连斩的收拍)
                OtherMeleeSize = 1.18f;
                SwingData.baseSwingSpeed = 5f;
            }
        }

        public override bool PreInOwnerUpdate()
        {
            if (UltMode)
            {
                //魔镰冲斩:短冲期间锁一段大幅旋挥,二连旋斩在节拍处生成
                //(绝对赋值幂等,按 Nemesis 姿势逐帧重申,防基类逐帧重算)
                maxSwingTime = 30;
                SwingData.maxSwingTime = 30;
                OtherMeleeSize = 1.3f;
                SwingData.baseSwingSpeed = 6f;
                if (Time >= DashStart * updateCount && Time <= DashEnd * updateCount)
                {
                    Owner.velocity = dashDir * DashSpeed;
                    Owner.GiveImmuneTimeForCollisionAttack(6);
                    if (Time % (2 * updateCount) == 0 && !Main.dedServ)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(Owner.Center + CEUtils.randomPointInCircle(30),
                            -dashDir * Main.rand.NextFloat(2f, 5f), Color.White, 1f);
                        v.Opacity = 0.6f;
                    }
                }
                if ((Time == SpinBeat1 * updateCount || Time == SpinBeat2 * updateCount) && Main.myPlayer == Projectile.owner)
                {
                    //二连旋斩:左右交替旋向
                    spinCounter++;
                    Projectile.NewProjectile(Source, Owner.Center, dashDir * 0.02f,
                        ModContent.ProjectileType<VoidScytheSpinSlash>(), Projectile.damage, Projectile.knockBack,
                        Owner.whoAmI, spinCounter % 2 == 0 ? 1f : -1f);
                }
                return true;
            }
            return base.PreInOwnerUpdate();
        }

        public override void MeleeEffect()
        {
            if (Main.rand.NextBool(3))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center + CEUtils.randomPointInCircle(40),
                    Projectile.velocity * 0.2f, Color.White, 0.8f);
                v.Opacity = 0.45f;
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 90, 2, 600, 20);
        }
    }

    /// <summary>
    /// 魔镰旋斩(友方版,PopeScytheSlash 玩家版):吸附玩家,12t 锐利缓出扫过约 250°,
    /// 刃线判定与双层顶点条带刀光同款。ai[0] = 旋向(±1);基准角借初速度通道原生同步。
    /// </summary>
    public class VoidScytheSpinSlash : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int SweepTime = 12;
        public const float BladeReach = 165f;
        public const float SweepArc = 4.4f; //≈250°

        public int SweepDir => Projectile.ai[0] >= 0 ? 1 : -1;

        private readonly List<Vector2> tipTrail = new();
        private readonly List<float> tipRots = new();

        private float Timer => SweepTime + 2 - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 600;
        }

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, -1);
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.timeLeft = SweepTime + 2;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>当前扫掠角:锐利缓出(poly(5)),是一记斩而不是波。</summary>
        public float SweepAngle
        {
            get
            {
                float p = MathHelper.Clamp(Timer / SweepTime, 0f, 1f);
                float ease = 1f - (float)Math.Pow(1f - p, 5);
                return Projectile.rotation + SweepDir * (-SweepArc * 0.5f + SweepArc * ease);
            }
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item71 with { Volume = 1f, Pitch = 0.3f }, Projectile.Center);
                }
            }
            Projectile.Center = Projectile.GetOwner().MountedCenter;

            Vector2 tip = Projectile.Center + SweepAngle.ToRotationVector2() * BladeReach;
            tipTrail.Add(tip);
            tipRots.Add(SweepAngle);
            if (tipTrail.Count > 10)
            {
                tipTrail.RemoveAt(0);
                tipRots.RemoveAt(0);
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 120, 2, 600, 20);
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 c = Projectile.GetOwner().MountedCenter;
            Vector2 tip = c + SweepAngle.ToRotationVector2() * BladeReach;
            return CEUtils.LineThroughRect(c + (tip - c) * 0.2f, tip, targetHitbox, 55);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (tipTrail.Count < 3)
            {
                return false;
            }
            SpriteBatch sb = Main.spriteBatch;
            GraphicsDevice gd = Main.graphics.GraphicsDevice;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            //双层条带(紫底 + 白芯),姿势与 PopeScytheSlash 同款
            for (int layer = 0; layer < 2; layer++)
            {
                List<ColoredVertex> ve = new List<ColoredVertex>();
                Color baseColor = layer == 0 ? Color.Purple : Color.White;
                for (int i = 0; i < tipTrail.Count; i++)
                {
                    float prog = (float)i / tipTrail.Count;
                    Color b = Color.Lerp(baseColor * 0.02f, baseColor, prog);
                    Vector2 inward = tipRots[i].ToRotationVector2() * -95f;
                    ve.Add(new ColoredVertex(tipTrail[i] - Main.screenPosition, new Vector3(prog, 1, 1), b));
                    ve.Add(new ColoredVertex(tipTrail[i] + inward - Main.screenPosition, new Vector3(prog, 0, 1), b));
                }
                gd.Textures[0] = layer == 0 ? CEExtraAssets.white : CEExtraAssets.SwordSlashTexture;
                gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
