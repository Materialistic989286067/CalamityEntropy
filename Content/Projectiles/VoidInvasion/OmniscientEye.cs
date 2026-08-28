using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 全知之眼(void-invasion.md §4.2 P2-6/P2-6s;全知之眼.png 132x100):
    /// ai[0] = 教皇 whoAmI;ai[1] = 模式:0~2 = P2-6 眼(索引即 120° 均布方位),
    /// 10~12 = P2-6s 分身眼(10 真身/11、12 分身,绕位公转,只接触判定不吐弹)。
    /// P2-6 眼:浮现 20t → 锚定教皇周围吐弹 240t(0.05rad/t 自转,每 8t 沿朝向 1 枚直飞魔焰弹)
    /// → 教皇进下一招后眼再公转 300t(接触判定 170)→ 碎裂。
    /// 位置与朝向全部由 (教皇位置 + 本地年龄) 确定性推导,吐弹在服务端。
    /// </summary>
    public class OmniscientEye : ModProjectile
    {
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int AppearTime = 20;
        public const int SpitTime = 240;
        public const int OrbitTime = 300;
        public const int ShatterTime = 15;
        public const int TotalLife = AppearTime + SpitTime + OrbitTime + ShatterTime; //575
        public const float AnchorRadius = 170f;
        public const float CloneOrbitRadius = 110f;

        public int OwnerIndex => (int)Projectile.ai[0];
        public bool CloneMode => Projectile.ai[1] >= 10;
        public int EyeIndex => CloneMode ? (int)Projectile.ai[1] - 10 : (int)Projectile.ai[1];

        private float age;
        private bool shattering;
        private float shatterCounter;
        /// <summary>吐弹枪口闪余量(双端由确定性吐弹拍各自置位,纯视觉)</summary>
        private float muzzleFlash;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 800;
        }

        public override void SetDefaults()
        {
            Projectile.width = 90;
            Projectile.height = 70;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 90;
        }

        private VoidPope Pope
        {
            get
            {
                if (OwnerIndex < 0 || OwnerIndex >= Main.maxNPCs)
                {
                    return null;
                }
                NPC n = Main.npc[OwnerIndex];
                return n.active && n.ModNPC is VoidPope pope ? pope : null;
            }
        }

        /// <summary>浮现比例(0→1)。</summary>
        private float AppearP => MathHelper.Clamp(age / AppearTime, 0f, 1f);

        /// <summary>自转朝向(吐弹方向,双端同式)。</summary>
        public float SelfRot => EyeIndex * MathHelper.TwoPi / 3f + (age - AppearTime) * 0.05f;

        public override void AI()
        {
            VoidPope pope = Pope;
            if (pope == null)
            {
                if (!shattering)
                {
                    BeginShatter();
                }
            }
            age++;
            Projectile.timeLeft = 90;

            if (shattering)
            {
                shatterCounter++;
                if (shatterCounter >= ShatterTime)
                {
                    Projectile.Kill();
                }
                return;
            }

            if (CloneMode)
            {
                CloneModeAI(pope);
            }
            else
            {
                SpitModeAI(pope);
            }

            muzzleFlash = Math.Max(muzzleFlash - 1f, 0f);
            Lighting.AddLight(Projectile.Center, 0.5f * AppearP, 0.2f * AppearP, 0.8f * AppearP);

            //浮现期粒子内聚
            if (!Main.dedServ && age < AppearTime && Main.rand.NextBool(2))
            {
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(50f, 140f);
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + offset, -offset * 0.1f, new Color(190, 100, 255), 0.4f);
                p.Configure(0.85f, lifetime: 12);
            }
        }

        /// <summary>P2-6:锚定吐弹 240t → 绕教皇公转 300t → 碎裂。</summary>
        private void SpitModeAI(VoidPope pope)
        {
            NPC owner = pope.NPC;
            float baseAng = -MathHelper.PiOver2 + EyeIndex * MathHelper.TwoPi / 3f;
            if (age <= AppearTime + SpitTime)
            {
                //吐弹段:锚定教皇周围固定方位
                Projectile.Center = owner.Center + baseAng.ToRotationVector2() * AnchorRadius;
                Projectile.rotation = SelfRot;
                if (age > AppearTime && (int)(age - AppearTime) % 8 == 0)
                {
                    //吐弹拍双端确定性一致:弹幕只在服务端生成,枪口闪双端各自演出
                    muzzleFlash = 4f;
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int damage = (int)(owner.defDamage * 0.5f + 0.5f); //魔焰弹 170 经典档(敌对弹幕命中 ×2)
                        Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center,
                            SelfRot.ToRotationVector2() * 9.5f, ModContent.ProjectileType<MagicEyeBolt>(), damage, 3f, -1, 0f);
                    }
                }
            }
            else if (age <= AppearTime + SpitTime + OrbitTime)
            {
                //公转段(§4.2:眼再存活 5s,绕教皇公转作接触判定)
                float orbitAge = age - AppearTime - SpitTime;
                float ang = baseAng + orbitAge * 0.02f;
                Vector2 want = owner.Center + ang.ToRotationVector2() * (AnchorRadius + 20f);
                Projectile.Center = Vector2.Lerp(Projectile.Center, want, 0.2f);
                Projectile.rotation = ang + MathHelper.PiOver2;
            }
            else
            {
                BeginShatter();
            }
        }

        /// <summary>P2-6s:绕真身/分身位公转,只接触判定;教皇退出该招即碎。</summary>
        private void CloneModeAI(VoidPope pope)
        {
            NPC owner = pope.NPC;
            if (pope.State != VoidPope.PopeState.P2TrinityEye || age > TotalLife)
            {
                BeginShatter();
                return;
            }
            Vector2 anchor = owner.Center;
            if (EyeIndex > 0 && owner.HasValidTarget)
            {
                anchor = pope.ClonePos(Main.player[owner.target].Center, EyeIndex - 1);
            }
            float ang = EyeIndex * 2.1f + age * 0.045f;
            Projectile.Center = anchor + ang.ToRotationVector2() * CloneOrbitRadius;
            Projectile.rotation = ang + MathHelper.PiOver2;
        }

        private void BeginShatter()
        {
            shattering = true;
            shatterCounter = 0;
            if (Main.dedServ)
            {
                return;
            }
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.9f, Pitch = -0.3f }, Projectile.Center);
            for (int i = 0; i < 16; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 8f), Color.White, 0.9f);
                v.Opacity = Main.rand.Next(30, 80) * 0.01f;
            }
            var flash = PRTLoader.NewParticle<PRT_Light>(Projectile.Center, Vector2.Zero, new Color(200, 120, 255), 1.4f);
            flash.Configure(0.82f, lifetime: 12);
        }

        public override bool CanHitPlayer(Player target)
        {
            if (shattering || age < AppearTime)
            {
                return false;
            }
            //P2-6 眼:接触判定只在公转段;分身眼:浮现后全程(§4.2)
            if (!CloneMode && age <= AppearTime + SpitTime)
            {
                return false;
            }
            return true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float alpha = AppearP;
            if (shattering)
            {
                alpha *= 1f - shatterCounter / ShatterTime;
            }
            if (alpha <= 0.01f)
            {
                return false;
            }
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            //吐弹后座:眼体沿吐向反挫几像素,弹簧回稳(发射有质量)
            Vector2 pos = Projectile.Center - dir * (muzzleFlash * 1.6f) - Main.screenPosition;
            float scale = (0.6f + 0.4f * AppearP) * Projectile.scale;
            //睁眼拍(演出二迭):浮现期纵向从眯到睁,弹性过冲
            float openP = AppearP >= 1f ? 1f
                : 1f - (float)(Math.Pow(2, -10 * AppearP) * Math.Cos(AppearP * 9.4f));
            Vector2 eyeScale = new Vector2(scale, scale * MathHelper.Clamp(0.12f + 0.88f * openP, 0.12f, 1.06f));
            Main.spriteBatch.Draw(tex, pos, null, Color.White * alpha, Projectile.rotation, tex.Size() / 2, eyeScale, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            float pulse = 1f + 0.12f * (float)Math.Sin(age * 0.3f);
            Main.spriteBatch.Draw(glow, pos, null, new Color(190, 100, 255) * (0.55f * alpha), 0, glow.Size() / 2, 0.85f * pulse, SpriteEffects.None, 0);
            //虹膜发光(演出二迭:瞳位沿注视向前移,亮白芯 + 紫晕,眼是"活"的)
            Vector2 irisPos = pos + dir * 16f * scale;
            Main.spriteBatch.Draw(glow, irisPos, null, new Color(230, 170, 255) * (0.8f * alpha), 0, glow.Size() / 2, 0.34f * pulse, SpriteEffects.None, 0);
            Main.spriteBatch.Draw(glow, irisPos, null, Color.White * (0.65f * alpha), 0, glow.Size() / 2, 0.18f * pulse, SpriteEffects.None, 0);
            //吐弹枪口闪(4t 衰减的口部炸花)
            if (muzzleFlash > 0f)
            {
                float mf = muzzleFlash / 4f;
                Vector2 muzzlePos = pos + dir * 44f * scale;
                Main.spriteBatch.Draw(glow, muzzlePos, null, Color.White * (0.85f * mf * alpha), 0, glow.Size() / 2, 0.55f * mf + 0.15f, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
