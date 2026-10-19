using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    /// <summary>
    /// 金乌图腾,烈阳召唤套激活时由本机玩家生成,挂在身后,不造成伤害
    /// ai[0] 召唤计时,只有拥有者生成太阳神鸟
    /// </summary>
    public class SunbirdTotem : ModProjectile
    {
        public const float TargetRange = 100 * 16;
        public const int SummonInterval = 30;
        public const int SunbirdBaseDamage = 250;

        private float glow;
        private float flash;

        public override string Texture => CEUtils.WhiteTexPath;

        public override void SetDefaults() {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 2;
            Projectile.netImportant = true;
            Projectile.DamageType = DamageClass.Summon;
        }

        public override bool? CanDamage() => false;

        public override void AI() {
            Player owner = Main.player[Projectile.owner];
            //只由拥有者收回,击杀包同步到其他端
            bool keep = owner.active && !owner.dead && owner.GetModPlayer<SunblazeArmorPlayer>().TotemActive;
            if (!owner.active || (!keep && Projectile.owner == Main.myPlayer)) {
                Projectile.Kill();
                return;
            }
            Projectile.timeLeft = 2;
            Projectile.Center = owner.MountedCenter + new Vector2(-owner.direction * 4f, -10f + owner.gfxOffY);
            Projectile.rotation += 0.012f;
            flash *= 0.88f;

            NPC target = FindTarget(owner);
            glow = MathHelper.Lerp(glow, target != null ? 1f : 0f, 0.08f);
            Lighting.AddLight(Projectile.Center, new Vector3(1f, 0.75f, 0.3f) * (0.3f + 0.5f * glow));
            if (target == null) {
                //重新锁到目标时 10 帧内出第一只
                Projectile.ai[0] = SummonInterval - 10;
                return;
            }
            if (++Projectile.ai[0] < SummonInterval) {
                return;
            }
            Projectile.ai[0] = 0;
            flash = 1f;
            if (Projectile.owner == Main.myPlayer) {
                SummonSunbird(owner, target);
            }
        }

        private NPC FindTarget(Player owner) {
            if (owner.HasMinionAttackTargetNPC) {
                NPC forced = Main.npc[owner.MinionAttackTargetNPC];
                if (forced.CanBeChasedBy(Projectile) && forced.DistanceSQ(owner.Center) <= TargetRange * TargetRange) {
                    return forced;
                }
            }
            NPC best = null;
            float bestDist = TargetRange * TargetRange;
            foreach (NPC npc in Main.ActiveNPCs) {
                if (!npc.CanBeChasedBy(Projectile)) {
                    continue;
                }
                float dist = npc.DistanceSQ(owner.Center);
                if (dist < bestDist) {
                    bestDist = dist;
                    best = npc;
                }
            }
            return best;
        }

        /// <summary>从拥有者屏幕上沿外生成,目标在屏幕上方时改从目标头顶 600 像素外生成</summary>
        private void SummonSunbird(Player owner, NPC target) {
            float top = Math.Min(Main.screenPosition.Y - 80f, target.Center.Y - 600f);
            Vector2 spawn = new(target.Center.X + Main.rand.NextFloat(-360f, 360f), top - Main.rand.NextFloat(0f, 80f));
            Vector2 velocity = (target.Center - spawn).SafeNormalize(Vector2.UnitY) * 14f;
            int damage = (int)owner.GetTotalDamage(DamageClass.Summon).ApplyTo(SunbirdBaseDamage);
            int index = Projectile.NewProjectile(Projectile.GetSource_FromThis(), spawn, velocity, ModContent.ProjectileType<Sunbird>(),
                damage, 3f, owner.whoAmI, target.whoAmI);
            if (index >= 0 && index < Main.maxProjectiles) {
                Main.projectile[index].originalDamage = SunbirdBaseDamage;
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            Vector2 center = Projectile.Center - Main.screenPosition;
            float pulse = 1f + 0.06f * MathF.Sin(Main.GlobalTimeWrappedHourly * 4f);
            Texture2D soft = CEUtils.getExtraTex("Glow");
            Texture2D ring = CEUtils.getExtraTex("BloomRing");
            Texture2D corona = CEUtils.getExtraTex("Corona");
            Color gold = new(255, 196, 90);
            Color ember = new(255, 120, 40);

            Main.EntitySpriteDraw(soft, center, null, (ember * (0.2f + 0.3f * glow)) with { A = 0 }, 0f, soft.Size() / 2f, 0.42f * pulse, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(corona, center, null, (gold * (0.25f + 0.45f * glow + 0.4f * flash)) with { A = 0 }, Projectile.rotation, corona.Size() / 2f, (0.13f + 0.03f * glow) * pulse, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(corona, center, null, (ember * (0.2f + 0.35f * glow)) with { A = 0 }, -Projectile.rotation * 1.6f, corona.Size() / 2f, (0.1f + 0.02f * glow) * pulse, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(ring, center, null, (gold * (0.5f + 0.4f * glow + 0.5f * flash)) with { A = 0 }, 0f, ring.Size() / 2f, 0.3f * pulse, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(ring, center, null, (Color.White * (0.15f + 0.3f * glow)) with { A = 0 }, 0f, ring.Size() / 2f, 0.27f * pulse, SpriteEffects.None, 0);
            return false;
        }
    }

    /// <summary>
    /// 太阳神鸟,前 FallFrames 帧只俯冲下落,之后追踪,命中一次后炸开
    /// ai[0] 目标 NPC 下标,localAI[0] 存活帧
    /// </summary>
    public class Sunbird : ModProjectile
    {
        public const int FallFrames = 12;

        public override void SetStaticDefaults() {
            Main.projFrames[Type] = 8;
            ProjectileID.Sets.MinionShot[Type] = true;
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override void SetDefaults() {
            Projectile.width = 36;
            Projectile.height = 36;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Summon;
            Projectile.penetrate = 1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 300;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI() {
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] == 1f && !Main.dedServ) {
                SoundEngine.PlaySound(SoundID.DD2_PhantomPhoenixShot with { Volume = 0.35f, PitchVariance = 0.2f, MaxInstances = 3 }, Main.player[Projectile.owner].Center);
            }
            Projectile.Opacity = MathHelper.Clamp(Projectile.localAI[0] / 10f, 0f, 1f);
            if (++Projectile.frameCounter >= 4) {
                Projectile.frameCounter = 0;
                Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];
            }

            if (Projectile.localAI[0] <= FallFrames) {
                Projectile.velocity.Y += 0.35f;
            }
            else if (GetTarget() is NPC target) {
                float speed = MathHelper.Lerp(18f, 26f, Utils.GetLerpValue(FallFrames, 60f, Projectile.localAI[0], true));
                Vector2 desired = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * speed;
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.1f);
            }
            Projectile.rotation = Projectile.velocity.ToRotation();

            Lighting.AddLight(Projectile.Center, 1f, 0.6f, 0.2f);
            if (!Main.dedServ && Main.rand.NextBool(2)) {
                Vector2 tail = Projectile.Center - Projectile.velocity.SafeNormalize(Vector2.Zero) * 14f + Main.rand.NextVector2Circular(10f, 10f);
                Dust d = Dust.NewDustPerfect(tail, DustID.SolarFlare, -Projectile.velocity * 0.15f, 100, default, Main.rand.NextFloat(0.9f, 1.3f));
                d.noGravity = true;
            }
        }

        /// <summary>原目标失效时只由拥有者重选,改了才同步</summary>
        private NPC GetTarget() {
            int index = (int)Projectile.ai[0];
            if (index >= 0 && index < Main.maxNPCs && Main.npc[index].CanBeChasedBy(Projectile)) {
                return Main.npc[index];
            }
            if (Projectile.owner != Main.myPlayer) {
                return null;
            }
            NPC next = Projectile.FindTargetWithinRange(SunbirdTotem.TargetRange);
            int nextIndex = next?.whoAmI ?? -1;
            if (nextIndex != index) {
                Projectile.ai[0] = nextIndex;
                Projectile.netUpdate = true;
            }
            return next;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            target.AddBuff(ModContent.BuffType<SunblazeBurn>(), 180);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info) {
            target.AddBuff(ModContent.BuffType<SunblazeBurn>(), 180);
        }

        public override void OnKill(int timeLeft) {
            if (Main.dedServ) {
                return;
            }
            for (int i = 0; i < 14; i++) {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.SolarFlare, Main.rand.NextVector2Circular(6f, 6f), 100, default, Main.rand.NextFloat(1f, 1.6f));
                d.noGravity = true;
            }
            //寿命耗尽时只散火星,命中才炸开
            if (timeLeft <= 0) {
                return;
            }
            SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Volume = 0.5f, PitchVariance = 0.2f, MaxInstances = 4 }, Projectile.Center);
            PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(255, 150, 60), 0.1f).Configure(1.6f, 16);
            PRTLoader.NewParticle<PRT_SparkleCal>(Projectile.Center, Vector2.Zero, new Color(255, 230, 160), 1.8f).Configure(new Color(255, 120, 40), 14, 0f, 2.5f);
            for (int i = 0; i < 10; i++) {
                Color color = Color.Lerp(Color.Orange, Color.Gold, Main.rand.NextFloat());
                PRTLoader.NewParticle<PRT_SparkCal>(Projectile.Center, Main.rand.NextVector2Circular(9f, 9f), color, Main.rand.NextFloat(0.6f, 1.1f)).Configure(true, 30);
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            int frameHeight = tex.Height / Main.projFrames[Type];
            Rectangle frame = new(0, frameHeight * Projectile.frame, tex.Width, frameHeight - 2);
            Vector2 origin = frame.Size() / 2f;
            //贴图鸟头朝右,往左飞时竖直翻转保持背朝上
            SpriteEffects effects = Projectile.velocity.X < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;

            for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
                if (Projectile.oldPos[i] == Vector2.Zero) {
                    continue;
                }
                float t = 1f - i / (float)Projectile.oldPos.Length;
                Color trail = (new Color(255, 140, 50) * (0.45f * t * Projectile.Opacity)) with { A = 0 };
                Main.EntitySpriteDraw(tex, Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition, frame, trail,
                    Projectile.oldRot[i], origin, Projectile.scale * (0.8f + 0.2f * t), effects, 0);
            }
            Texture2D soft = CEUtils.getExtraTex("Glow");
            Main.EntitySpriteDraw(soft, Projectile.Center - Main.screenPosition, null, (new Color(255, 150, 60) * (0.5f * Projectile.Opacity)) with { A = 0 },
                0f, soft.Size() / 2f, 0.4f, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, frame, Color.White * Projectile.Opacity,
                Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }
}
