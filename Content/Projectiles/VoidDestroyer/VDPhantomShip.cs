using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.NPCs.VoidDestroyer.States;
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
    /// 幻影舰队的幻影:门口待机 ai[1] 帧,航道跟着玩家预测点瞄,本体锁交汇点(RolledPoints[4])之后航道定死;
    /// 与真身同帧出手,第 FleetCrossFrame 帧穿过交汇点。ai[0] 本体,ai[2] 目标玩家;伤害是真身接触的 60%
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

        /// <summary>交汇点:本体锁定后读 RolledPoints[4],之前跟着玩家预测点</summary>
        private Vector2 CrossPoint(out bool locked) {
            VoidDestroyerNPC boss = Owner;
            locked = VDPhantomFleetState.CrossLocked(Age - 1);
            if (boss != null && locked) {
                return boss.Context.RolledPoints[4];
            }
            Player target = TargetPlayer(2);
            return target != null ? target.Center + target.velocity * VDDirector.FleetCrossLead : Projectile.Center + Vector2.UnitY * 200f;
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
                Vector2 cross = CrossPoint(out _);
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

            VoidDestroyerNPC boss = Owner;
            if (Dashing && boss != null && boss.Phase >= 3 && IsServer && age % VDDirector.FleetTrailInterval == 3) {
                int dmg = boss.ProjDamage(VDDirector.DmgVoidBolt);
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, Projectile.velocity.SafeNormalize(Vector2.UnitY) * VDDirector.PhantomTrailSpeed, ModContent.ProjectileType<VDVoidBolt>(), dmg, 0f, Main.myPlayer, VDVoidBolt.ModeAccelerate);
            }
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
                Vector2 cross = CrossPoint(out bool locked);
                Vector2 toCross = cross - Projectile.Center;
                VDVfx.DrawLane(Projectile.Center, toCross, toCross.Length() + 500f, VDDirector.FleetLaneWidth, VDVfx.VoidPurple, locked ? 1f : 0.2f + 0.5f * p);
            }
            if (opacity <= 0.01f) {
                return;
            }

            VDHologramDraw.Begin();
            if (Dashing) {
                for (int i = 1; i < trailCount && i <= 4; i++) {
                    VDHologramDraw.DrawPart(tex, trailPos[i] - Main.screenPosition, null, VDVfx.VoidPurple, opacity * (0.3f - i * 0.06f), Projectile.rotation, tex.Size() / 2f, Projectile.scale, SpriteEffects.None);
                }
            }
            VDHologramDraw.DrawPart(tex, pos, null, VDVfx.VoidPurple, opacity, Projectile.rotation, tex.Size() / 2f, Projectile.scale, SpriteEffects.None);
            VDHologramDraw.End();
        }
    }
}
