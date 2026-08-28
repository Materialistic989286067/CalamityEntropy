using CalamityEntropy.Common;
using CalamityEntropy.Content.Items.Books;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Core.Graphics;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 堕虚邪典(void-invasion.md §5.4):教皇掉落的熵之书。
    /// 常态连射魔焰弹(魔眼弹贴图友方版,轻微追踪);持续施放时约每 3s 自动向最近敌人
    /// 伸出一条小型死怨铁索抽击(教皇招牌下放,PopeChain 视觉的友方短版)。
    /// 伤害定标:CosmicBlessing 110(深渊亡魂档)↔ VoidOde 200(终局档)插值偏下取 150;
    /// SlotCount 对照 VoidOde 同档取 6。
    /// </summary>
    public class FallenVoidCodex : EntropyBook
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.damage = 150;
            Item.useAnimation = Item.useTime = 7;
            Item.crit = 10;
            Item.mana = 9;
            Item.shootSpeed = 15;
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.value = Item.buyPrice(platinum: 2, gold: 20);
        }

        [VaultLoaden("CalamityEntropy/Content/UI/EntropyBookUI/BookMark6")]
        internal static Asset<Texture2D> BookMarkSlotTex;
        public override Texture2D BookMarkTexture => BookMarkSlotTex.Value;
        public override int HeldProjectileType => ModContent.ProjectileType<FallenVoidCodexHeld>();
        public override int SlotCount => 6;
    }

    public class FallenVoidCodexHeld : EntropyBookHeldProjectile
    {
        /// <summary>铁索抽击间隔(§5.4:约每 3s)</summary>
        public const int ChainInterval = 180;
        public const float ChainSeekRange = 560f;

        private int chainCd = 60;

        //无翻页立绘美术,持握立绘复用 item 贴图(§6.3 缺口临时方案,待美术)
        [VaultLoaden("CalamityEntropy/Content/Items/Weapons/VoidInvasion/FallenVoidCodex")]
        private static Asset<Texture2D> bookTex;
        public override Texture2D getTexture() => bookTex.Value;
        public override void playTurnPageAnimation()
        {
            playPageSound();
        }

        public override EBookStatModifer getBaseModifer()
        {
            var m = base.getBaseModifer();
            m.Homing += 0.9f;
            m.HomingRange += 0.5f;
            return m;
        }

        public override float randomShootRotMax => 0.16f;
        public override int baseProjectileType => ModContent.ProjectileType<CodexEyeBolt>();

        public override EBookProjectileEffect getEffect()
        {
            return new FallenVoidCodexBaseEffect();
        }

        public override void AI()
        {
            base.AI();
            //铁索抽击:施放期间由所有者端结算,弹幕生成原生同步
            chainCd--;
            if (Main.myPlayer == Projectile.owner && active && Opened && chainCd <= 0)
            {
                NPC target = CEUtils.FindTarget_HomingProj(Projectile, Projectile.GetOwner().Center, ChainSeekRange);
                if (target != null)
                {
                    chainCd = ChainInterval;
                    Vector2 dir = (target.Center - Projectile.GetOwner().Center).SafeNormalize(Vector2.UnitX);
                    int dmg = CauculateProjectileDamage(1.5f);
                    Projectile.NewProjectile(Projectile.GetOwner().GetSource_ItemUse(bookItem),
                        Projectile.GetOwner().Center, dir, ModContent.ProjectileType<CodexVoidChain>(),
                        dmg, 5f, Projectile.owner);
                }
            }
        }
    }

    public class FallenVoidCodexBaseEffect : EBookProjectileEffect
    {
        public override void OnHitNPC(Projectile projectile, NPC target, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 120, 2, 600, 20);
        }
    }

    /// <summary>魔焰弹友方版:复用魔眼弹贴图与视觉,走熵之书弹幕基类吃书签加成。</summary>
    public class CodexEyeBolt : EBookBaseProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/MagicEyeBolt";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public override void SetDefaults()
        {
            base.SetDefaults();
            Projectile.width = 22;
            Projectile.height = 22;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 240;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            base.AI();
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.5f, 0.2f, 0.75f);
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center - Projectile.velocity * 0.5f,
                    -Projectile.velocity * 0.1f, new Color(190, 90, 255), 0.35f);
                p.Configure(0.85f, lifetime: 12);
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
            {
                return;
            }
            for (int i = 0; i < 5; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), Color.White, 0.8f);
                v.Opacity = Main.rand.Next(30, 70) * 0.01f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr);
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, Color.White,
                Projectile.rotation, tex.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
                new Color(190, 90, 255) * 0.6f, 0, glow.Size() / 2, 0.5f * Projectile.scale, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }

    /// <summary>
    /// 小型死怨铁索(友方版,PopeChain 视觉短版):自玩家伸出 8t → 定格 8t → 收回 10t,
    /// 判定在伸出与定格期;方向借初速度通道原生同步,基点逐帧吸附玩家。
    /// </summary>
    public class CodexVoidChain : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/MaliceClaw";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int ExtendTime = 8;
        public const int HoldTime = 8;
        public const int RetractTime = 10;
        public const int TotalLife = ExtendTime + HoldTime + RetractTime;
        public const float ChainLength = 360f;

        private float Timer => TotalLife - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 600;
        }

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Magic, false, -1);
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.timeLeft = TotalLife;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>伸出比例:锐利缓出突刺,收回平滑缓入(镜像 PopeChain 包络)。</summary>
        public float ExtendProgress
        {
            get
            {
                float t = Timer;
                if (t < ExtendTime)
                {
                    float p = t / ExtendTime;
                    return 1f - (1f - p) * (1f - p) * (1f - p);
                }
                if (t < ExtendTime + HoldTime)
                {
                    return 1f;
                }
                float r = (t - ExtendTime - HoldTime) / RetractTime;
                return 1f - r * r;
            }
        }

        public Vector2 TipPos => Projectile.Center + Projectile.rotation.ToRotationVector2() * ChainLength * ExtendProgress;

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    //突刺音与 PopeChain 同款,音高抬高作友方版区分
                    Terraria.Audio.SoundEngine.PlaySound(SoundID.Item71 with { Volume = 0.8f, Pitch = 0.2f }, Projectile.Center);
                }
            }
            //基点吸附玩家(鞭式手感)
            Projectile.Center = Projectile.GetOwner().MountedCenter;
            if (Timer >= 0)
            {
                Lighting.AddLight(Vector2.Lerp(Projectile.Center, TipPos, 0.7f), 0.4f, 0.15f, 0.65f);
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (Timer >= ExtendTime + HoldTime)
            {
                return false;
            }
            return CEUtils.LineThroughRect(Projectile.Center, TipPos, targetHitbox, 24);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 120, 2, 600, 20);
            if (!Main.dedServ)
            {
                for (int i = 0; i < 8; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(target.Center,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 6f), Color.White, 0.9f);
                    v.Opacity = Main.rand.Next(30, 70) * 0.01f;
                }
            }
        }

        private float StripWidth(float completionRatio, Vector2 position)
        {
            return MathHelper.Lerp(12f, 7f, completionRatio);
        }

        private Color StripColor(float completionRatio, Vector2 position)
        {
            return Color.Lerp(new Color(90, 30, 160), new Color(200, 120, 255), completionRatio) * 0.8f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float ext = ExtendProgress;
            if (ext <= 0.02f)
            {
                return false;
            }
            Vector2 tip = TipPos;
            Vector2 dir = Projectile.rotation.ToRotationVector2();

            //链体条带(基点 → 链头,镜像 PopeChain)
            List<Vector2> points = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                points.Add(Vector2.Lerp(Projectile.Center, tip, i / 10f));
            }
            GameShaders.Misc["CalamityEntropy:TrailStreak"].SetShaderTexture(Assets.Register.CEExtraAssets.StreakFadedAsset);
            CEPrimitiveRenderer.RenderTrail(points, new CEPrimitiveSettings(StripWidth, StripColor, (_, _) => Vector2.Zero,
                shader: GameShaders.Misc["CalamityEntropy:TrailStreak"]), 16);

            //链环矩形(暗紫小节)
            Main.spriteBatch.UseBlendState(BlendState.AlphaBlend);
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            float linkGap = 22f;
            int linkCount = (int)(ChainLength * ext / linkGap);
            for (int i = 0; i < linkCount; i++)
            {
                Vector2 lp = Projectile.Center + dir * (i + 0.5f) * linkGap;
                float linkRot = Projectile.rotation + (i % 2 == 0 ? 0f : MathHelper.PiOver2);
                Main.spriteBatch.Draw(pixel, lp - Main.screenPosition, new Rectangle(0, 0, 1, 1), new Color(45, 18, 80) * 0.95f,
                    linkRot, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), SpriteEffects.None, 0);
                Main.spriteBatch.Draw(pixel, lp - Main.screenPosition, new Rectangle(0, 0, 1, 1), new Color(150, 80, 220) * 0.5f,
                    linkRot, new Vector2(0.5f, 0.5f), new Vector2(7f, 2f), SpriteEffects.None, 0);
            }

            //链头恶念之爪(源图爪尖朝左,镜像后沿链轴指向)
            Texture2D claw = TextureAssets.Projectile[Projectile.type].Value;
            Main.spriteBatch.Draw(claw, tip - Main.screenPosition, null, Color.White, Projectile.rotation,
                new Vector2(claw.Width * 0.35f, claw.Height * 0.5f), 0.62f, SpriteEffects.FlipHorizontally, 0);

            //链头辉光
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            Main.spriteBatch.Draw(glow, tip - Main.screenPosition, null, new Color(190, 100, 255) * 0.7f, 0, glow.Size() / 2, 0.6f, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
