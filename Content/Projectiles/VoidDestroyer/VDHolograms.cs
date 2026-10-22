using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using VoidDestroyerNPC = CalamityEntropy.Content.NPCs.VoidDestroyer.VoidDestroyer;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 全息弹幕公共部分:通过 ai[0] 找到本体,本体不在对应状态时自行消散;节拍读本体 Context 的表现通道,不读状态私有量。
    /// 全部留在平面
    /// </summary>
    public abstract class VDHoloProjectile : VDDepthProjectile
    {
        public abstract VDStateIndex OwnerMode { get; }
        public abstract Color HoloColor { get; }
        public override bool WantsMarker => false;

        /// <summary>本体引用,无效时为 null</summary>
        protected VoidDestroyerNPC Owner {
            get {
                int idx = (int)Projectile.ai[0];
                if (idx < 0 || idx >= Main.maxNPCs) {
                    return null;
                }
                NPC npc = Main.npc[idx];
                if (!npc.active || npc.ModNPC is not VoidDestroyerNPC boss) {
                    return null;
                }
                return boss;
            }
        }

        /// <summary>本体仍在本状态里(且没死)</summary>
        protected bool OwnerActive {
            get {
                VoidDestroyerNPC boss = Owner;
                return boss != null && !boss.Dying && boss.CurrentStateIndex == OwnerMode;
            }
        }

        /// <summary>整体透明度:出现 12 帧渐显,寿命末尾 20 帧渐隐</summary>
        protected float HoloOpacity(float peak = 0.85f) {
            float fadeIn = MathHelper.Clamp(Projectile.localAI[1] / 12f, 0f, 1f);
            float fadeOut = MathHelper.Clamp(Projectile.timeLeft / 20f, 0f, 1f);
            return peak * Math.Min(fadeIn, fadeOut);
        }

        /// <summary>本体离开模式时收尾:把剩余寿命压到渐隐长度</summary>
        protected void FadeOutIfOrphaned() {
            if (!OwnerActive && Projectile.timeLeft > 20) {
                Projectile.timeLeft = 20;
            }
        }

        public static void SpawnHoloBurst(Vector2 pos, Color color) {
            for (int i = 0; i < 14; i++) {
                Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(3f, 9f);
                VDVfx.Spark(pos, v, color, Main.rand.NextFloat(0.5f, 1f), 1f, 22, gravity: true);
            }
        }
    }

    /// <summary>
    /// 红恶魔全息:没有伤害,只做演出,平面位置跟着本体的 AnchorPos(每轮换边),缩放 RedDevilScale,亮度按 HoloCharge 走。
    /// 红射线从它打到脚下的光盘,三叉戟由本体状态从它的位置射出
    /// </summary>
    public class VDHoloRedDevil : VDHoloProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override VDStateIndex OwnerMode => VDStateIndex.RedHell;
        public override Color HoloColor => VDHologramDraw.HellRed;
        public override int DefaultTimeLeft => 900;
        protected override float WhooshStrength => 0f;

        private Vector2 lastAnchor;
        private float blinkPop;

        public override void SetExtraDefaults() {
            Projectile.width = 40;
            Projectile.height = 60;
            Projectile.hostile = false;
        }

        public override bool? CanDamage() => false;
        public override bool ShouldUpdatePosition() => false;

        protected override void DepthAI() {
            Projectile.localAI[1]++;
            FadeOutIfOrphaned();
            VoidDestroyerNPC boss = Owner;
            if (boss == null) {
                return;
            }
            //本体进入收尾拍时提前渐隐
            if (boss.CurrentStateIndex == OwnerMode && boss.Context.HoloWrapUp && Projectile.timeLeft > 20) {
                Projectile.timeLeft = 20;
            }
            Vector2 anchor = boss.AnchorPos;
            if (Projectile.localAI[1] <= 1f) {
                lastAnchor = anchor;
                Projectile.Center = anchor;
            }
            if (Vector2.DistanceSquared(anchor, lastAnchor) > 16f) {
                //锚点换了 = 红恶魔传送:旧位置爆一圈全息碎片,新位置弹一下
                if (!Main.dedServ) {
                    SpawnHoloBurst(Projectile.Center, HoloColor);
                    SpawnHoloBurst(anchor, HoloColor);
                    CEUtils.PlaySound("vbapear", 0.9f, anchor, 4, 0.8f);
                }
                lastAnchor = anchor;
                blinkPop = 1f;
            }
            Projectile.Center = anchor + new Vector2(0, MathF.Sin(Projectile.localAI[1] * 0.06f) * 8f);
            blinkPop *= 0.85f;
            Projectile.direction = boss.Target.Center.X > Projectile.Center.X ? 1 : -1;

            //蓄力粒子:跟本体声明的蓄力读数走
            if (!Main.dedServ && !boss.Context.HoloWrapUp && boss.Context.HoloCharge > 0.02f && Main.rand.NextBool(2)) {
                Vector2 from = Projectile.Center + CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(70f, 160f);
                Vector2 v = (Projectile.Center - from) * 0.08f;
                VDVfx.Spark(from, v, HoloColor, Main.rand.NextFloat(0.4f, 0.8f), 0.9f, 13);
            }
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Main.instance.LoadNPC(NPCID.RedDevil);
            Texture2D tex = TextureAssets.Npc[NPCID.RedDevil].Value;
            int frames = Math.Max(1, Main.npcFrameCount[NPCID.RedDevil]);
            int frameH = tex.Height / frames;
            int frame = (int)(Main.GlobalTimeWrappedHourly * 7f) % frames;
            Rectangle src = new Rectangle(0, frame * frameH, tex.Width, frameH);
            Vector2 origin = new Vector2(tex.Width / 2f, frameH / 2f);
            SpriteEffects fx = Projectile.direction > 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            float opacity = HoloOpacity();
            float scale = VDDirector.RedDevilScale + blinkPop * 0.6f;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            VoidDestroyerNPC boss = Owner;
            float charge = 0f;
            if (boss != null && boss.CurrentStateIndex == OwnerMode && !boss.Context.HoloWrapUp) {
                charge = MathHelper.Clamp(boss.Context.HoloCharge, 0f, 1f);
            }

            spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            spriteBatch.Draw(glow, drawPos, null, HoloColor * (0.35f * opacity), 0f, glow.Size() / 2f, (0.9f + blinkPop * 0.3f) * scale / VDDirector.RedDevilScale, SpriteEffects.None, 0f);
            if (charge > 0f) {
                spriteBatch.Draw(glow, drawPos, null, new Color(255, 160, 120) * (0.8f * charge * opacity), 0f, glow.Size() / 2f, (0.35f + 0.45f * charge) * scale / VDDirector.RedDevilScale, SpriteEffects.None, 0f);
            }
            CEUtils.ReSetToEndShader();

            VDHologramDraw.Draw(tex, drawPos, src, HoloColor, opacity, 0f, origin, scale, fx);
        }
    }

    /// <summary>全息邪恶三叉戟:从红恶魔处匀速直飞;画大 1.4 倍,拖尾是同一张贴图的全息残影。小激光档伤害</summary>
    public class VDHoloTrident : VDDepthProjectile
    {
        public const float BodyScale = 1.4f;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DefaultTimeLeft => 150;

        public override void SetExtraDefaults() {
            Projectile.width = 24;
            Projectile.height = 24;
        }

        protected override void DepthAI() {
            Projectile.localAI[1]++;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver4;
            Lighting.AddLight(Projectile.Center, VDHologramDraw.HellRed.ToVector3() * 0.45f);
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Main.instance.LoadProjectile(ProjectileID.UnholyTridentHostile);
            Texture2D tex = TextureAssets.Projectile[ProjectileID.UnholyTridentHostile].Value;
            Vector2 origin = tex.Size() / 2f;
            float opacity = MathHelper.Clamp(Projectile.localAI[1] / 8f, 0f, 1f) * 0.95f;
            if (opacity <= 0.01f) {
                return;
            }
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            spriteBatch.Draw(glow, drawPos, null, VDHologramDraw.HellRed * (0.4f * opacity), 0f, glow.Size() / 2f, 0.32f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
            VDHologramDraw.Begin();
            for (int i = trailCount - 1; i >= 1; i--) {
                float a = (1f - i / (float)TrailLength) * 0.45f;
                VDHologramDraw.DrawPart(tex, trailPos[i] - Main.screenPosition, null, VDHologramDraw.HellRed, opacity * a, Projectile.rotation, origin, BodyScale * (1f - i * 0.04f), SpriteEffects.None);
            }
            VDHologramDraw.DrawPart(tex, drawPos, null, VDHologramDraw.HellRed, opacity, Projectile.rotation, origin, BodyScale, SpriteEffects.None);
            VDHologramDraw.End();
        }
    }

    /// <summary>
    /// 全息陆龟:每次冲锋在玩家左右 TortoiseStartOffset 处现身(屏内),待机期画出整条水平航道,y 在现身那一刻锁死,
    /// 起冲时扇形 StingerCount 发毒刺,沿航道直冲不修正;下一冲换到另一侧。ai[0] 本体,ai[1] 目标玩家,ai[2] 首冲侧向(0 = 自选)。小激光档伤害
    /// </summary>
    public class VDHoloTortoise : VDHoloProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override VDStateIndex OwnerMode => VDStateIndex.GreenJungle;
        public override Color HoloColor => VDHologramDraw.JungleGreen;
        public override int DefaultTimeLeft => 1200;
        protected override float WhooshStrength => 0f;

        /// <summary>服务端状态机:0 现身待机,1 冲刺,2 收尾。客户端不跑状态机,只按同步来的速度判定是否在冲刺</summary>
        private int phase;
        private int phaseTimer;
        private int dashesDone;
        private float spin;
        private Vector2 lastPos;
        private bool wasDashing;
        /// <summary>本地的待机计时(客户端用它画航道渐亮;冲刺中归零)</summary>
        private int idleAge;

        public override void SetExtraDefaults() {
            Projectile.width = 128;
            Projectile.height = 128;
        }

        /// <summary>速度表示同步过来的是否在冲刺,待机期速度是零</summary>
        public bool Dashing => Projectile.velocity.LengthSquared() > 1f;

        public override bool ShouldUpdatePosition() => Dashing;

        private int TotalDashes => VDDirector.JungleDashes(Owner != null ? Owner.Phase : 1);

        /// <summary>下一冲的航向:从现身那一侧朝另一侧(ai[2] 记的是现身侧)</summary>
        private float DashDir => -Math.Sign(Projectile.ai[2] == 0f ? 1f : Projectile.ai[2]);

        protected override void DepthAI() {
            Projectile.localAI[1]++;
            FadeOutIfOrphaned();
            Player target = TargetPlayer(1);
            if (target == null) {
                Projectile.Kill();
                return;
            }
            if (Projectile.localAI[1] <= 1f) {
                lastPos = Projectile.Center;
            }

            if (IsServer) {
                ServerStateMachine(target);
            }

            bool dashing = Dashing;
            idleAge = dashing ? 0 : idleAge + 1;
            spin += dashing ? 0.35f * Math.Sign(Projectile.velocity.X == 0f ? 1f : Projectile.velocity.X) : 0.08f;
            Projectile.rotation = spin;
            Lighting.AddLight(Projectile.Center, HoloColor.ToVector3() * 0.6f);

            //客户端演出靠「位置跳变 / 冲刺起落」这两个可观测量,不依赖服务端私有状态
            if (!Main.dedServ) {
                if (Vector2.DistanceSquared(Projectile.Center, lastPos) > 300f * 300f) {
                    SpawnHoloBurst(lastPos, HoloColor);
                    SpawnHoloBurst(Projectile.Center, HoloColor);
                    CEUtils.PlaySound("vbapear", 0.8f, Projectile.Center, 4, 0.9f);
                }
                if (dashing && !wasDashing) {
                    CEUtils.PlaySound("CruiserDash", 0.8f, Projectile.Center, 3, 0.9f);
                }
                if (dashing && Main.rand.NextBool(2)) {
                    Vector2 v = -Projectile.velocity * 0.1f + CEUtils.randomPointInCircle(2f);
                    VDVfx.Spark(Projectile.Center + CEUtils.randomPointInCircle(50f), v, HoloColor, Main.rand.NextFloat(0.5f, 1f), 0.9f, 18);
                }
            }
            lastPos = Projectile.Center;
            wasDashing = dashing;
        }

        private void ServerStateMachine(Player target) {
            if (phase == 0) {
                if (phaseTimer == 0) {
                    //首冲按玩家移动方向选侧(站着不动取本体当前侧向),之后每冲换边
                    if (Projectile.ai[2] == 0) {
                        Projectile.ai[2] = Math.Abs(target.velocity.X) > 0.5f ? Math.Sign(target.velocity.X) : (Owner != null ? Owner.Context.SideDir : 1);
                    }
                    else if (dashesDone > 0) {
                        Projectile.ai[2] = -Projectile.ai[2];
                    }
                    Projectile.Center = target.Center + new Vector2(Projectile.ai[2] * VDDirector.TortoiseStartOffset, 0f);
                    Projectile.velocity = Vector2.Zero;
                    Projectile.netUpdate = true;
                }
                phaseTimer++;
                if (phaseTimer >= VDDirector.TortoiseAppearFrames) {
                    //起冲:沿航道定速横冲,同时扇形 4 发毒刺
                    Projectile.velocity = new Vector2(DashDir * VDDirector.TortoiseDashSpeed, 0f);
                    FireStingers(target);
                    Projectile.netUpdate = true;
                    phase = 1;
                    phaseTimer = 0;
                }
                return;
            }
            if (phase == 1) {
                phaseTimer++;
                if (phaseTimer % 30 == 0) {
                    Projectile.netUpdate = true;
                }
                if (phaseTimer >= VDDirector.TortoiseDashFrames) {
                    dashesDone++;
                    Projectile.velocity = Vector2.Zero;
                    Projectile.netUpdate = true;
                    if (dashesDone >= TotalDashes) {
                        Projectile.timeLeft = Math.Min(Projectile.timeLeft, 20);
                        phase = 2;
                    }
                    else {
                        phase = 0;
                        phaseTimer = 0;
                    }
                }
            }
        }

        /// <summary>扇形全息毒刺,朝玩家,匀速</summary>
        private void FireStingers(Player target) {
            Vector2 aim = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            int n = VDDirector.StingerCount;
            for (int i = 0; i < n; i++) {
                float ang = MathHelper.ToRadians(VDDirector.StingerSpreadDeg * (n == 1 ? 0f : -1f + 2f * i / (n - 1)));
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, aim.RotatedBy(ang) * VDDirector.StingerSpeed, ModContent.ProjectileType<VDHoloStinger>(), Owner != null ? Owner.ProjDamage(VDDirector.DmgStinger) : VDDirector.DmgStinger, 0f, Main.myPlayer);
            }
        }

        protected override bool? CollidingOnPlane(Rectangle projHitbox, Rectangle targetHitbox) {
            if (!Dashing) {
                return false;
            }
            return projHitbox.Intersects(targetHitbox);
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Main.instance.LoadNPC(NPCID.GiantTortoise);
            Texture2D tex = TextureAssets.Npc[NPCID.GiantTortoise].Value;
            int frames = Math.Max(1, Main.npcFrameCount[NPCID.GiantTortoise]);
            int frameH = tex.Height / frames;
            Rectangle src = new Rectangle(0, 0, tex.Width, frameH);
            Vector2 origin = new Vector2(tex.Width / 2f, frameH / 2f);
            float scale = 128f / Math.Max(tex.Width, frameH) * 1.15f;
            float opacity = HoloOpacity(0.9f);
            if (opacity <= 0.01f) {
                return;
            }
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            //待机期的航道:从现身处朝冲刺方向横贯一屏,随待机计时渐亮
            if (!Dashing && Projectile.timeLeft > 20) {
                float lane = MathHelper.Clamp(idleAge / (float)VDDirector.TortoiseAppearFrames, 0f, 1f);
                VDVfx.DrawLane(Projectile.Center, new Vector2(DashDir, 0f), VDDirector.TortoiseDashSpeed * VDDirector.TortoiseDashFrames, VDDirector.TortoiseLaneWidth, HoloColor, 0.25f + 0.75f * lane);
            }

            spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            spriteBatch.Draw(glow, drawPos, null, HoloColor * (0.4f * opacity), 0f, glow.Size() / 2f, 0.9f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();

            VDHologramDraw.Begin();
            if (Dashing) {
                for (int i = 1; i < trailCount && i <= 4; i++) {
                    VDHologramDraw.DrawPart(tex, trailPos[i] - Main.screenPosition, src, HoloColor, opacity * (0.3f - i * 0.06f), Projectile.rotation - i * 0.2f, origin, scale, SpriteEffects.None);
                }
            }
            VDHologramDraw.DrawPart(tex, drawPos, src, HoloColor, opacity, Projectile.rotation, origin, scale, SpriteEffects.None);
            VDHologramDraw.End();
        }
    }

    /// <summary>全息青苔黄蜂毒刺:匀速直飞,弹幕档伤害</summary>
    public class VDHoloStinger : VDHostileProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DefaultTimeLeft => 120;

        public override void SetExtraDefaults() {
            Projectile.width = 14;
            Projectile.height = 14;
        }

        public override void AI() {
            Projectile.localAI[1]++;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }

        public override bool PreDraw(ref Color lightColor) {
            Main.instance.LoadProjectile(ProjectileID.Stinger);
            Texture2D tex = TextureAssets.Projectile[ProjectileID.Stinger].Value;
            float opacity = MathHelper.Clamp(Projectile.localAI[1] / 6f, 0f, 1f) * 0.95f;
            Main.spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null, VDHologramDraw.JungleGreen * (0.35f * opacity), 0f, glow.Size() / 2f, 0.2f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
            VDHologramDraw.Draw(tex, Projectile.Center - Main.screenPosition, null, VDHologramDraw.JungleGreen, opacity, Projectile.rotation, tex.Size() / 2f, 1.9f, SpriteEffects.None);
            return false;
        }
    }

    /// <summary>
    /// 全息小白龙:在玩家上方 SkyPassHeight 处横越两程,中心与高度在放出那一刻锁死,不跟人也不脱轨;
    /// 龙头每越过一个格点就落一朵雪花,第二程反向且格点错半格。所有龙节都有判定。
    /// ai[0] 本体,ai[1] 横越中心 x,ai[2] 龙头高度 y,首程方向取本体 SideDir;体术档伤害
    /// </summary>
    public class VDHoloWyvern : VDHoloProjectile
    {
        public const int SegmentCount = VDDirector.SkyWyvernSegments;
        public const float SegmentLength = 44f;
        public const float SegmentScale = 2f;
        /// <summary>龙头横越时的上下摆幅与频率(rad/帧)</summary>
        private const float BobAmp = 28f;
        private const float BobFreq = 0.1f;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override VDStateIndex OwnerMode => VDStateIndex.BlueSky;
        public override Color HoloColor => VDHologramDraw.SkyBlue;
        public override int DefaultTimeLeft => VDDirector.SkyWyvernLife;
        protected override float WhooshStrength => 0f;

        private readonly Vector2[] segments = new Vector2[SegmentCount];
        /// <summary>当前所在程,换程时龙身在屏外另一侧重排</summary>
        private int pass = -1;
        private Vector2 lastHead;

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 3600;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 60;
            Projectile.height = 60;
        }

        public override bool ShouldUpdatePosition() => false;

        protected override void DepthAI() {
            Projectile.localAI[1]++;
            FadeOutIfOrphaned();
            VoidDestroyerNPC boss = Owner;
            if (boss == null) {
                Projectile.Kill();
                return;
            }
            int age = (int)Projectile.localAI[1];
            int k = Math.Min(age / VDDirector.SkyPassStride, VDDirector.SkyPasses - 1);
            int local = age - k * VDDirector.SkyPassStride;
            int side = boss.Context.SideDir >= 0 ? 1 : -1;
            float dir = k % 2 == 0 ? side : -side;
            float speed = 2f * VDDirector.SkyPassHalfSpan / VDDirector.SkyPassFrames;
            float startX = Projectile.ai[1] - dir * VDDirector.SkyPassHalfSpan;
            Vector2 head = new Vector2(startX + dir * speed * local, Projectile.ai[2] + MathF.Sin(local * BobFreq) * BobAmp);

            if (pass != k) {
                pass = k;
                lastHead = head;
                for (int i = 0; i < SegmentCount; i++) {
                    segments[i] = new Vector2(head.X - dir * i * SegmentLength, head.Y);
                }
            }
            if (IsServer) {
                DropFlakes(boss, k, dir, startX, lastHead.X, head);
            }
            lastHead = head;
            Projectile.Center = head;

            //蠕虫跟随:每节钉在前一节后方定长处
            segments[0] = head;
            for (int i = 1; i < SegmentCount; i++) {
                Vector2 diff = segments[i] - segments[i - 1];
                if (diff.LengthSquared() < 0.01f) {
                    diff = Vector2.UnitX;
                }
                segments[i] = segments[i - 1] + diff.SafeNormalize(Vector2.UnitX) * SegmentLength;
            }
            Lighting.AddLight(Projectile.Center, HoloColor.ToVector3() * 0.7f);
            if (!Main.dedServ && Main.rand.NextBool(3)) {
                int idx = Main.rand.Next(SegmentCount);
                VDVfx.Spark(segments[idx] + CEUtils.randomPointInCircle(20f), CEUtils.randomPointInCircle(1.5f), HoloColor, Main.rand.NextFloat(0.4f, 0.8f), 0.8f, 16);
            }
        }

        /// <summary>龙头本帧从 prevX 走到 head.X,每越过一个格点(沿航向距离 = 错位 + j × 间距)就在该处落一朵雪花;第二程错半格</summary>
        private void DropFlakes(VoidDestroyerNPC boss, int k, float dir, float startX, float prevX, Vector2 head) {
            float spacing = VDDirector.SkyFlakeSpacing;
            float offset = k % 2 == 0 ? 0f : spacing * 0.5f;
            float span = 2f * VDDirector.SkyPassHalfSpan;
            float along0 = dir * (prevX - startX);
            float along1 = dir * (head.X - startX);
            int damage = boss.ProjDamage(VDDirector.DmgSnowflake);
            int type = ModContent.ProjectileType<VDHoloSnowflake>();
            for (int j = Math.Max(0, (int)MathF.Floor((along0 - offset) / spacing) + 1); ; j++) {
                float d = offset + j * spacing;
                if (d > along1 || d > span) {
                    break;
                }
                Vector2 pos = new Vector2(startX + dir * d, head.Y + 24f);
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), pos, new Vector2(0f, VDDirector.SkyFlakeFall), type, damage, 0f, Main.myPlayer);
            }
        }

        protected override bool? CollidingOnPlane(Rectangle projHitbox, Rectangle targetHitbox) {
            if (Projectile.localAI[1] < 12f) {
                return false;
            }
            //龙节贴图约 32px 宽,放大 2 倍后判定盒取 72,略宽于可见身体
            int half = (int)(18f * SegmentScale);
            for (int i = 0; i < SegmentCount; i++) {
                Rectangle seg = new Rectangle((int)segments[i].X - half, (int)segments[i].Y - half, half * 2, half * 2);
                if (seg.Intersects(targetHitbox)) {
                    return true;
                }
            }
            return false;
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Main.instance.LoadNPC(NPCID.WyvernHead);
            Main.instance.LoadNPC(NPCID.WyvernLegs);
            Main.instance.LoadNPC(NPCID.WyvernBody);
            Main.instance.LoadNPC(NPCID.WyvernBody2);
            Main.instance.LoadNPC(NPCID.WyvernBody3);
            Main.instance.LoadNPC(NPCID.WyvernTail);
            float opacity = HoloOpacity(0.85f);
            if (opacity <= 0.01f) {
                return;
            }

            //尾先画,头压在最上面
            VDHologramDraw.Begin();
            for (int i = SegmentCount - 1; i >= 0; i--) {
                int type;
                if (i == 0) {
                    type = NPCID.WyvernHead;
                }
                else if (i == SegmentCount - 1) {
                    type = NPCID.WyvernTail;
                }
                else if (i % 12 == 3) {
                    type = NPCID.WyvernLegs;
                }
                else {
                    type = (i % 3) switch { 0 => NPCID.WyvernBody, 1 => NPCID.WyvernBody2, _ => NPCID.WyvernBody3 };
                }
                Texture2D tex = TextureAssets.Npc[type].Value;
                int frames = Math.Max(1, Main.npcFrameCount[type]);
                int frameH = tex.Height / frames;
                Rectangle src = new Rectangle(0, 0, tex.Width, frameH);
                Vector2 origin = new Vector2(tex.Width / 2f, frameH / 2f);
                Vector2 here = segments[i];
                Vector2 ahead = i == 0 ? here + (here - segments[1]) : segments[i - 1];
                float rot = (ahead - here).ToRotation() + MathHelper.PiOver2;
                VDHologramDraw.DrawPart(tex, here - Main.screenPosition, src, HoloColor, opacity, rot, origin, SegmentScale, SpriteEffects.None);
            }
            VDHologramDraw.End();
        }
    }

    /// <summary>全息雪花:小白龙横越时从龙身落下,匀速直落,不摆不加速;弹幕档伤害</summary>
    public class VDHoloSnowflake : VDHostileProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DefaultTimeLeft => VDDirector.SkyFlakeFallFrames + 60;

        public override void SetExtraDefaults() {
            Projectile.width = 28;
            Projectile.height = 28;
        }

        public override void AI() {
            Projectile.localAI[1]++;
            Projectile.rotation += 0.06f;
            Lighting.AddLight(Projectile.Center, VDHologramDraw.SkyBlue.ToVector3() * 0.35f);
        }

        public override bool PreDraw(ref Color lightColor) {
            Main.instance.LoadProjectile(ProjectileID.NorthPoleSnowflake);
            Texture2D tex = TextureAssets.Projectile[ProjectileID.NorthPoleSnowflake].Value;
            float opacity = MathHelper.Clamp(Projectile.localAI[1] / 6f, 0f, 1f) * MathHelper.Clamp(Projectile.timeLeft / 20f, 0f, 1f) * 0.95f;
            if (opacity <= 0.01f) {
                return false;
            }
            Vector2 pos = Projectile.Center - Main.screenPosition;
            Main.spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Main.spriteBatch.Draw(glow, pos, null, VDHologramDraw.SkyBlue * (0.35f * opacity), 0f, glow.Size() / 2f, 0.22f, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
            VDHologramDraw.Draw(tex, pos, null, VDHologramDraw.SkyBlue, opacity, Projectile.rotation, tex.Size() / 2f, 1.6f, SpriteEffects.None);
            return false;
        }
    }

}
