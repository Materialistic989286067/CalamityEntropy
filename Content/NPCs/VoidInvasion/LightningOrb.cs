using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using CalamityEntropy.Core.Graphics;
using CalamityEntropy.Utilities;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 闪电球(void-invasion.md §4.3 P3-2,M8):教皇背后扇形升起的可击破放电单位。
    /// 40,000 HP、无接触伤害、不掉落、不计事件进度(不挂 IVoidInvasionNPC);
    /// 每 50t 对玩家放一道 <see cref="VoidLightningBolt"/> 单放模式(30t 暗弧预警 → 电击 190 档),
    /// 各球错拍 10t 此起彼伏;12s 自爆,教皇消失/离开 P3/进入死亡演出时一并自毁(打球即减压)。
    /// ai[0] = 教皇 whoAmI,ai[1] = 扇位序号(0~4);槽位 = 领域锚点 + 扇形偏移,双端同式推导。
    /// 视觉全程序化:Bloom 光核叠层 + HeavenlyGaleLightningArc 短电弧(纯客户端,5t 重掷)。
    /// </summary>
    public class LightningOrb : ModNPC
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        [InnoVault.VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static ReLogic.Content.Asset<Texture2D> glowTex;

        /// <summary>自爆寿命(§4.3:12s)</summary>
        public const int SelfDestructAge = 720;
        /// <summary>放电间隔(§4.3:每 50t)</summary>
        public const int ZapInterval = 50;
        /// <summary>升起段时长</summary>
        public const int RiseTime = 40;

        private int SlotIndex => (int)NPC.ai[1];

        //本地演出计时(双端各自推进,位置由原生 NPC 同步兜底)
        private float age = 0;
        //放电节拍(仅服务端定夺;弹幕生成后原生同步)
        private int zapTimer = -1;
        //短电弧折线(纯客户端视觉)
        private readonly List<List<Vector2>> miniArcs = new();

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[NPC.type] = 1;
            NPCID.Sets.MustAlwaysDraw[NPC.type] = true;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
            NPCID.Sets.NPCBestiaryDrawModifiers hide = new NPCID.Sets.NPCBestiaryDrawModifiers();
            hide.Hide = true;
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = hide;
        }

        public override void SetDefaults()
        {
            NPC.width = 56;
            NPC.height = 56;
            NPC.damage = 0;
            NPC.defense = 40;
            NPC.lifeMax = 40000;
            NPC.HitSound = SoundID.NPCHit53;
            NPC.DeathSound = SoundID.NPCDeath56;
            NPC.value = 0f;
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.dontCountMe = true;
            NPC.npcSlots = 0.5f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return false; //无接触伤害(§4.3:威胁只来自放电)
        }

        private VoidPope Pope
        {
            get
            {
                int idx = (int)NPC.ai[0];
                if (idx < 0 || idx >= Main.maxNPCs)
                {
                    return null;
                }
                NPC n = Main.npc[idx];
                return n.active && n.ModNPC is VoidPope pope ? pope : null;
            }
        }

        /// <summary>扇位槽(§4.3:教皇背后扇形,锚点取领域中心;双端同式)。</summary>
        private Vector2 SlotOffset()
        {
            float ang = -MathHelper.PiOver2 + (SlotIndex - 2) * 0.46f;
            float bobY = (float)Math.Sin(age * 0.045f + SlotIndex * 1.3f) * 14f;
            return ang.ToRotationVector2() * 330f + new Vector2(0f, bobY);
        }

        public override void AI()
        {
            age++;
            VoidPope pope = Pope;
            //守护条件(§4.3):教皇没了 / 不在 P3 / 死亡演出中 → 自毁(StrikeInstantKill 走打击广播,双端都播 HitEffect)
            if (pope == null || pope.phase < 3 || pope.State == VoidPope.PopeState.P3Death)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.StrikeInstantKill();
                    NPC.netUpdate = true;
                }
                return;
            }

            //12s 自爆(服务端定夺)
            if (age >= SelfDestructAge && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.StrikeInstantKill();
                NPC.netUpdate = true;
                return;
            }

            //槽位跟随:升起段自教皇心缓出,之后悬浮微漂(双端同式,原生同步兜底)
            Vector2 anchor = pope.DomainAnchor;
            float rise = MathHelper.Clamp(age / (float)RiseTime, 0f, 1f);
            float riseEase = 1f - (1f - rise) * (1f - rise);
            Vector2 want = anchor + SlotOffset() * riseEase;
            NPC.Center = Vector2.Lerp(NPC.Center, want, 0.18f);
            NPC.velocity = Vector2.Zero;

            //放电(仅服务端):首放 90t + 槽位错拍 10t,此后每 50t;弹幕自带 30t 暗弧预警
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                if (zapTimer < 0)
                {
                    zapTimer = 90 + SlotIndex * 10;
                }
                zapTimer--;
                if (zapTimer <= 0)
                {
                    zapTimer = ZapInterval;
                    Player target = pope.NPC.HasValidTarget
                        ? Main.player[pope.NPC.target]
                        : Main.player[Player.FindClosest(NPC.Center, 1, 1)];
                    if (target != null && target.active && !target.dead)
                    {
                        int damage = (int)(pope.NPC.defDamage * 0.559f + 0.5f); //电击 190 经典档
                        Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitY);
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, dir * 0.02f,
                            ModContent.ProjectileType<VoidLightningBolt>(), damage, 3f, -1, 0f, 1f);
                    }
                }
            }

            //常燃电弧视觉(纯客户端,5t 重掷短折线)
            if (!Main.dedServ && (int)age % 5 == 0)
            {
                RollMiniArcs();
            }
            Lighting.AddLight(NPC.Center, 0.55f, 0.35f, 0.95f);
        }

        /// <summary>核心周身的短电弧折线(纯客户端)。</summary>
        private void RollMiniArcs()
        {
            miniArcs.Clear();
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = NPC.Center + CEUtils.randomRot().ToRotationVector2() * 30f;
                Vector2 b = NPC.Center + CEUtils.randomRot().ToRotationVector2() * 30f;
                miniArcs.Add(LightningGenerator.GenerateLightning(a, b, 12f, 4));
            }
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.dedServ)
            {
                return;
            }
            if (NPC.life > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + CEUtils.randomPointInCircle(24f),
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), new Color(180, 140, 255), 0.4f);
                    p.Configure(0.85f, lifetime: 12);
                }
                return;
            }
            //击破/自爆:电光炸裂(§4.3:打球即减压的爽拍)
            SoundEngine.PlaySound(SoundID.Item94 with { Volume = 1f, Pitch = -0.2f }, NPC.Center);
            PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(180, 140, 255), 0.1f).Configure(4f, 30);
            for (int i = 0; i < 26; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 9f), Color.White, 0.9f);
                v.Opacity = Main.rand.Next(30, 90) * 0.01f;
            }
            for (int i = 0; i < 10; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 7f), new Color(200, 160, 255), 0.55f);
                p.Configure(0.9f, lifetime: 18);
            }
        }

        public float ArcWidth(float completionRatio, Vector2 vertex)
        {
            return 5f;
        }

        public Color ArcColor(float completionRatio, Vector2 vertex)
        {
            float lerp = (float)Math.Sin(NPC.whoAmI * 1.7f + completionRatio * 12f + Main.GlobalTimeWrappedHourly * 1.4f) * 0.5f + 0.5f;
            return CEUtils.MulticolorLerp(lerp, new Color(215, 165, 255), new Color(130, 70, 230));
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return false;
            }
            //升起渐显
            float alpha = MathHelper.Clamp(age / 20f, 0f, 1f);
            float pulse = 1f + 0.1f * (float)Math.Sin(age * 0.12f);
            //自爆前 60t 危险闪烁(可读性:快炸的球更亮更急)
            if (age > SelfDestructAge - 60)
            {
                pulse += 0.2f * (float)Math.Sin(age * 0.5f);
            }

            //Bloom 光核叠层(加法)
            spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            Vector2 pos = NPC.Center - screenPos;
            spriteBatch.Draw(glow, pos, null, new Color(120, 60, 220) * (0.85f * alpha), 0, glow.Size() / 2, 1.5f * pulse, SpriteEffects.None, 0);
            spriteBatch.Draw(glow, pos, null, new Color(185, 140, 255) * (0.9f * alpha), 0, glow.Size() / 2, 0.95f * pulse, SpriteEffects.None, 0);
            spriteBatch.Draw(glow, pos, null, Color.White * (0.75f * alpha), 0, glow.Size() / 2, 0.5f * pulse, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();

            //短电弧条带(镜像 VoidLightningBolt 姿势,RenderTrail 自管 GPU 状态)
            if (miniArcs.Count > 0 && alpha > 0.5f)
            {
                GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"].UseImage1("Images/Misc/Perlin");
                GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"].Apply();
                foreach (var points in miniArcs)
                {
                    CEPrimitiveRenderer.RenderTrail(points, new CEPrimitiveSettings(ArcWidth, ArcColor,
                        (_, _) => Vector2.Zero, false,
                        shader: GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"]), 6);
                }
            }
            return false;
        }
    }
}
