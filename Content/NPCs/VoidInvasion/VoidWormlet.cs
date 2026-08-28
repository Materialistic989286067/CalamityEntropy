using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空蠕虫(void-invasion.md §4.1 P1-1 / §4.2 P2-1s,教皇门中演出怪):锚定传送门上探身半截,
    /// 6 节(掠食者头+体节贴图 scale 0.55,深紫染色)绘制在一个 NPC 里;头摆动扫 30° 扇形,
    /// 错拍点火喷 2 波虚空火(VoidFlameBreath scale 0.8);不可被击中,有头部接触判定,随门关闭消失。
    /// ai[0] = 教皇 whoAmI;ai[1] = 探身段时长;ai[2] = 点火错拍偏移(+1000 = P2-1s 脱门模式);
    /// ai[3] = 年龄(双端各自推进)。门的开/关节拍由生成侧对齐(门 40t 张开,蠕虫 40t 后探身)。
    /// 脱门段(§4.2 P2-1s):喷火结束后整条脱门而出,8px/t 小幅度追踪(转向 ≤0.02rad/t)弧线飞 3s,
    /// 身后新开小门钻入消失;脱门期维持 dontTakeDamage(纯威胁演出),蛇链沿头部飞行路径回溯绘制。
    /// </summary>
    public class VoidWormlet : ModNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head";

        //部件贴图只在绘制路径读取(专用服务器上恒为 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head")]
        private static Asset<Texture2D> headTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/body", 1, 2, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] bodyTextures;

        private const float WormScale = 0.55f;
        private const int SegmentCount = 6;
        private const float SegmentGap = 36f;
        /// <summary>探身长度(半截身位)</summary>
        private const float EmergeLength = 150f;
        private const int EmergeStart = 40;  //对齐门的张开前摇
        private const int EmergeTime = 20;
        private const int RetractTime = 30;
        /// <summary>点火拍基准(首波),第二波 +65t</summary>
        private const int FireBase = 70;
        private const int SecondWaveDelay = 65;

        //———脱门段(§4.2 P2-1s)———
        private const float BreakoutSpeed = 8f;
        private const float BreakoutTurn = 0.02f;
        private const int BreakoutFlyTime = 180;
        private const int BreakoutDiveTime = 25;

        public int PopeIndex => (int)NPC.ai[0];
        public int Lifetime => (int)NPC.ai[1];
        public int FireStagger => (int)NPC.ai[2] % 1000;
        /// <summary>P2-1s 脱门模式(ai[2] 加 1000 编码,探身段结束后不缩回而是脱门而出)</summary>
        public bool Breakout => NPC.ai[2] >= 1000;
        public float Age => NPC.ai[3];

        /// <summary>脱门后的头部飞行路径(双端各自记录,蛇链绘制回溯用)</summary>
        private readonly List<Vector2> flightPath = new List<Vector2>();

        //锚点(门心),首帧从生成位置转存;双端一致
        private Vector2 Anchor
        {
            get => new Vector2(NPC.localAI[0], NPC.localAI[1]);
            set { NPC.localAI[0] = value.X; NPC.localAI[1] = value.Y; }
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[NPC.type] = 1;
            NPCID.Sets.MustAlwaysDraw[NPC.type] = true;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetDefaults()
        {
            NPCID.Sets.NPCBestiaryDrawModifiers hide = new NPCID.Sets.NPCBestiaryDrawModifiers();
            hide.Hide = true;
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = hide;
            NPC.width = 60;
            NPC.height = 60;
            NPC.damage = 150;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.defense = 100;
            NPC.lifeMax = 10000;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 0f;
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.dontCountMe = true;
            NPC.dontTakeDamage = true;
        }

        public override bool CheckActive()
        {
            return false;
        }

        /// <summary>探身比例:出巢锐利缓出,收巢平滑缓入;脱门模式无收巢段(恒全探)。</summary>
        public float EmergeProgress
        {
            get
            {
                float age = Age;
                if (age < EmergeStart)
                {
                    return 0f;
                }
                float outP = MathHelper.Clamp((age - EmergeStart) / EmergeTime, 0f, 1f);
                float emerge = 1f - (1f - outP) * (1f - outP) * (1f - outP);
                if (Breakout)
                {
                    return emerge;
                }
                float retractP = MathHelper.Clamp((Lifetime - age) / RetractTime, 0f, 1f);
                return emerge * retractP;
            }
        }

        /// <summary>是否已进入脱门段(§4.2 P2-1s:探身段走完即整条飞出)。</summary>
        public bool InBreakout => Breakout && Age >= Lifetime;

        /// <summary>头部朝向:朝教皇目标的基准角 + ±15° 正弦摆动(§4.1:扫 30° 扇形)。</summary>
        public float HeadAngle
        {
            get
            {
                float baseAim = NPC.HasValidTarget
                    ? (Main.player[NPC.target].Center - Anchor).ToRotation()
                    : NPC.rotation;
                return baseAim + (float)Math.Sin(Age * 0.09f) * MathHelper.ToRadians(15);
            }
        }

        public override void AI()
        {
            if (NPC.localAI[2] == 0)
            {
                NPC.localAI[2] = 1;
                Anchor = NPC.Center;
            }
            NPC.ai[3]++;
            NPC.velocity = Vector2.Zero;

            NPC pope = PopeIndex >= 0 && PopeIndex < Main.maxNPCs ? Main.npc[PopeIndex] : null;
            if (pope == null || !pope.active || pope.ModNPC is not VoidPope)
            {
                //教皇没了:演出怪立即随之消失(双端各自判,结果一致)
                NPC.active = false;
                NPC.netUpdate = true;
                return;
            }
            NPC.target = pope.target;

            float age = Age;

            //———脱门段(§4.2 P2-1s):8px/t 小幅度追踪 3s → 身后开小门钻入消失———
            if (InBreakout)
            {
                BreakoutAI(age);
                return;
            }

            float emerge = EmergeProgress;

            //本体中心贴头位(接触判定 = 撕咬头),锚点保持门心
            Vector2 dir = HeadAngle.ToRotationVector2();
            NPC.Center = Anchor + dir * EmergeLength * emerge;

            if (age == EmergeStart && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath13 with { Volume = 0.7f, Pitch = -0.5f }, Anchor);
            }

            //两波喷火(§4.1:错拍 15t 依次点火,火用 VoidFlameBreath scale 0.8,默认 50t 一波)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                float wave1 = FireBase + FireStagger;
                float wave2 = wave1 + SecondWaveDelay;
                if (age == wave1 || age == wave2)
                {
                    //初始喷向偏 ±15°,火流自身向目标缓转,自然扫出扇形
                    float tilt = age == wave1 ? MathHelper.ToRadians(15) : MathHelper.ToRadians(-15);
                    Vector2 fireDir = (HeadAngle + tilt).ToRotationVector2();
                    int damage = (int)(NPC.defDamage * 0.533f + 0.5f); //火 160 经典档(敌对弹幕命中 ×2)
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, fireDir,
                        ModContent.ProjectileType<VoidFlameBreath>(), damage, 2f, -1, NPC.whoAmI, 0.8f);
                }
                //随门关闭消失(脱门模式在 InBreakout 分支接管,不走这里)
                if (age >= Lifetime && !Breakout)
                {
                    NPC.active = false;
                    if (Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                    }
                }
            }

            //探身期的门口粒子
            if (!Main.dedServ && emerge > 0.05f && emerge < 0.95f && Main.rand.NextBool(3))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Anchor + CEUtils.randomPointInCircle(30f),
                    dir * Main.rand.NextFloat(1f, 3f), Color.White, 1f);
                v.Opacity = Main.rand.Next(20, 70) * 0.01f;
            }
        }

        /// <summary>
        /// 脱门段:头部以 velocity 驱动(原生同步),朝目标转向 ≤0.02rad/t 弧线飞 3s,
        /// 之后身后开小门(服务端)+ 渐隐钻入消失。蛇链由 flightPath 回溯绘制。
        /// </summary>
        private void BreakoutAI(float age)
        {
            float flyAge = age - Lifetime;
            //首帧点火:沿当前头向飞出
            if (flyAge == 0)
            {
                NPC.velocity = HeadAngle.ToRotationVector2() * BreakoutSpeed;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.NPCDeath13 with { Volume = 0.9f, Pitch = -0.2f }, NPC.Center);
                }
            }
            else if (flyAge < BreakoutFlyTime)
            {
                //小幅度追踪(§4.2:转向 ≤0.02rad/t)
                if (NPC.HasValidTarget)
                {
                    float want = (Main.player[NPC.target].Center - NPC.Center).ToRotation();
                    float cur = NPC.velocity.ToRotation();
                    float turn = MathHelper.Clamp(MathHelper.WrapAngle(want - cur), -BreakoutTurn, BreakoutTurn);
                    NPC.velocity = (cur + turn).ToRotationVector2() * BreakoutSpeed;
                }
            }
            else if (flyAge == BreakoutFlyTime)
            {
                //前方开小门(服务端;门只是演出,钻入靠渐隐)
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 doorPos = NPC.Center + NPC.velocity.SafeNormalize(Vector2.UnitX) * BreakoutSpeed * BreakoutDiveTime;
                    VoidPortal.Open(NPC.GetSource_FromAI(), doorPos, NPC.velocity, BreakoutDiveTime + 30, 0.7f);
                }
            }
            else if (flyAge >= BreakoutFlyTime + BreakoutDiveTime)
            {
                //钻入完成:消失(双端各自判,结果一致)
                NPC.active = false;
                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                }
                return;
            }
            NPC.rotation = NPC.velocity.ToRotation();

            //飞行路径记录(蛇链回溯)
            flightPath.Add(NPC.Center);
            if (flightPath.Count > 90)
            {
                flightPath.RemoveAt(0);
            }
            //飞行粒子尾
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, -NPC.velocity * 0.1f, Color.White, 0.9f);
                v.Opacity = Main.rand.Next(20, 60) * 0.01f;
            }
        }

        /// <summary>钻入段透明度(脱门尾声渐隐)。</summary>
        private float BreakoutAlpha
        {
            get
            {
                if (!InBreakout)
                {
                    return 1f;
                }
                float diveAge = Age - Lifetime - BreakoutFlyTime;
                return diveAge <= 0 ? 1f : MathHelper.Clamp(1f - diveAge / BreakoutDiveTime, 0f, 1f);
            }
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            if (InBreakout)
            {
                //脱门期全程有头部接触判定,钻入渐隐后关闭
                return BreakoutAlpha > 0.5f;
            }
            return EmergeProgress > 0.5f;
        }

        /// <summary>沿飞行路径自头位回溯 backDist 的点(路径不足时向门心方向延伸补足)。</summary>
        private Vector2 PathPoint(float backDist)
        {
            if (flightPath.Count == 0)
            {
                return Anchor;
            }
            float remain = backDist;
            for (int i = flightPath.Count - 1; i > 0; i--)
            {
                float segLen = Vector2.Distance(flightPath[i], flightPath[i - 1]);
                if (segLen >= remain)
                {
                    return segLen <= 0.01f ? flightPath[i] : Vector2.Lerp(flightPath[i], flightPath[i - 1], remain / segLen);
                }
                remain -= segLen;
            }
            Vector2 first = flightPath[0];
            Vector2 toAnchor = (Anchor - first).SafeNormalize(Vector2.Zero);
            return first + toAnchor * remain;
        }

        /// <summary>脱门段蛇链绘制:各节沿头部飞行路径回溯取位。</summary>
        private void DrawBreakoutChain(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D head = headTex.Value;
            Color tint = drawColor.MultiplyRGB(new Color(150, 110, 235)) * BreakoutAlpha;
            float gap = SegmentGap * WormScale * 1.6f;
            for (int i = SegmentCount - 1; i >= 0; i--)
            {
                Vector2 pos = PathPoint(i * gap);
                Vector2 ahead = i == 0 ? pos + NPC.velocity : PathPoint((i - 1) * gap);
                float segAng = i == 0 ? NPC.rotation : (ahead - pos).ToRotation();
                float rot = segAng + MathHelper.PiOver2;
                Texture2D tex = i == 0 ? head : bodyTextures[(i - 1) % 2];
                spriteBatch.Draw(tex, pos - screenPos, null, tint, rot, tex.Size() / 2, WormScale, SpriteEffects.None, 0);
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            if (InBreakout)
            {
                if (BreakoutAlpha > 0.01f)
                {
                    DrawBreakoutChain(spriteBatch, screenPos, drawColor);
                }
                return false;
            }
            float emerge = EmergeProgress;
            if (emerge <= 0.02f)
            {
                return false;
            }
            Texture2D head = headTex.Value;
            float headAng = HeadAngle;
            Vector2 baseDir = headAng.ToRotationVector2();
            Vector2 perp = new Vector2(-baseDir.Y, baseDir.X);
            //深紫染色(§七:复用掠食者贴图的换皮手段)
            Color tint = drawColor.MultiplyRGB(new Color(150, 110, 235));

            //自尾向头绘制,蜿蜒 = 相位错开的正弦侧摆;缩进门里的节不画
            for (int i = SegmentCount - 1; i >= 0; i--)
            {
                float distAlong = EmergeLength * emerge - i * SegmentGap * WormScale * 1.6f;
                if (distAlong < -10f)
                {
                    continue;
                }
                float sway = (float)Math.Sin(Age * 0.07f + i * 0.8f) * 9f * emerge * Math.Min(1f, i * 0.5f);
                Vector2 pos = Anchor + baseDir * distAlong + perp * sway;
                //画布上方 = 朝向(掠食者美术约定),段朝向取指向前一节的方向
                float segAng = i == 0 ? headAng : (Anchor + baseDir * (distAlong + SegmentGap * WormScale * 1.6f) + perp * (float)Math.Sin(Age * 0.07f + (i - 1) * 0.8f) * 9f * emerge - pos).ToRotation();
                float rot = segAng + MathHelper.PiOver2;
                Texture2D tex = i == 0 ? head : bodyTextures[(i - 1) % 2];
                spriteBatch.Draw(tex, pos - screenPos, null, tint, rot, tex.Size() / 2, WormScale, SpriteEffects.None, 0);
            }
            return false;
        }
    }
}
