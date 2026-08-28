using CalamityEntropy.Assets.Register;
using InnoVault;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空教皇的手(void-invasion.md §4.0/§4.2/§4.3/§4.5):武器化装饰件,dontTakeDamage,不做转伤。
    /// ai[0] = 教皇 whoAmI,ai[1] = 方位(±1),ai[2] = handRole(0 巡航/1 举升/2 持镰/3 提灯/4 刺索,
    /// M8 增 5 拽体横扫/6 缚身垂落),ai[3] = 编队层(0 下/1 中/2 上;(方位, 层) 唯一定位 P2 六手,P1 双手恒层 0)。
    /// role 由教皇的同步状态双端确定性推导(本类逐帧拉取写入 ai[2],原生 ai 同步作兜底)。
    /// 保留:顶点条带拖尾、爪尖判定框(ModifyCollisionData)、抓投机制
    /// (被本手或"来源是手"的铁索命中 → 定身 8t 后向教皇方向抛出,<see cref="TryGrab"/>)。
    /// 教皇瞬移时手随切位快照并清拖尾,避免残留错误条带。
    /// 贴图:P1 双手各用一款(handP1_1/handP1_2);P2 六手两款按 (层+方位) 交错,
    /// 每手与躯干之间画 armP2 连接件(两点旋转 + X 向拉伸);换阶新增手自带 30t 渐显。
    /// M8 P3(§4.3):P2→P3 换阶时层 &gt;0 的四手渐隐(110t 拍由教皇服务端 despawn),
    /// 层 0 双手换 handP3 巨手贴图换参数(独立浮游,armP2 连接件停画,爪尖判定前伸加长);
    /// P3-3 横扫窗按 <see cref="VoidPope.SweepBeats"/> 同表大幅摆越;死亡演出凭教皇状态同拍坠落碎裂。
    /// </summary>
    public class VoidPopeHand : ModNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP1_1";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP1_1")]
        private static Asset<Texture2D> hand1Tex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP1_2")]
        private static Asset<Texture2D> hand2Tex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP2_1")]
        private static Asset<Texture2D> handP2aTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP2_2")]
        private static Asset<Texture2D> handP2bTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/armP2")]
        private static Asset<Texture2D> armTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/handP3")]
        private static Asset<Texture2D> handP3Tex;
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

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
            NPC.width = 76;
            NPC.height = 76;
            NPC.damage = 0;
            NPC.defense = 60;
            NPC.lifeMax = 1600000;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCHit1;
            NPC.value = 0f;
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.dontCountMe = true;
            NPC.dontTakeDamage = true;
        }

        public List<Vector2> oldPos = new List<Vector2>();
        public List<float> oldRots = new List<float>();

        public int direction
        {
            get { return (int)NPC.ai[1]; }
            set { NPC.ai[1] = value; }
        }

        /// <summary>手的姿态(§4.5:0 巡航/1 举升/2 持镰/3 提灯/4 刺索),存 ai[2]</summary>
        public byte handRole
        {
            get { return (byte)NPC.ai[2]; }
            set { NPC.ai[2] = value; }
        }

        /// <summary>编队层(§4.2 六手:0 下/1 中/2 上),存 ai[3];P1 双手恒 0</summary>
        public int layer
        {
            get { return (int)NPC.ai[3]; }
            set { NPC.ai[3] = value; }
        }

        public int counter1 = 6;
        private float bobCounter = 0;
        /// <summary>换阶新增手的渐显计数(本地视觉,P1 起始双手生成时跳满)</summary>
        private float spawnFade = 0;

        //———抓投机制(§4.0 保留):定身 8t 后向教皇方向抛出———
        public Player handPlayer = null;
        public int handPlayerTime = 0;

        /// <summary>抓握入口(本手接触或"来源是手"的铁索命中时调;在受击者本机结算)。</summary>
        public void TryGrab(Player target)
        {
            if (handPlayerTime <= 0)
            {
                handPlayer = target;
                handPlayerTime = 8;
            }
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return handPlayerTime <= 0;
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            TryGrab(target);
        }

        public override bool ModifyCollisionData(Rectangle victimHitbox, ref int immunityCooldownSlot, ref MultipliableFloat damageMultiplier, ref Rectangle npcHitbox)
        {
            //判定框在爪尖(沿朝向前伸;P3 巨手贴图更大,前伸与框同步放大)
            var pope = ((int)NPC.ai[0]).ToNPC().ModNPC as VoidPope;
            float reach = pope != null && pope.HandsP3 ? 96f : 64f;
            float boxScale = pope != null && pope.HandsP3 ? 1.5f : 1f;
            npcHitbox = (NPC.Center + NPC.rotation.ToRotationVector2() * reach * NPC.scale)
                .getRectCentered(NPC.width * NPC.scale * boxScale, NPC.height * NPC.scale * boxScale);
            return true;
        }

        public override bool CheckActive()
        {
            return false;
        }

        /// <summary>role 对应的跟随偏移(相对教皇中心);P1 双手用原表,P2 六手按 (招式, 层) 特化,P3 巨手独立远位表。</summary>
        private Vector2 RoleOffset(byte role, VoidPope pope)
        {
            float bob = (float)Math.Sin(bobCounter) * 10f;
            if (pope.HandsP3)
            {
                return RoleOffsetP3(role, pope, bob);
            }
            if (pope.phase >= 2)
            {
                return RoleOffsetP2(role, pope, bob);
            }
            switch (role)
            {
                case 1: //举升:掌间凝爆弹(爆弹吸附在 (0,-118))
                    return new Vector2(direction * 46f, -110f);
                case 2: //持镰:合握在镰柄下(虚影在 (0,-90))
                    return new Vector2(direction * 22f, -64f);
                case 3: //提灯:两侧平举
                    return new Vector2(direction * 150f, -18f + bob * 0.4f);
                case 4: //刺索:朝玩家侧前压
                    return new Vector2(direction * 118f, -8f + bob * 0.5f);
                default: //巡航
                    return new Vector2(direction * 100f, bob);
            }
        }

        /// <summary>
        /// P2 六手偏移(§4.2/§4.5):巡航 = 左右各三的编队常量表(上两只在举升位);
        /// 举升按招式收拢到各自爆弹/法球挂点;持镰双镰位;刺索按层散开。
        /// </summary>
        private Vector2 RoleOffsetP2(byte role, VoidPope pope, float bob)
        {
            switch (role)
            {
                case 1: //举升:P2-2s 三弹位(上/左/右)、P2-2ss 双弹位(左上/右上)、P2-4s 法球位
                    if (pope.State == VoidPope.PopeState.P2TripleBomb)
                    {
                        return layer switch
                        {
                            2 => new Vector2(direction * 36f, -146f),
                            1 => new Vector2(direction * 196f, -84f),
                            _ => new Vector2(direction * 164f, -58f),
                        };
                    }
                    if (pope.State == VoidPope.PopeState.P2TwinScythe)
                    {
                        return new Vector2(direction * 88f, -146f);
                    }
                    //P2-2ss 上四手凝 2 弹(弹位 ±96,-128)
                    return layer == 2 ? new Vector2(direction * 76f, -142f) : new Vector2(direction * 118f, -104f);
                case 2: //持镰:双镰位(±55,-80)两侧合握
                    return layer == 0 ? new Vector2(direction * 40f, -58f) : new Vector2(direction * 72f, -96f);
                case 3: //提灯:下层两侧平举
                    return new Vector2(direction * 150f, -18f + bob * 0.4f);
                case 4: //刺索:六手按层散开前压
                    return new Vector2(direction * (96f + layer * 34f), -6f - layer * 38f + bob * 0.5f);
                default: //巡航编队(左右各三)
                    return layer switch
                    {
                        2 => new Vector2(direction * 66f, -124f + bob * 0.4f),
                        1 => new Vector2(direction * 162f, -42f + bob * 0.6f),
                        _ => new Vector2(direction * 120f, 30f + bob),
                    };
            }
        }

        /// <summary>
        /// P3 巨手偏移(§4.3:两侧远位悬浮待命的招式执行者):
        /// 举升 = 终曲巨弹托位(弹吸附 ±262,-96);拽体横扫 = 扑击前倾 + 按
        /// <see cref="VoidPope.SweepBeats"/> 同表的大幅摆越(左右交替可背);缚身 = 垂落 + 末段震颤。
        /// </summary>
        private Vector2 RoleOffsetP3(byte role, VoidPope pope, float bob)
        {
            switch (role)
            {
                case 1: //终曲举弹:托在巨弹下方
                    return new Vector2(direction * 262f, -14f + bob * 0.3f);
                case 5: //拽体/横扫(P3-3)
                    return SweepOffset(pope, bob);
                case 6: //缚身垂落(P3-6):挣脱前 40t 震颤
                    {
                        Vector2 droop = new Vector2(direction * 236f, 70f + bob * 0.35f);
                        if (pope.boundTimer > 0 && pope.boundTimer < 40)
                        {
                            droop += new Vector2((float)Math.Sin(bobCounter * 22f + direction) * 6f, (float)Math.Sin(bobCounter * 19f) * 5f);
                        }
                        return droop;
                    }
                default: //远位悬浮待命(§4.3)
                    return new Vector2(direction * 315f, -46f + bob);
            }
        }

        /// <summary>
        /// P3-3 横扫编排(双端由教皇同步 attackTimer 同表推导):
        /// 扑击段双手前压拽体;各扫拍前 20t 该侧手高举后拉(前摇可读),
        /// 拍落 12t 内大幅横越到对侧(poly 缓出,一次爆发),24t 内收回。
        /// </summary>
        private Vector2 SweepOffset(VoidPope pope, float bob)
        {
            int t = pope.attackTimer;
            Vector2 drag = new Vector2(direction * 178f, 6f + bob * 0.4f);
            for (int i = 0; i < VoidPope.SweepBeats.Length; i++)
            {
                int beat = VoidPope.SweepBeats[i];
                int side = i % 2 == 0 ? -1 : 1;
                if (side != direction)
                {
                    continue;
                }
                if (t >= beat - 20 && t < beat)
                {
                    //前摇:高举后拉
                    float p = (t - (beat - 20)) / 20f;
                    return Vector2.Lerp(drag, new Vector2(direction * 336f, -96f), 1f - (1f - p) * (1f - p));
                }
                if (t >= beat && t < beat + 12)
                {
                    //横越:12t 内扫到对侧(poly(5) 缓出)
                    float p = (t - beat) / 12f;
                    float ease = 1f - (float)Math.Pow(1f - p, 5);
                    return Vector2.Lerp(new Vector2(direction * 336f, -96f), new Vector2(-direction * 96f, 26f), ease);
                }
                if (t >= beat + 12 && t < beat + 34)
                {
                    //收回
                    float p = (t - beat - 12) / 22f;
                    return Vector2.Lerp(new Vector2(-direction * 96f, 26f), drag, p * p);
                }
            }
            return drag;
        }

        public override void AI()
        {
            if (counter1 > 0)
            {
                counter1--;
                return;
            }
            NPC owner = ((int)NPC.ai[0]).ToNPC();
            if (!owner.active || owner.ModNPC is not VoidPope)
            {
                NPC.active = false;
                NPC.netUpdate = true;
                return;
            }
            VoidPope pope = (VoidPope)owner.ModNPC;
            NPC.realLife = owner.whoAmI;
            NPC.target = owner.target;
            NPC.damage = owner.damage; //瞬移/换阶/遁入期教皇接触归零,手自动跟随
            NPC.scale = owner.scale;
            bobCounter += 0.05f;
            spawnFade = Math.Min(spawnFade + 1f / 30f, 1f);

            //———死亡演出坠落(§4.4:双巨手逐个坠落碎裂;双端凭教皇同步状态同拍自演,服务端限时收尸)———
            if (pope.State == VoidPope.PopeState.P3Death)
            {
                int fallBeat = direction < 0 ? VoidPope.DeathHandLBeat : VoidPope.DeathHandRBeat;
                int t = pope.attackTimer;
                if (t >= fallBeat)
                {
                    NPC.velocity.X *= 0.99f;
                    NPC.velocity.Y += 0.35f;
                    NPC.rotation += 0.06f * direction;
                    if (t == fallBeat && !Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.NPCHit2 with { Volume = 1f, Pitch = -0.6f }, NPC.Center);
                    }
                    //落速 48t 后碎裂:客户端玻璃尘,服务端收尸
                    if (t == fallBeat + 48 && !Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.Shatter with { Volume = 0.8f, Pitch = -0.3f }, NPC.Center);
                        for (int d = 0; d < 14; d++)
                        {
                            Dust.NewDust(NPC.Center + CEUtils.randomPointInCircle(40f), 1, 1,
                                Terraria.ModLoader.ModContent.DustType<Dusts.GlassBreak>());
                        }
                    }
                    if (t >= fallBeat + 50 && Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        NPC.active = false;
                        NPC.netUpdate = true;
                        if (Main.netMode == NetmodeID.Server)
                        {
                            NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                        }
                    }
                    oldPos.Clear();
                    oldRots.Clear();
                    return;
                }
            }

            //———P2→P3 换阶:多余四手(层 >0)渐隐收回,110t 拍教皇服务端 despawn(§4.3)———
            if (layer > 0 && pope.phase == 2 && pope.transitionTimer >= 90)
            {
                NPC.velocity *= 0.9f;
                NPC.Center = Vector2.Lerp(NPC.Center, owner.Center, 0.08f); //向躯干收拢再消失
                oldPos.Clear();
                oldRots.Clear();
                return;
            }

            //role 双端各自从教皇同步态拉取(ai[2] 存储,原生同步兜底)
            handRole = pope.CurrentHandRole(direction, layer);

            Vector2 targetPos = owner.Center + RoleOffset(handRole, pope) * NPC.scale;

            //刺索姿态:向目标侧前倾
            if (handRole == 4 && NPC.HasValidTarget)
            {
                targetPos += (Main.player[NPC.target].Center - owner.Center).SafeNormalize(Vector2.Zero) * 24f;
            }

            //抓握定身:玩家钉在爪尖,8t 后向教皇方向抛出(§4.0;在受击者本机推进)
            if (handPlayerTime > 0 && handPlayer != null)
            {
                handPlayer.Center = NPC.Center + NPC.rotation.ToRotationVector2() * 86 * NPC.scale;
                handPlayer.velocity *= 0;
                handPlayerTime--;
                if (handPlayerTime == 0)
                {
                    handPlayer.velocity = (owner.Center - NPC.Center).SafeNormalize(Vector2.Zero) * 20f;
                }
            }

            NPC.velocity *= 0.9f;
            NPC.Center += (targetPos - NPC.Center) * 0.24f;

            //教皇瞬移/远距校正:直接快照并清拖尾(role 切换后不残留错误条带)
            if (CEUtils.getDistance(targetPos, NPC.Center) > 600)
            {
                NPC.Center = targetPos;
                oldPos.Clear();
                oldRots.Clear();
            }

            //朝向:刺索姿态瞄玩家,其余背离教皇(举升/持镰自然向上)
            float wantRot;
            if (handRole == 4 && NPC.HasValidTarget)
            {
                wantRot = (Main.player[NPC.target].Center - NPC.Center).ToRotation();
            }
            else
            {
                wantRot = (NPC.Center - owner.Center).ToRotation();
            }
            NPC.rotation = Utils.AngleLerp(NPC.rotation, wantRot, 0.25f);

            oldPos.Add(NPC.Center);
            oldRots.Add(NPC.rotation);
            if (oldPos.Count > 24)
            {
                oldPos.RemoveAt(0);
                oldRots.RemoveAt(0);
            }
        }

        public float trailOffset = 0;

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return false;
            }
            trailOffset += 0.06f;
            SpriteBatch sb = Main.spriteBatch;
            GraphicsDevice gd = Main.graphics.GraphicsDevice;
            //瞬移/换阶/遁入渐隐与教皇同步,换阶新增手叠自身渐显
            float alpha = spawnFade;
            VoidPope pope = ((int)NPC.ai[0]).ToNPC().ModNPC as VoidPope;
            if (pope != null)
            {
                alpha *= pope.BodyAlpha;
                //P2→P3 换阶:多余四手渐隐收回(§4.3,90~110t 内淡出,110t 服务端 despawn)
                if (layer > 0 && pope.phase == 2 && pope.transitionTimer >= 90)
                {
                    alpha *= MathHelper.Clamp(1f - (pope.transitionTimer - 90) / 20f, 0f, 1f);
                }
            }

            //———顶点条带拖尾(§4.0 保留):紫底 + 白芯双层———
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            for (int layer = 0; layer < 2; layer++)
            {
                Color baseColor = (layer == 0 ? Color.Purple : Color.White) * alpha;
                List<ColoredVertex> ve = new List<ColoredVertex>();
                for (int i = 0; i < oldRots.Count; i++)
                {
                    float prog = (float)i / oldRots.Count;
                    Color b = Color.Lerp(baseColor * 0.01f, baseColor, prog);
                    ve.Add(new ColoredVertex(oldPos[i] - screenPos + oldRots[i].ToRotationVector2() * (16 + 80 * NPC.scale * (1 - prog) * 0.5f),
                          new Vector3(prog + trailOffset, 1, 1), b));
                    ve.Add(new ColoredVertex(oldPos[i] - screenPos + oldRots[i].ToRotationVector2() * (16 + 80 * NPC.scale - 80 * NPC.scale * (1 - prog) * 0.5f),
                          new Vector3(prog + trailOffset, 0, 1), b));
                }
                if (ve.Count >= 3)
                {
                    gd.Textures[0] = layer == 0 ? CEExtraAssets.white : CEExtraAssets.SwordSlashTexture;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
                }
            }

            //———提灯(role 3,§4.1 P1-6 程序化:光球灯芯 + 短垂链 + 辉光,加法批次内直接画)———
            if (handRole == 3 && alpha > 0.05f)
            {
                Vector2 lanternPos = NPC.Center + new Vector2(0, 40f);
                Texture2D pixel = TextureAssets.MagicPixel.Value;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 lp = NPC.Center + new Vector2((float)Math.Sin(bobCounter * 2f + i) * 2f, 10f + i * 10f);
                    sb.Draw(pixel, lp - screenPos, new Rectangle(0, 0, 1, 1), new Color(150, 90, 230) * (0.8f * alpha),
                        0, new Vector2(0.5f), new Vector2(3f, 8f), SpriteEffects.None, 0);
                }
                Texture2D glow = glowTex.Value;
                float pulse = 1f + 0.12f * (float)Math.Sin(bobCounter * 3f);
                sb.Draw(glow, lanternPos - screenPos, null, new Color(190, 110, 255) * (0.9f * alpha), 0, glow.Size() / 2, 1.1f * pulse, SpriteEffects.None, 0);
                sb.Draw(glow, lanternPos - screenPos, null, Color.White * (0.5f * alpha), 0, glow.Size() / 2, 0.55f * pulse, SpriteEffects.None, 0);
                Texture2D ring = CEExtraAssets.BloomRing;
                sb.Draw(ring, lanternPos - screenPos, null, new Color(170, 90, 255) * (0.45f * alpha), 0, ring.Size() / 2, 0.4f * pulse, SpriteEffects.None, 0);
            }
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            bool p2 = pope != null && pope.phase >= 2;
            bool p3 = pope != null && pope.HandsP3;

            //———armP2 连接件(§4.2/§4.5,仅 P2;P3 巨手独立浮游停画)———
            if (p2 && !p3 && pope.NPC.active)
            {
                Texture2D arm = armTex.Value;
                Vector2 shoulder = pope.NPC.Center + new Vector2(direction * 44f, -26f - layer * 26f) * NPC.scale;
                Vector2 toHand = NPC.Center - shoulder;
                float armRot = toHand.ToRotation();
                float stretch = Math.Max(0.5f, toHand.Length() / arm.Width);
                SpriteEffects armFx = direction < 0 ? SpriteEffects.FlipVertically : SpriteEffects.None;
                Main.EntitySpriteDraw(arm, shoulder - screenPos, null, drawColor * (alpha * 0.95f), armRot,
                    new Vector2(0f, arm.Height / 2f), new Vector2(stretch, NPC.scale), armFx);
            }

            //———手本体:P1 双手各用一款,P2 两款按 (层+方位) 交错(§6.2);P3 巨手同款左右镜像;源图指尖朝左,水平镜像后沿 rotation 指向———
            Texture2D tex;
            if (p3)
            {
                tex = handP3Tex.Value;
            }
            else if (p2)
            {
                tex = (layer + (direction > 0 ? 1 : 0)) % 2 == 0 ? handP2aTex.Value : handP2bTex.Value;
            }
            else
            {
                tex = direction > 0 ? hand1Tex.Value : hand2Tex.Value;
            }
            SpriteEffects fx = SpriteEffects.FlipHorizontally;
            if (direction < 0)
            {
                fx |= SpriteEffects.FlipVertically;
            }
            Main.EntitySpriteDraw(tex, NPC.Center - screenPos, null, drawColor * alpha, NPC.rotation, tex.Size() / 2, NPC.scale, fx);
            return false;
        }
    }
}
