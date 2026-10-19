using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using CalamityEntropy.Content.Rarities;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidDestroyer
{
    /// <summary>蓄力不足 MinReleaseCharge 松手只熄灭;掷出爆炸带电,再甩连锁电弧</summary>
    public class VoidElectricField : ModItem
    {
        public override void SetDefaults() {
            Item.width = 56;
            Item.height = 56;
            Item.damage = 280;
            Item.DamageType = DamageClass.Magic;
            Item.mana = 10;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.channel = true;
            Item.knockBack = 5f;
            Item.UseSound = null;
            Item.autoReuse = false;
            Item.value = Item.buyPrice(platinum: 2, gold: 50);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.shoot = ModContent.ProjectileType<VoidElectricFieldHoldout>();
            Item.shootSpeed = 1f;
        }

        public override bool MagicPrefix() => true;

        public override bool CanUseItem(Player player) => player.ownedProjectileCounts[Item.shoot] == 0;
    }

    /// <summary>
    /// ai[0] 蓄力帧,拥有者每 20 帧同步;ai[1] 为 1 表示已松手
    /// 方向由相位、序号、蓄力确定,余弦当纵深;松手后回弹 ReleaseFrames 再收
    /// </summary>
    public class VoidElectricFieldHoldout : ModProjectile
    {
        public const int MaxCharge = 180;
        public const int MinReleaseCharge = 15;
        public const int LaserCount = 5;
        public const float LaserRange = 1400f;
        public const int LaserHitCooldown = 5;
        public const int ManaInterval = 12;
        public const int ReleaseFrames = 14;
        public const float BallMinMult = 1.5f;
        public const float BallMaxMult = 10f;
        public const float SpreadOpen = 0.56f;
        public const float SpreadFocus = 0.24f;
        /// <summary>光束可见宽度(像素):基础 + 满蓄力增量,再乘出现包络</summary>
        public const float BeamWidthBase = 12f;
        public const float BeamWidthCharge = 10f;
        public const float HitWidthExtra = 14f;
        /// <summary>打空的束末端收窄到起点宽度的比例,和末端渐隐占全长的比例</summary>
        public const float OpenEndTaper = 0.3f;
        public const float OpenEndFade = 0.24f;
        /// <summary>暗鞘相对光束的宽度倍数与不透明度</summary>
        public const float UnderlayWidthMult = 1.9f;
        public const float UnderlayOpacity = 0.45f;
        public static readonly Color BeamColor = new Color(170, 80, 255);
        public static readonly Color CoreColor = new Color(235, 215, 255);
        public static readonly Color ArcColor = new Color(200, 160, 255);
        public static readonly Color UnderlayColor = new Color(18, 4, 36);
        /// <summary>电弧爆炸(放球闪光、闪电球爆炸)的热色与冷却色,主色用 BeamColor</summary>
        public static readonly Color ElectricHot = new Color(245, 235, 255);
        public static readonly Color ElectricCool = new Color(80, 50, 160);

        //贴图几何(110×110,杖头朝右上):柄端、握点、轴角
        public static readonly Vector2 Butt = new Vector2(0f, 97f);
        public const float AxisAngle = -0.6896f;
        public const float GripAlong = 28f;
        public static Vector2 Pivot => Butt + AxisAngle.ToRotationVector2() * GripAlong;

        public override string Texture => "CalamityEntropy/Content/Items/Weapons/VoidDestroyer/VoidElectricField";

        [VaultLoaden("CalamityEntropy/Content/Items/Weapons/VoidDestroyer/VoidElectricField_Glow")]
        internal static Asset<Texture2D> GlowMask;

        private bool initialized;
        private float aim;
        private float spin;
        private float kick;
        private float kickVel;
        private float climb;
        private float climbVel;
        private int releaseTimer = -1;
        private float releasedRatio;
        private bool releasedBall;
        private bool readyCue;
        private bool manaOut;
        private LoopSound humSound;
        private LoopSound beamSound;
        private PlasmaBall ball;
        private readonly Vector2[] beamStart = new Vector2[LaserCount];
        private readonly Vector2[] beamEnd = new Vector2[LaserCount];
        private readonly float[] beamDepth = new float[LaserCount];
        private readonly bool[] beamBlocked = new bool[LaserCount];

        public float Charge { get => Projectile.ai[0]; set => Projectile.ai[0] = value; }
        public float Ratio => MathHelper.Clamp(Charge / MaxCharge, 0f, 1f);
        public int Age => (int)Projectile.localAI[0];
        public bool Releasing => releaseTimer >= 0;
        /// <summary>激光出现的包络:第 3 帧起 9 帧展宽</summary>
        public float Envelope => MathHelper.Clamp((Age - 3) / 9f, 0f, 1f);

        public static float RadiusAt(float ratio) {
            float s = ratio * ratio * (3f - 2f * ratio);
            return MathHelper.Lerp(10f, 44f, s);
        }

        public float BallRadius => RadiusAt(Ratio);

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 1600;
        }

        public override void SetDefaults() {
            Projectile.HeldProjSetDefaults(DamageClass.Magic);
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 2;
            Projectile.aiStyle = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = LaserHitCooldown;
        }

        public override bool ShouldUpdatePosition() => false;
        public override bool? CanCutTiles() => false;

        private int Facing => MathF.Cos(aim) >= 0f ? 1 : -1;

        private Vector2 HandPos(Player owner) {
            float armRot = aim - MathHelper.PiOver2;
            return owner.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRot) + new Vector2(0f, owner.gfxOffY);
        }

        private HeldSprite Staff(Player owner) {
            float rot = aim - climb * Facing;
            Vector2 world = HandPos(owner) - rot.ToRotationVector2() * kick;
            return HeldSprite.Along(TextureAssets.Projectile[Type].Value, Pivot, AxisAngle, world, rot, Facing);
        }

        /// <summary>闪电球球心:沿杖轴,离柄端 120 + 0.75 × 半径(球越大越往外推,始终落在杖头之上)</summary>
        private Vector2 BallPos(Player owner) {
            HeldSprite staff = Staff(owner);
            float along = 120f + 0.75f * BallRadius - GripAlong;
            return staff.World + (aim - climb * Facing).ToRotationVector2() * along;
        }

        public override void AI() {
            Player owner = Projectile.GetOwner();
            if (!owner.active || owner.dead || owner.HeldItem.type != ModContent.ItemType<VoidElectricField>()) {
                Projectile.Kill();
                return;
            }
            Projectile.localAI[0]++;
            bool isOwner = Projectile.owner == Main.myPlayer;
            if (isOwner) {
                owner.Entropy().MouseWorldListener = true;
            }
            float want = (owner.Entropy().MouseWorld - owner.MountedCenter).ToRotation();
            if (!initialized) {
                initialized = true;
                aim = want;
            }
            if (!Releasing) {
                aim = CEUtils.RotateTowardsAngle(aim, want, 0.14f, false);
            }
            owner.ChangeDir(Facing);
            owner.heldProj = Projectile.whoAmI;
            owner.itemTime = owner.itemAnimation = 2;
            Projectile.timeLeft = 2;

            kick += kickVel;
            kickVel *= 0.62f;
            kick *= 0.8f;
            climb += climbVel;
            climbVel *= 0.6f;
            climb *= 0.82f;

            float armRot = aim - climb * Facing - MathHelper.PiOver2;
            owner.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
            owner.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Quarter, armRot + 0.35f * Facing);
            owner.itemRotation = MathHelper.WrapAngle(aim + (Facing < 0 ? MathHelper.Pi : 0f));
            Projectile.Center = HandPos(owner);

            if (Releasing) {
                releaseTimer++;
                if (releaseTimer >= ReleaseFrames) {
                    Projectile.Kill();
                }
                return;
            }

            if (isOwner && Age > 1 && Age % ManaInterval == 0) {
                int cost = Math.Max(1, (int)(owner.GetManaCost(owner.HeldItem) * 0.35f));
                if (!owner.CheckMana(cost, true)) {
                    manaOut = true;
                }
            }
            //松手只由拥有者判定(远端的 channel 可能滞后),远端跟随同步来的 ai[1]
            bool holding = owner.channel && !manaOut && !owner.CCed && !owner.noItems;
            if (isOwner && !holding) {
                Release(owner);
                return;
            }
            if (!isOwner && Projectile.ai[1] == 1f) {
                BeginRelease(Charge >= MinReleaseCharge);
                return;
            }

            Charge = Math.Min(Charge + 1f, MaxCharge);
            if (isOwner && Age % 20 == 0) {
                Projectile.netUpdate = true;
            }
            spin += MathHelper.Lerp(0.045f, 0.11f, Ratio);
            UpdateBeams(owner);
            Lighting.AddLight(BallPos(owner), BeamColor.ToVector3() * (0.6f + 0.8f * Ratio));
            if (!Main.dedServ) {
                ClientCharge(owner);
            }
        }

        /// <summary>五道激光:相位按旋转推进,扇面随蓄力收拢;逐道向前步进到第一块实心物块</summary>
        private void UpdateBeams(Player owner) {
            Vector2 center = BallPos(owner);
            float r = BallRadius;
            float spread = MathHelper.Lerp(SpreadOpen, SpreadFocus, Ratio);
            for (int i = 0; i < LaserCount; i++) {
                float phase = spin + i * MathHelper.TwoPi / LaserCount;
                Vector2 d = (aim + spread * MathF.Sin(phase)).ToRotationVector2();
                beamDepth[i] = MathF.Cos(phase);
                Vector2 start = center + d * r * 0.7f;
                float len = Raycast(start, d, LaserRange, out beamBlocked[i]);
                beamStart[i] = start;
                beamEnd[i] = start + d * len;
            }
        }

        private static float Raycast(Vector2 start, Vector2 dir, float max, out bool blocked) {
            blocked = false;
            float d = 0f;
            while (d < max) {
                float next = Math.Min(d + 12f, max);
                if (!CEUtils.isAir(start + dir * next)) {
                    while (d + 3f < next && CEUtils.isAir(start + dir * (d + 3f))) {
                        d += 3f;
                    }
                    blocked = true;
                    return d;
                }
                d = next;
            }
            return max;
        }

        private void ClientCharge(Player owner) {
            ball ??= new PlasmaBall();
            ball.Update(Ratio);
            Vector2 center = BallPos(owner);
            float r = BallRadius;
            float ratio = Ratio;

            if (Age == 2) {
                CEUtils.PlaySound("lasercharge", 1.15f, center, 3, 0.45f);
            }
            if (humSound == null && CalamityEntropy.ofCharge != null) {
                humSound = new LoopSound(CalamityEntropy.ofCharge);
                humSound.setVolume(0f);
                humSound.play();
            }
            if (beamSound == null && CalamityEntropy.ealaserSound != null) {
                beamSound = new LoopSound(CalamityEntropy.ealaserSound);
                beamSound.setVolume(0f);
                beamSound.play();
            }
            if (humSound != null) {
                humSound.timeleft = 3;
                humSound.setVolume_Dist(center, 200f, 1300f, 0.18f + 0.4f * ratio);
                if (humSound.instance != null) {
                    humSound.instance.Pitch = MathHelper.Clamp(-0.35f + 0.65f * ratio + (readyCue ? 0.1f : 0f), -1f, 1f);
                }
            }
            if (beamSound != null) {
                beamSound.timeleft = 3;
                beamSound.setVolume_Dist(center, 200f, 1300f, 0.22f * Envelope);
                if (beamSound.instance != null) {
                    beamSound.instance.Pitch = MathHelper.Clamp(-0.1f + 0.25f * ratio, -1f, 1f);
                }
            }

            if (Charge >= MaxCharge && !readyCue) {
                readyCue = true;
                CEUtils.PlaySound("spark", 1.35f, center, 3, 0.7f);
                PRTLoader.NewParticle<PRT_PulseRing>(center, Vector2.Zero, ArcColor, 0.15f).Configure(0.9f, 16);
                PRTLoader.NewParticle<PRT_ShineParticle>(center, Vector2.Zero, CoreColor, 0.6f).Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, 0f, 14);
                VDVfx.SparkBurst(center, CoreColor, 14, 3f, 8f, 20, 0.4f, 0.8f);
            }

            //向球汇聚的电点:蓄得越满越密
            if (Main.rand.NextFloat() < 0.3f + 0.5f * ratio) {
                Vector2 from = center + CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 140f);
                VDVfx.Spark(from, (center - from) / 10f, Color.Lerp(ArcColor, Color.White, Main.rand.NextFloat(0.5f)), Main.rand.NextFloat(0.3f, 0.5f), 0.9f, 10);
            }
            if (Main.rand.NextBool(3)) {
                Vector2 on = center + CEUtils.randomRot().ToRotationVector2() * r;
                VDVfx.Spark(on, (on - center).SafeNormalize(Vector2.Zero) * Main.rand.NextFloat(1f, 3f), ArcColor, Main.rand.NextFloat(0.3f, 0.5f), 0.9f, 10);
            }
            //激光打在墙上的端点溅火花
            for (int i = 0; i < LaserCount; i++) {
                if (beamBlocked[i] && Main.rand.NextBool(3) && Envelope > 0.5f) {
                    Vector2 d = (beamEnd[i] - beamStart[i]).SafeNormalize(Vector2.UnitX);
                    VDVfx.Spark(beamEnd[i], (-d).RotatedByRandom(1.2f) * Main.rand.NextFloat(2f, 6f), BeamColor, Main.rand.NextFloat(0.35f, 0.55f), 1f, 10, gravity: true);
                }
            }
        }

        /// <summary>拥有者松手:蓄力够就按蓄力比放球、人往后坐,不够只熄灭;置 ai[1] 同步给各端</summary>
        private void Release(Player owner) {
            bool fire = Charge >= MinReleaseCharge;
            if (fire) {
                float ratio = Ratio;
                Vector2 dir = aim.ToRotationVector2();
                int damage = (int)(Projectile.damage * MathHelper.Lerp(BallMinMult, BallMaxMult, ratio));
                float speed = MathHelper.Lerp(8.5f, 5.5f, ratio);
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), BallPos(owner), dir * speed, ModContent.ProjectileType<VoidElectricBall>(), damage, Projectile.knockBack * (1f + ratio), Projectile.owner, ratio);
                owner.velocity -= dir * (1f + 4f * ratio);
            }
            Projectile.ai[1] = 1f;
            Projectile.netUpdate = true;
            BeginRelease(fire);
        }

        /// <summary>进入收杖:各端各放一次放球(或熄灭)演出,杖身后坐、枪口上扬</summary>
        private void BeginRelease(bool fired) {
            if (Releasing) {
                return;
            }
            Player owner = Projectile.GetOwner();
            releaseTimer = 0;
            releasedRatio = Ratio;
            releasedBall = fired;
            humSound?.stop();
            beamSound?.stop();
            kickVel += fired ? 6f + 10f * releasedRatio : 2f;
            climbVel += fired ? 0.05f + 0.12f * releasedRatio : 0.02f;
            if (Main.dedServ) {
                return;
            }
            Vector2 center = BallPos(owner);
            if (!fired) {
                CEUtils.PlaySound("spark", 0.6f, center, 3, 0.4f);
                VDVfx.SparkBurst(center, ArcColor, 6, 1.5f, 4f, 14, 0.3f, 0.5f);
                return;
            }
            Vector2 dir = aim.ToRotationVector2();
            CEUtils.PlaySound("ThunderStrike", 1.25f - 0.35f * releasedRatio, center, 4, 0.45f + 0.45f * releasedRatio);
            CEUtils.SetShake(center - dir * 40f, 2f + 6f * releasedRatio, 1600f);
            for (int i = 0; i < 10 + (int)(14 * releasedRatio); i++) {
                VDVfx.Spark(center, dir.RotatedByRandom(0.5f) * Main.rand.NextFloat(4f, 12f), i % 3 == 0 ? Color.White : CoreColor, Main.rand.NextFloat(0.4f, 0.8f), 1f, Main.rand.Next(10, 18));
            }
            PRTLoader.NewParticle<PRT_ShineParticle>(center, Vector2.Zero, BeamColor, 0.5f + 0.6f * releasedRatio).Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, 0f, 12);
            PRTLoader.NewParticle<PRT_ShineParticle>(center, Vector2.Zero, Color.White, 0.3f + 0.4f * releasedRatio).Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, 0f, 10);
            //放球闪光:不带暗核的电浆团
            PRTLoader.NewParticle<PRT_VDVoidBlast>(center, Vector2.Zero, Color.White, BallRadius * 1.5f)
                .Configure(12 + (int)(6 * releasedRatio), 0f, ElectricHot, BeamColor, ElectricCool);
        }

        public override void OnKill(int timeLeft) {
            humSound?.stop();
            beamSound?.stop();
        }

        public override bool? CanDamage() => !Releasing && Envelope > 0.3f ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            float w = (BeamWidthBase + BeamWidthCharge * Ratio) * Envelope + HitWidthExtra;
            for (int i = 0; i < LaserCount; i++) {
                if (CEUtils.LineThroughRect(beamStart[i], beamEnd[i], targetHitbox, (int)w)) {
                    return true;
                }
            }
            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            Vector2 dir = aim.ToRotationVector2();
            for (int i = 0; i < 3; i++) {
                VDVfx.Spark(target.Center + CEUtils.randomPointInCircle(target.width * 0.3f), dir.RotatedByRandom(0.8f) * Main.rand.NextFloat(3f, 8f), BeamColor, Main.rand.NextFloat(0.4f, 0.65f), 1f, 12, gravity: true);
            }
            CEUtils.PlaySound("nvspark", Main.rand.NextFloat(1.6f, 2f), target.Center, 3, 0.2f);
        }

        public override bool PreDraw(ref Color lightColor) {
            Player owner = Projectile.GetOwner();
            if (!initialized) {
                return false;
            }
            HeldSprite staff = Staff(owner);
            Texture2D glow = GlowMask.Value;
            staff.Draw(lightColor);
            staff.Draw(glow, Color.White);

            if (Releasing) {
                //放球那一下杖头还亮着,随后淡掉
                float f = 1f - releaseTimer / (float)ReleaseFrames;
                if (releasedBall && f > 0f) {
                    VDWeaponFx.BeginAdditive();
                    staff.Draw(glow, BeamColor * (0.8f * f));
                    VDWeaponFx.Glow(BallPos(owner), BeamColor * (0.6f * f * f), 30f + 40f * releasedRatio);
                    VDWeaponFx.End();
                }
                return false;
            }

            float env = Envelope;
            float ratio = Ratio;
            Vector2 center = BallPos(owner);
            float r = BallRadius;
            DrawBeams(BeamLayer.Underlay, env, ratio, center, r);
            DrawBeams(BeamLayer.Back, env, ratio, center, r);
            ball?.Draw(center, r, ratio, MathHelper.Clamp(Age / 6f, 0f, 1f), BeamColor, CoreColor);
            DrawBeams(BeamLayer.Front, env, ratio, center, r);
            DrawBeamFlares(env, ratio, center, r);
            return false;
        }

        private enum BeamLayer
        {
            Underlay,
            Back,
            Front
        }

        /// <summary>背面束从球心出发(球体盖住起点),正面束从球壳出发;打空收窄并渐隐,打墙平收在墙面</summary>
        private void BeamShape(int i, Vector2 center, float r, float baseWidth, out Vector2 start, out Vector2 dir, out float wStart, out float wEnd, out float capStart, out float capEnd) {
            dir = (beamEnd[i] - beamStart[i]).SafeNormalize(aim.ToRotationVector2());
            bool back = beamDepth[i] < 0f;
            start = back ? center : center + dir * r * 0.92f;
            wStart = baseWidth * (0.75f + 0.25f * beamDepth[i]);
            wEnd = beamBlocked[i] ? wStart * 0.85f : wStart * OpenEndTaper;
            capStart = back ? 0f : 0.02f;
            capEnd = beamBlocked[i] ? 0.015f : OpenEndFade;
        }

        /// <summary>暗鞘层是 AlphaBlend 深紫宽束,亮背景上先压暗底,加法束才不被冲成白线;着色器缺失时不画</summary>
        private void DrawBeams(BeamLayer layer, float env, float ratio, Vector2 center, float r) {
            if (env <= 0.01f) {
                return;
            }
            float baseWidth = (BeamWidthBase + BeamWidthCharge * ratio) * env;
            bool under = layer == BeamLayer.Underlay;
            Color main = under ? UnderlayColor : BeamColor;
            Color core = under ? UnderlayColor : CoreColor;
            float opacity = under ? UnderlayOpacity : layer == BeamLayer.Back ? 0.55f : 1f;
            float seed = Projectile.whoAmI * 0.37f;
            bool batched = VDBeamDraw.BeginTapered(main, core, opacity, seed, under ? BlendState.AlphaBlend : null);
            if (!batched && under) {
                return;
            }
            float widthMult = under ? UnderlayWidthMult : 1f;
            for (int i = 0; i < LaserCount; i++) {
                bool isBack = beamDepth[i] < 0f;
                if ((layer == BeamLayer.Back && !isBack) || (layer == BeamLayer.Front && isBack)) {
                    continue;
                }
                BeamShape(i, center, r, baseWidth, out Vector2 start, out _, out float wS, out float wE, out float capS, out float capE);
                if (batched) {
                    VDBeamDraw.TaperedQuad(start, beamEnd[i], wS * widthMult, wE * widthMult, 1f, capStart: capS, capEnd: capE);
                }
                else {
                    VDBeamDraw.DrawTapered(start, beamEnd[i], wS, wE, BeamColor, CoreColor, 1f, opacity, seed + i, endGlow: false, capStart: capS, capEnd: capE);
                }
            }
            if (batched) {
                VDBeamDraw.EndTapered();
            }
        }

        /// <summary>正面束的出射光与打墙束的撞击光</summary>
        private void DrawBeamFlares(float env, float ratio, Vector2 center, float r) {
            if (env <= 0.05f) {
                return;
            }
            float baseWidth = (BeamWidthBase + BeamWidthCharge * ratio) * env;
            float flick = 0.85f + 0.15f * MathF.Sin(Age * 1.7f);
            VDWeaponFx.BeginAdditive();
            for (int i = 0; i < LaserCount; i++) {
                BeamShape(i, center, r, baseWidth, out Vector2 start, out Vector2 dir, out float w, out _, out _, out _);
                float depth = (beamDepth[i] + 1f) * 0.5f;
                float a = (0.35f + 0.65f * depth) * env;
                float rot = dir.ToRotation();
                if (beamDepth[i] >= 0f) {
                    VDWeaponFx.GlowStretched(start + dir * w * 0.6f, CoreColor * (0.8f * a), rot, w * 4f, w * 1.7f);
                    VDWeaponFx.Glow(start, Color.White * (0.5f * a), w * 0.9f);
                }
                if (beamBlocked[i]) {
                    Vector2 end = beamEnd[i];
                    VDWeaponFx.GlowStretched(end, BeamColor * (0.85f * a * flick), rot + MathHelper.PiOver2, w * 5.5f, w * 1.6f);
                    VDWeaponFx.Glow(end, CoreColor * (0.7f * a), w * 1.6f);
                    VDWeaponFx.Glow(end, Color.White * (0.6f * a * flick), w * 0.7f);
                }
            }
            VDWeaponFx.End();
        }
    }

    /// <summary>
    /// 闪电球:ai[0] 为蓄力比 0..1,半径与判定框按它定;两次更新直飞(大球更慢),贴到敌怪、撞墙或超时就爆炸(<see cref="VoidElectricBurst"/>)
    /// 球体自身不判伤,伤害全由爆炸结算。外观与杖头同一颗等离子球,多一道余晖拖尾
    /// </summary>
    public class VoidElectricBall : ModProjectile
    {
        public const int ElectrifyTime = 240;
        public const int TrailLength = 10;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";

        private readonly List<Vector2> trail = new List<Vector2>();
        private PlasmaBall ball;

        public float Ratio => MathHelper.Clamp(Projectile.ai[0], 0f, 1f);
        public float Radius => VoidElectricFieldHoldout.RadiusAt(Ratio);

        public override void SetDefaults() {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 360;
            Projectile.extraUpdates = 1;
            Projectile.aiStyle = -1;
        }

        public override bool? CanDamage() => false;

        public override void AI() {
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] == 1f) {
                int size = (int)(Radius * 1.4f);
                Projectile.Resize(size, size);
            }
            if (Projectile.owner == Main.myPlayer) {
                Rectangle box = Projectile.Hitbox;
                foreach (NPC n in Main.ActiveNPCs) {
                    if (n.CanBeChasedBy(Projectile) && box.Intersects(n.Hitbox)) {
                        Projectile.Kill();
                        return;
                    }
                }
            }
            Lighting.AddLight(Projectile.Center, VoidElectricFieldHoldout.BeamColor.ToVector3() * (0.6f + 0.8f * Ratio));
            if (Projectile.localAI[0] % 2 != 0 || Main.dedServ) {
                return;
            }
            trail.Insert(0, Projectile.Center);
            if (trail.Count > TrailLength) {
                trail.RemoveAt(trail.Count - 1);
            }
            ball ??= new PlasmaBall();
            ball.Update(Ratio);
            if (Main.rand.NextBool(2)) {
                Vector2 on = Projectile.Center + CEUtils.randomRot().ToRotationVector2() * Radius;
                VDVfx.Spark(on, (on - Projectile.Center).SafeNormalize(Vector2.Zero) * Main.rand.NextFloat(1f, 3f) - Projectile.velocity * 0.2f, VoidElectricFieldHoldout.ArcColor, Main.rand.NextFloat(0.3f, 0.5f), 0.9f, 10);
            }
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => true;

        public override void OnKill(int timeLeft) {
            if (Projectile.owner == Main.myPlayer) {
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero, ModContent.ProjectileType<VoidElectricBurst>(), Projectile.damage, Projectile.knockBack, Projectile.owner, Ratio);
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            float r = Radius;
            if (trail.Count > 2) {
                VDWeaponFx.BeginAdditive();
                VDWeaponFx.Ribbon(trail, t => r * 1.7f * (1f - t), t => VoidElectricFieldHoldout.BeamColor * ((1f - t) * 0.45f));
                VDWeaponFx.Ribbon(trail, t => r * 0.6f * (1f - t), t => VoidElectricFieldHoldout.CoreColor * ((1f - t) * 0.4f));
                VDWeaponFx.End();
            }
            ball?.Draw(Projectile.Center, r, Ratio, 1f, VoidElectricFieldHoldout.BeamColor, VoidElectricFieldHoldout.CoreColor);
            return false;
        }
    }

    /// <summary>
    /// 闪电球爆炸:半径随蓄力比 120 → 210 的圆形判定,前 <see cref="DamageFrames"/> 帧有效,命中带电。
    /// 拥有者在首帧向爆炸圈边缘及以外的敌人(最多 3 + 5 × 蓄力比 个)甩出连锁电弧 <see cref="VoidElectricArc"/>;演出是电浆配色的 PRT_VDVoidBlast(暗核压低)与十余道向外爬的电弧
    /// </summary>
    public class VoidElectricBurst : ModProjectile
    {
        public const int Lifetime = 20;
        public const int DamageFrames = 4;
        public const float ChainRange = 640f;
        public const float ChainMult = 0.4f;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";

        private readonly List<List<Vector2>> arcs = new List<List<Vector2>>();
        private readonly List<float> arcAngles = new List<float>();

        public float Ratio => MathHelper.Clamp(Projectile.ai[0], 0f, 1f);
        public float Radius => MathHelper.Lerp(120f, 210f, Ratio);
        public int Age => (int)Projectile.localAI[0];

        public override void SetDefaults() {
            Projectile.width = 200;
            Projectile.height = 200;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.aiStyle = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool? CanDamage() => Age <= DamageFrames ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            Vector2 nearest = Vector2.Clamp(Projectile.Center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            return Vector2.DistanceSquared(nearest, Projectile.Center) <= Radius * Radius;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            target.AddBuff(BuffID.Electrified, VoidElectricBall.ElectrifyTime);
        }

        public override void AI() {
            Projectile.localAI[0]++;
            if (Age == 1) {
                int size = (int)(Radius * 2f);
                Projectile.Resize(size, size);
                if (Projectile.owner == Main.myPlayer) {
                    SpawnChains();
                }
                if (!Main.dedServ) {
                    float ratio = Ratio;
                    CEUtils.PlaySound("shockBlast", 1.1f - 0.25f * ratio, Projectile.Center, 4, 0.6f + 0.4f * ratio);
                    CEUtils.PlaySound("light_bolt", Main.rand.NextFloat(0.95f, 1.1f), Projectile.Center, 3, 0.35f);
                    CEUtils.SetShake(Projectile.Center, 3f + 6f * ratio, 1800f);
                    PRTLoader.NewParticle<PRT_VDVoidBlast>(Projectile.Center, Vector2.Zero, Color.White, Radius * 0.9f)
                        .Configure(22 + (int)(10 * ratio), 0.3f, VoidElectricFieldHoldout.ElectricHot, VoidElectricFieldHoldout.BeamColor, VoidElectricFieldHoldout.ElectricCool);
                    VDVfx.SparkBurst(Projectile.Center, VoidElectricFieldHoldout.CoreColor, 14 + (int)(18 * ratio), 4f, 14f, 24, 0.5f, 1f);
                    VDVfx.SparkBurst(Projectile.Center, VoidElectricFieldHoldout.BeamColor, 10 + (int)(12 * ratio), 2f, 8f, 22, 0.45f, 0.9f);
                    for (int i = 0; i < 8; i++) {
                        Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 6f);
                        VDVfx.VoidPuff(Projectile.Center + v * 3f, v, 1.2f, 0.6f);
                    }
                    int count = 10 + (int)(6 * ratio);
                    for (int i = 0; i < count; i++) {
                        arcs.Add(new List<Vector2>());
                        arcAngles.Add(i * MathHelper.TwoPi / count + Main.rand.NextFloat(-0.2f, 0.2f));
                    }
                }
            }
            Lighting.AddLight(Projectile.Center, VoidElectricFieldHoldout.BeamColor.ToVector3() * (1.4f * (1f - Age / (float)Lifetime)));
            if (!Main.dedServ && Age % 2 == 1) {
                float reach = Radius * MathHelper.Lerp(0.55f, 1.05f, VDVfx.EaseOut(Age / (float)Lifetime));
                for (int i = 0; i < arcs.Count; i++) {
                    Vector2 end = Projectile.Center + arcAngles[i].ToRotationVector2() * reach * Main.rand.NextFloat(0.75f, 1f);
                    VDWeaponFx.BuildArc(arcs[i], Projectile.Center + arcAngles[i].ToRotationVector2() * 12f, end, 6, 16f, Main.rand);
                }
            }
        }

        /// <summary>连锁电弧的目标:爆炸圈 0.6 倍半径以外、<see cref="ChainRange"/> 以内的敌人,由近到远</summary>
        private void SpawnChains() {
            int max = 3 + (int)(5 * Ratio);
            List<NPC> picks = new List<NPC>();
            foreach (NPC n in Main.ActiveNPCs) {
                if (!n.CanBeChasedBy(Projectile)) {
                    continue;
                }
                float d = Vector2.Distance(n.Center, Projectile.Center);
                if (d > Radius * 0.6f && d < ChainRange) {
                    picks.Add(n);
                }
            }
            picks.Sort((a, b) => Vector2.DistanceSquared(a.Center, Projectile.Center).CompareTo(Vector2.DistanceSquared(b.Center, Projectile.Center)));
            int damage = Math.Max(1, (int)(Projectile.damage * ChainMult));
            for (int i = 0; i < picks.Count && i < max; i++) {
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero, ModContent.ProjectileType<VoidElectricArc>(), damage, 2f, Projectile.owner, picks[i].whoAmI, i);
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            float life = Age / (float)Lifetime;
            VDWeaponFx.BeginAdditive();
            float arcOpacity = 1f - life;
            foreach (var arc in arcs) {
                if (arc.Count > 1) {
                    VDWeaponFx.DrawArc(arc, VoidElectricFieldHoldout.BeamColor, VoidElectricFieldHoldout.CoreColor, 2.2f * (1f - life * 0.5f), arcOpacity);
                }
            }
            VDWeaponFx.End();
            return false;
        }
    }

    /// <summary>
    /// 连锁电弧:从爆炸点甩到 ai[0] 号敌人,只打它一次(第 1~2 帧判定),命中带电;电弧每 2 帧重抖并追着目标当前位置。ai[1] 为序号(前两道出声)
    /// </summary>
    public class VoidElectricArc : ModProjectile
    {
        public const int Lifetime = 14;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";

        private readonly List<Vector2> points = new List<Vector2>();
        private Vector2 end;

        public int Age => (int)Projectile.localAI[0];

        private NPC Target {
            get {
                int i = (int)Projectile.ai[0];
                if (i < 0 || i >= Main.maxNPCs) {
                    return null;
                }
                NPC n = Main.npc[i];
                return n.active && n.life > 0 ? n : null;
            }
        }

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 600;
        }

        public override void SetDefaults() {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.aiStyle = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[0] ? null : false;

        public override bool? CanDamage() => Age <= 2 ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) => true;

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            target.AddBuff(BuffID.Electrified, VoidElectricBall.ElectrifyTime);
        }

        public override void AI() {
            Projectile.localAI[0]++;
            NPC target = Target;
            if (target != null) {
                end = target.Center;
            }
            else if (Age == 1) {
                Projectile.Kill();
                return;
            }
            if (Main.dedServ) {
                return;
            }
            if (Age == 1) {
                if (Projectile.ai[1] < 2f) {
                    CEUtils.PlaySound("spark", Main.rand.NextFloat(1.3f, 1.6f), end, 4, 0.35f);
                }
                VDVfx.SparkBurst(end, VoidElectricFieldHoldout.CoreColor, 8, 2f, 7f, 16, 0.35f, 0.65f);
            }
            if (Age % 2 == 1) {
                float len = Vector2.Distance(Projectile.Center, end);
                VDWeaponFx.BuildArc(points, Projectile.Center, end, Math.Max(6, (int)(len / 26f)), 20f, Main.rand);
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            if (points.Count < 2) {
                return false;
            }
            float life = Age / (float)Lifetime;
            float opacity = 1f - life * life;
            VDWeaponFx.BeginAdditive();
            VDWeaponFx.DrawArc(points, VoidElectricFieldHoldout.BeamColor, VoidElectricFieldHoldout.CoreColor, 2.6f * (1f - life * 0.6f), opacity);
            VDWeaponFx.Glow(end, VoidElectricFieldHoldout.CoreColor * (0.7f * opacity), 26f);
            VDWeaponFx.End();
            return false;
        }
    }
}
