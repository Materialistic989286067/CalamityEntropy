using CalamityEntropy.Content.Items.Weapons.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Particles;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 3 秒引信,半径 ai[1],伤害在 VDNukeExplosion,本体不撞人;ai[0] 目标玩家
    /// 爆炸范围圈跟着弹体画在标记层(平面上、物块之后),最后一秒闪烁
    /// </summary>
    public class VDVoidNuke : VDDepthProjectile
    {
        public const int FuseTime = 180;
        public static readonly Color GlowColor = new Color(200, 70, 255);

        public float Radius => Projectile.ai[1] > 0 ? Projectile.ai[1] : VDDirector.NukeRadius;
        public override int DefaultTimeLeft => FuseTime;
        /// <summary>范围圈自己画,不用基类的收缩小环;但要走标记层(平面上、物块之后)所以恒为真</summary>
        public override bool WantsMarker => true;
        protected override float WhooshStrength => 0f;

        public override void SetExtraDefaults() {
            Projectile.width = 20;
            Projectile.height = 20;
        }

        protected override void DepthAI() {
            Player target = TargetPlayer(0);
            if (target != null) {
                float cur = Projectile.velocity.ToRotation();
                float want = (target.Center - Projectile.Center).ToRotation();
                float diff = MathHelper.WrapAngle(want - cur);
                float turn = MathHelper.Clamp(diff, -0.021f, 0.021f);
                Projectile.velocity = (cur + turn).ToRotationVector2() * Math.Max(Projectile.velocity.Length(), 6f);
            }
            //贴图弹头朝下,喷口朝上
            Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * 0.6f);

            if (Main.dedServ) {
                return;
            }
            //最后 60 帧闪烁加快,并给一声警报
            if (Projectile.timeLeft == 60 || Projectile.timeLeft == 30 || Projectile.timeLeft == 12) {
                CEUtils.PlaySound("VoidAnticipation", 1.4f, Projectile.Center, 4, 0.7f);
            }
            if (Main.rand.NextBool(2)) {
                Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.UnitY);
                var p = PRTLoader.NewParticle<PRT_Void>(Projectile.Center + back * 30f, back * Main.rand.NextFloat(1f, 3f), Color.White, 1.1f);
                p.Opacity = 0.6f;
                p.ad = 0.035f;
            }
        }

        protected override bool? CollidingOnPlane(Rectangle projHitbox, Rectangle targetHitbox) => false;

        public override void OnKill(int timeLeft) {
            //核弹只有引信走完才爆炸,阶段切换时的清场 Kill 不引爆
            if (IsServer && timeLeft <= 1) {
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, Vector2.Zero, ModContent.ProjectileType<VDNukeExplosion>(), Projectile.damage, 0f, Main.myPlayer, Radius);
            }
        }

        /// <summary>闪烁强度:0~1,随引信剩余时间越短闪得越快</summary>
        private float Blink() {
            float t = Projectile.timeLeft;
            float freq = t > 60 ? 4f : t > 30 ? 9f : 18f;
            return 0.5f + 0.5f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * freq * MathHelper.TwoPi);
        }

        /// <summary>爆炸范围:淡紫填充 + 外圈,最后一秒闪烁;画在平面标记层,与弹体所在深度无关</summary>
        protected override void DrawMarker(SpriteBatch spriteBatch) {
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float blink = Blink();
            float radius = Radius;
            spriteBatch.UseAdditive();
            Texture2D circle = CEUtils.getExtraTex("Circle");
            float fill = 0.06f + 0.06f * blink * (Projectile.timeLeft <= 60 ? 1f : 0.3f);
            spriteBatch.Draw(circle, drawPos, null, GlowColor * fill, 0f, circle.Size() / 2f, radius * 2f / 300f, SpriteEffects.None, 0f);
            Texture2D ring = CEUtils.getExtraTex("BloomRing");
            float ringAlpha = 0.35f + 0.45f * blink;
            spriteBatch.Draw(ring, drawPos, null, new Color(230, 150, 255) * ringAlpha, 0f, ring.Size() / 2f, radius * 2f / 140f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
        }

        private static readonly Vector2[] exhaust = new Vector2[TrailLength];

        /// <summary>尾焰带 → 闪烁弹体光 → 警示描边(引信最后一秒随闪烁加亮)→ 弹体</summary>
        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float blink = Blink();
            float scale = Projectile.scale * 1.3f;
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Vector2 nozzle = -Projectile.velocity.SafeNormalize(Vector2.UnitY) * 30f * scale;

            spriteBatch.UseAdditive();
            if (trailCount > 2) {
                for (int i = 0; i < trailCount; i++) {
                    exhaust[i] = (i == 0 ? Projectile.Center : trailPos[i]) + nozzle;
                }
                var pts = new ArraySegment<Vector2>(exhaust, 0, trailCount);
                VDWeaponFx.Ribbon(pts, t => 24f - 16f * t, t => Color.Lerp(VDVfx.VoidPink, GlowColor, t) * ((1f - t) * 0.5f));
                VDWeaponFx.Ribbon(pts, t => 8f - 5f * t, t => Color.Lerp(Color.White, VDHomingMissile.ExhaustHot, t) * ((1f - t) * (1f - t) * 0.9f));
            }
            //弹体光:引信在闪,越到最后闪得越快
            spriteBatch.Draw(glow, drawPos, null, GlowColor * (0.5f + 0.5f * blink), 0f, glow.Size() / 2f, (0.34f + 0.12f * blink) * scale, SpriteEffects.None, 0f);
            spriteBatch.Draw(glow, drawPos, null, Color.White * (0.3f + 0.4f * blink), 0f, glow.Size() / 2f, 0.1f * scale, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();

            float rim = Projectile.timeLeft <= 60 ? 0.75f + 0.5f * blink : 1f;
            VDVfx.DrawHazardRim(tex, drawPos, null, Projectile.rotation, tex.Size() / 2f, scale, rim, VDVfx.HazardRimWidth * 1.25f);
            spriteBatch.Draw(tex, drawPos, null, Color.White, Projectile.rotation, tex.Size() / 2f, scale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>核弹爆炸:半径 ai[0] 的圆形判定,前 6 帧有伤害,命中 480 + 虚空之火 3 秒</summary>
    public class VDNukeExplosion : VDHostileProjectile
    {
        public const int Lifetime = 26;
        public const int DamageFrames = 6;
        public static readonly Color GlowColor = new Color(200, 70, 255);

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DebuffType => ModContent.BuffType<Buffs.VoidFire>();
        public override int DefaultTimeLeft => Lifetime;
        public float Radius => Projectile.ai[0];

        public override void SetExtraDefaults() {
            Projectile.width = 40;
            Projectile.height = 40;
        }

        public override void AI() {
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                if (!Main.dedServ) {
                    CEUtils.PlaySound("VoidBomb", 0.8f, Projectile.Center, 3, 1.2f);
                    CEUtils.SetShake(Projectile.Center, 14f, 3000f);
                    VDVfx.Explosion(Projectile.Center, Radius / VDVfx.BlastRadiusUnit, 40, 0.85f);
                    for (int i = 0; i < 60; i++) {
                        Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(6f, 26f);
                        VDVfx.Spark(Projectile.Center, v, GlowColor, Main.rand.NextFloat(0.8f, 1.6f), 1f, 40, gravity: true);
                    }
                    for (int i = 0; i < 40; i++) {
                        Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(4f, 14f);
                        var p = PRTLoader.NewParticle<PRT_Void>(Projectile.Center, v, Color.White, Main.rand.NextFloat(1.2f, 2.2f));
                        p.Opacity = 0.9f;
                        p.ad = 0.02f;
                    }
                }
            }
            Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * 2f);
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            if (Projectile.timeLeft < Lifetime - DamageFrames) {
                return false;
            }
            Vector2 closest = new Vector2(
                MathHelper.Clamp(Projectile.Center.X, targetHitbox.Left, targetHitbox.Right),
                MathHelper.Clamp(Projectile.Center.Y, targetHitbox.Top, targetHitbox.Bottom));
            return Vector2.DistanceSquared(closest, Projectile.Center) <= Radius * Radius;
        }

        public override bool PreDraw(ref Color lightColor) {
            float progress = 1f - Projectile.timeLeft / (float)Lifetime;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Main.spriteBatch.UseAdditive();
            Texture2D circle = CEUtils.getExtraTex("Circle");
            float scale = Radius * 2f / 300f * (0.6f + 0.4f * progress);
            Main.spriteBatch.Draw(circle, drawPos, null, GlowColor * (0.55f * (1f - progress)), 0f, circle.Size() / 2f, scale, SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(circle, drawPos, null, Color.White * (0.35f * (1f - progress)), 0f, circle.Size() / 2f, scale * 0.6f * (1f - progress * 0.5f), SpriteEffects.None, 0f);
            Texture2D ring = CEUtils.getExtraTex("BloomRing");
            Main.spriteBatch.Draw(ring, drawPos, null, new Color(240, 170, 255) * (1f - progress), 0f, ring.Size() / 2f, Radius * 2f / 140f * (0.8f + 0.4f * progress), SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
