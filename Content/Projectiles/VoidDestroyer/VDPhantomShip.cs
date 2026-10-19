using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using VoidDestroyerNPC = CalamityEntropy.Content.NPCs.VoidDestroyer.VoidDestroyer;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 幻影舰队的幻影:门口待机 ai[1] 帧,航道指向交汇点(四扇门的圆心,本体记在 RolledPoints[4],全程不跟人);
    /// 与真身同帧出手,第 FleetCrossFrame 帧穿过交汇点。ai[0] 本体,ai[2] 目标玩家;伤害低于真身接触一档
    /// </summary>
    public class VDPhantomShip : VDDepthProjectile
    {
        [InnoVault.VaultLoaden("CalamityEntropy/Content/NPCs/VoidDestroyer/VoidDestroyerP2")]
        private static Asset<Texture2D> p2Tex = null;

        public override string Texture => "CalamityEntropy/Content/NPCs/VoidDestroyer/VoidDestroyer";
        public override int DebuffType => ModContent.BuffType<VoidFire>();
        public override int DefaultTimeLeft => 200;

        public int AimFrames => (int)Math.Max(Projectile.ai[1], 6f);
        public int Age => (int)Projectile.localAI[1];
        /// <summary>幻影比本体早一帧生成(本体在同一帧换拍),龄比本体拍内计时大 1;出手放在龄 AimFrames + 1,与真身同帧</summary>
        public int LaunchAge => AimFrames + 1;
        public bool Dashing => Age >= LaunchAge && Age < LaunchAge + VDDirector.FleetDashFrames;

        private VoidDestroyerNPC Owner {
            get {
                int idx = (int)Projectile.ai[0];
                if (idx < 0 || idx >= Main.maxNPCs || !Main.npc[idx].active || Main.npc[idx].ModNPC is not VoidDestroyerNPC boss) {
                    return null;
                }
                return boss;
            }
        }

        /// <summary>交汇点:本体在开门那一刻记下的圆心;本体失效时退回玩家当前位置</summary>
        private Vector2 CrossPoint() {
            VoidDestroyerNPC boss = Owner;
            if (boss != null) {
                return boss.Context.RolledPoints[4];
            }
            Player target = TargetPlayer(2);
            return target != null ? target.Center : Projectile.Center + Vector2.UnitY * 200f;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 120;
            Projectile.height = 70;
        }

        protected override void DepthAI() {
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                Projectile.timeLeft = LaunchAge + VDDirector.FleetDashFrames + VDDirector.FleetEndFade;
                VDVfx.HoloBurst(Projectile.Center, VDVfx.VoidPurple);
            }
            Projectile.localAI[1]++;
            int age = Age;

            if (age < LaunchAge) {
                Projectile.velocity = Vector2.Zero;
            }
            if (age == LaunchAge) {
                //齐冲:各端按同一交汇点定速(服务端的速度随包覆盖)
                Vector2 cross = CrossPoint();
                Projectile.velocity = (cross - Projectile.Center) / VDDirector.FleetCrossFrame;
                if (IsServer) {
                    Projectile.netUpdate = true;
                }
                if (!Main.dedServ) {
                    CEUtils.PlaySound("CruiserDash", 1.1f, Projectile.Center, 4, 0.8f);
                }
            }
            if (age >= LaunchAge + VDDirector.FleetDashFrames) {
                Projectile.velocity *= 0.8f;
            }

            Projectile.rotation = MathHelper.Lerp(Projectile.rotation, MathHelper.Clamp(Projectile.velocity.X * 0.012f, -0.25f, 0.25f), 0.2f);
            Lighting.AddLight(Projectile.Center, VDVfx.VoidPurple.ToVector3() * 0.7f);

            if (!Main.dedServ && Dashing && Main.rand.NextBool(2)) {
                Vector2 v = -Projectile.velocity * 0.1f + CEUtils.randomPointInCircle(2f);
                VDVfx.SparkBurst(Projectile.Center + CEUtils.randomPointInCircle(40f), VDVfx.VoidPurple, 1, v.Length(), v.Length(), 18, 0.5f, 1f);
            }
        }

        protected override bool? CollidingOnPlane(Rectangle projHitbox, Rectangle targetHitbox) {
            if (!Dashing) {
                return false;
            }
            return projHitbox.Intersects(targetHitbox);
        }

        public override void OnKill(int timeLeft) {
            VDVfx.HoloBurst(Projectile.Center, VDVfx.VoidPurple);
            VDVfx.HoloBurst(Projectile.Center, Color.White);
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            VoidDestroyerNPC boss = Owner;
            Texture2D tex = boss != null && boss.Phase >= 2 && p2Tex != null ? p2Tex.Value : TextureAssets.Projectile[Type].Value;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            float fadeIn = MathHelper.Clamp(Age / 8f, 0f, 1f);
            float fadeOut = MathHelper.Clamp(Projectile.timeLeft / (float)VDDirector.FleetEndFade, 0f, 1f);
            float opacity = 0.85f * Math.Min(fadeIn, fadeOut);
            //瞄准期越接近出手越亮(全息闪烁加剧 = 预告),同时画出自己的航道
            if (Age < LaunchAge) {
                float p = Age / (float)LaunchAge;
                opacity *= 0.6f + 0.4f * p;
                Vector2 toCross = CrossPoint() - Projectile.Center;
                VDVfx.DrawLane(Projectile.Center, toCross, toCross.Length() + 500f, VDDirector.FleetLaneWidth, VDVfx.VoidPurple, 0.3f + 0.7f * p);
            }
            if (opacity <= 0.01f) {
                return;
            }

            //全息体半透明垫不了实心描边,冲刺时染向警示暖白
            Color holo = Dashing ? Color.Lerp(VDVfx.VoidPurple, VDVfx.HazardRim, 0.45f) : VDVfx.VoidPurple;
            VDHologramDraw.Begin();
            if (Dashing) {
                for (int i = 1; i < trailCount && i <= 4; i++) {
                    VDHologramDraw.DrawPart(tex, trailPos[i] - Main.screenPosition, null, VDVfx.VoidPurple, opacity * (0.3f - i * 0.06f), Projectile.rotation, tex.Size() / 2f, Projectile.scale, SpriteEffects.None);
                }
            }
            VDHologramDraw.DrawPart(tex, pos, null, holo, opacity, Projectile.rotation, tex.Size() / 2f, Projectile.scale, SpriteEffects.None);
            VDHologramDraw.End();
        }
    }
}
