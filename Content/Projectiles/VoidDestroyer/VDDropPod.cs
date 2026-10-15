using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 投送舱:生成在落点正上方 ReinforcePodFallHeight 处,按龄二次缓入砸到落点(各端同算,不靠速度同步);
    /// 坠落全程落点环收紧,落地那一帧起 6 帧接触判定,服务端在落点放一名教徒。
    /// ai[0] 目标玩家,ai[1] 本体 whoAmI,ai[2] 落点 y
    /// </summary>
    public class VDDropPod : VDDepthProjectile
    {
        public const int ShatterFrames = 20;
        public static readonly Color ShellColor = new Color(150, 80, 230);

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DebuffType => ModContent.BuffType<VoidFire>();
        public override int DefaultTimeLeft => 300;

        private float startY;
        /// <summary>落地帧龄(-1 未落地)</summary>
        private int landedAge = -1;
        private bool released;

        public bool Landed => landedAge >= 0;
        public bool Impacting => landedAge >= 0 && landedAge < VDDirector.ReinforcePodImpactFrames;
        public Vector2 Landing => new Vector2(Projectile.Center.X, Projectile.ai[2]);

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 1200;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 40;
            Projectile.height = 40;
        }

        public override bool ShouldUpdatePosition() => false;

        protected override void DepthAI() {
            Projectile.velocity = Vector2.Zero;
            Projectile.localAI[1]++;
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                startY = Projectile.Center.Y;
                if (!Main.dedServ) {
                    VDVfx.HoloBurst(Projectile.Center, VDDirector.RimSummonGold);
                }
            }
            if (!Landed) {
                float t = MathHelper.Clamp(Projectile.localAI[1] / VDDirector.ReinforcePodFrames, 0f, 1f);
                Projectile.Center = new Vector2(Projectile.Center.X, MathHelper.Lerp(startY, Projectile.ai[2], t * t));
                if (t >= 1f) {
                    Land();
                }
                else if (!Main.dedServ && Main.rand.NextBool(2)) {
                    VDVfx.VoidPuff(Projectile.Center + CEUtils.randomPointInCircle(14f) - new Vector2(0f, 20f), new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -1.5f), 0.9f, 0.5f);
                }
                Lighting.AddLight(Projectile.Center, VDDirector.RimSummonGold.ToVector3() * 0.4f);
                return;
            }
            landedAge++;
            Lighting.AddLight(Projectile.Center, VDDirector.RimSummonGold.ToVector3() * (1f - landedAge / (float)ShatterFrames));
            if (landedAge >= ShatterFrames) {
                Projectile.Kill();
            }
        }

        /// <summary>落地:开判定、放教徒、一记冲击</summary>
        private void Land() {
            landedAge = 0;
            if (!released) {
                released = true;
                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    int type = ModContent.NPCType<VoidVanguardCultist>();
                    int n = NPC.NewNPC(Projectile.GetSource_FromAI(), (int)Projectile.Center.X, (int)Projectile.Center.Y, type);
                    if (n < Main.maxNPCs) {
                        int target = (int)Projectile.ai[0];
                        Main.npc[n].target = target >= 0 && target < Main.maxPlayers ? target : Main.npc[n].target;
                        if (Main.netMode == NetmodeID.Server) {
                            NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, n);
                        }
                    }
                }
            }
            if (!Main.dedServ) {
                CEUtils.PlaySound("VoidBomb", 1.1f, Projectile.Center, 4, 0.8f);
                CEUtils.SetShake(Projectile.Center, 5f, 1400f);
                VDVfx.Explosion(Projectile.Center, 0.55f, 20);
                VDVfx.SparkBurst(Projectile.Center, VDDirector.RimSummonGold, 18, 3f, 9f, 26, 0.6f, 1.1f);
                for (int i = 0; i < 10; i++) {
                    Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 5f);
                    VDVfx.VoidPuff(Projectile.Center + v * 3f, v, 1.2f, 0.6f);
                }
            }
        }

        protected override bool? CollidingOnPlane(Rectangle projHitbox, Rectangle targetHitbox) {
            if (!Impacting) {
                return false;
            }
            Vector2 nearest = new Vector2(
                MathHelper.Clamp(Projectile.Center.X, targetHitbox.Left, targetHitbox.Right),
                MathHelper.Clamp(Projectile.Center.Y, targetHitbox.Top, targetHitbox.Bottom));
            return nearest.DistanceSQ(Projectile.Center) <= VDDirector.ReinforcePodImpactRadius * VDDirector.ReinforcePodImpactRadius;
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Texture2D ring = CEUtils.getExtraTex("BloomRing");

            if (Landed) {
                //落地碎裂:环壳撑开淡出,核心熄灭
                float p = landedAge / (float)ShatterFrames;
                spriteBatch.UseAdditive();
                spriteBatch.Draw(ring, drawPos, null, ShellColor * (0.8f * (1f - p)), landedAge * 0.1f, ring.Size() / 2f, 0.55f + 0.9f * p, SpriteEffects.None, 0f);
                spriteBatch.Draw(glow, drawPos, null, VDDirector.RimSummonGold * (0.9f * (1f - p)), 0f, glow.Size() / 2f, 0.5f * (1f - p * 0.5f), SpriteEffects.None, 0f);
                CEUtils.ReSetToEndShader();
                return;
            }
            float t = MathHelper.Clamp(Projectile.localAI[1] / VDDirector.ReinforcePodFrames, 0f, 1f);
            //落点环:坠落全程收紧,告诉站在门上的人会被砸
            VDVfx.DrawDepthMarker(Landing, t, VDDirector.ReinforcePodImpactRadius, VDDirector.RimSummonGold, 0.9f);

            spriteBatch.UseAdditive();
            //尾焰朝上拖,越快越长;舱壳一环一核,越近越亮
            float len = 40f + 120f * t;
            CEUtils.drawLineBetter(Projectile.Center, Projectile.Center - new Vector2(0f, len), VDDirector.RimSummonGold * (0.5f + 0.3f * t), 22f);
            spriteBatch.Draw(glow, drawPos, null, ShellColor * 0.8f, 0f, glow.Size() / 2f, 0.55f, SpriteEffects.None, 0f);
            spriteBatch.Draw(ring, drawPos, null, ShellColor * 0.9f, Main.GlobalTimeWrappedHourly * 2f, ring.Size() / 2f, 0.42f, SpriteEffects.None, 0f);
            spriteBatch.Draw(glow, drawPos, null, VDDirector.RimSummonGold * (0.6f + 0.4f * t), 0f, glow.Size() / 2f, 0.28f, SpriteEffects.None, 0f);
            spriteBatch.Draw(glow, drawPos, null, Color.White * (0.4f + 0.4f * t), 0f, glow.Size() / 2f, 0.12f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
        }
    }
}
