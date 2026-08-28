using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.FriendFinderNPC;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空教皇(void-invasion.md §4.0~§4.4,M8 = P1 + P2 + P3 领域生存战 + 死亡演出完整版):月后 21.5 档正式 Boss。
    /// 状态机 = phase + attackID + attackTimer;选招走 seed 盐化确定性随机(服务端定夺经 attackID 同步,
    /// 盐 = 选招计数,中途加入的客户端不会错位),同招不连发;P1 招 5/6 分别 HP&lt;85%/&lt;75% 入池,
    /// P2 招 2ss/6s 分别 &lt;55%/&lt;50%(且 P2-6 至少放过一次)入池。
    /// 瞬移语言(§4.0 全招共用):目的地法阵闪光 25t 预告 → 本体渐隐 12t(PRT_Void 外散)
    /// → 目的地浮现 12t(粒子内聚);瞬移期间本体一直可打,接触伤害归零。
    /// 换阶演出(transitionTimer 承载,按 phase 分流):65% P1→P2(§4.2,3s);40% P2→P3(§4.3,3.5s):
    /// 清弹幕 → 瞬移屏心 → 二阶段躯体过曝碎裂 → bodyP3 自碎片拼合 → 侧结构飞入 → 多余四手收回消失
    /// → 双巨手远位展开 → 领域 0→1150 缓张 60t → phase=3 落同步。
    /// P3(§4.3):本体定于领域中心,DamageReduction=0.99 + 每秒自损 1% 最大生命(双端同式推进,服务端 30t 节流同步),
    /// 领域半径随血量 1150→575 线性收缩(由 life 推导,domainRadiusFactor 只承载展开进度);
    /// 玩家触边环绕 = 本机客户端处理自己玩家(<see cref="HandleDomainWrap"/>,EModPlayer.PostUpdate 挂点);
    /// 六招:安全区轮转/闪电球(<see cref="LightningOrb"/>)/冲斩扇射/反射激光(<see cref="Projectiles.VoidInvasion.ReflectLaser"/>)
    /// /双爆弹终曲(&lt;20% 入池)/铁索缚身(每第 4 招强制,boundTimer 窗口 DR 归零);
    /// 死亡演出(§4.4,life&lt;=1 触发,自损干死与玩家打死同路):领域龟裂 → 部件逐个坠落
    /// → bodyP3 像素扫描消散(AWDeath 技法)→ 白闪 → 领域爆碎 → StrikeInstantKill。
    /// 手 = <see cref="VoidPopeHand"/>(ai[2] = handRole,ai[3] = 编队层,由本类逐帧派发);
    /// 铁索模板 = <see cref="Projectiles.VoidInvasion.PopeChain"/>(P3-6 反向缚身样式 3);魔盘 = <see cref="DimensionDisc"/>
    /// (discState/discPos 同步字段驱动,本类是权威)。
    /// </summary>
    [AutoloadBossHead]
    public class VoidPope : ModNPC
    {
        public enum PopeState : byte
        {
            Hover,        //招间呼吸拍(P1/P2 共用)
            PortalWorms,  //P1-1 门中蠕虫
            DeathBombCast,//P1-2 死亡爆弹
            ChainHunt,    //P1-3 铁索追猎
            ScytheDash,   //P1-4 魔镰冲斩
            Trinity,      //P1-5 三位一体(<85%)
            SoulLanterns, //P1-6 冥魂灯收魂(<75%)
            P2PortalWorms,//P2-1s 六门蠕虫(蠕虫脱门追踪)
            P2TripleBomb, //P2-2s 三重爆弹
            P2LanternBomb,//P2-2ss 灯弹复合(<55%)
            P2ChainStorm, //P2-3s 万索追猎(六索合围)
            P2TwinScythe, //P2-4s 双镰连突 + 法球魔焰弹
            P2DiscLaser,  //P2-5 维度魔盘·巨激光
            P2Omniscient, //P2-6 全知之眼
            P2TrinityEye, //P2-6s 分身之眼(<50% 且 P2-6 放过)
            P2ClawSwarm,  //P2-7 恶念之爪群
            P2DiscDive,   //P2-8 掷盘遁入(破门衔接镰/索)
            P3SafeZones,  //P3-1 安全区轮转
            P3LightningOrbs,//P3-2 闪电球
            P3PounceSweep,//P3-3 冲斩扇射(P3 唯一位移)
            P3ReflectLaser,//P3-4 反射激光
            P3FinalBombs, //P3-5 双爆弹终曲(<20%)
            P3ChainBind,  //P3-6 铁索缚身(每第 4 招强制)
            P3Death,      //死亡演出(§4.4,life<=1 触发)
        }

        //———瞬移语言节拍(§4.0)———
        public const int TeleWarn = 25;
        public const int TeleFadeOut = 12;
        public const int TeleFadeIn = 12;
        public const int TeleTotal = TeleWarn + TeleFadeOut + TeleFadeIn; //49

        //———呼吸拍(§4.1:招间 0.8~1.2s)———
        private const int HoverMin = 48;
        private const int HoverSpan = 25;

        //———P1-1 门中蠕虫:出门拍 → 全程约 7s———
        private const int WormsSpawnBeat = TeleTotal + 7;   //56
        private const int WormsPortalLife = 320;
        private const int WormsTotal = 400;

        //———P1-2 死亡爆弹:爆弹自管充能/静默/投掷,教皇掷出后收招———
        private const int BombBeat = TeleTotal + 7;                          //56
        private const int BombCastTotal = BombBeat + DeathBomb.ThrowTick + 40; //256

        //———P1-3 铁索追猎:6s 追逐,第 3 刺后提速 45t→38t———
        private static readonly int[] HuntStabBeats = { 45, 90, 135, 173, 211, 249 };
        private const int HuntTotal = 375;
        private const float HuntSpeed = 5.5f;

        //———P1-4 魔镰冲斩———
        private const int ScytheCondenseEnd = TeleTotal + 40; //89
        private const int ScytheDashLaunch = ScytheCondenseEnd;
        private const int ScytheSlash1 = 97;
        private const int ScytheSlash2 = 111;
        private const int ScytheBrake = 121;
        private const int ScytheRelaunch = 135;
        private const int ScytheSlash3 = 146;
        private const int ScytheTotal = 195;
        private const float ScytheDashSpeed = 26f;

        //———P1-5 三位一体:公转 r=560 @0.03rad/t,每 120t 换位,8s 散去———
        private const int TrinityFadeIn = 20;
        private const float TrinityRadius = 560f;
        private const float TrinityOrbitSpeed = 0.03f;
        private static readonly int[] TrinitySingleStabs = { 79, 139, 199, 259 };
        private static readonly int[] TrinityDoubleStabs = { 319, 379, 439 };
        private static readonly int[] TrinitySwaps = { 169, 289, 409 };
        private const int TrinityTotal = 529;

        //———P1-6 冥魂灯收魂:收魂 120t(定身+受伤 ×1.15)→ 5s 阵雨(每 40t 一阵)———
        private const int HarvestEnd = TeleTotal + 120; //169
        private const int RainGap = 40;
        private const int RainWaves = 7;
        private const int LanternTotal = 490;

        //———换阶演出(§4.2,65% 触发,3s 无敌;transitionTimer 承载,双端各自推进)———
        //演出二迭:瞬移落定(49)后插 13t 悬停吸气拍(MOTION 碎裂前的静默),爆散后移 62,后续拍点顺延
        private const int TransTotal = 180;
        private const int TransFeatherBurst = 62;   //羽影爆散拍(悬停吸气拍之后)
        private const int TransBodyFadeEnd = 92;    //躯体交叉渐变完成
        private const int TransWingEnd = 142;       //二阶段翼弹性展开完成
        private const int TransHandSpawn = 96;      //服务端新增 4 手
        private const int TransRoar = 150;          //咆哮 + 无伤冲击环
        private const int FeatherCount = 20;

        //———P2-1s 六门蠕虫:6 门,蠕虫喷火 2 波后脱门追踪(脱门段在 VoidWormlet 里)———
        private const int P2WormsSpawnBeat = TeleTotal + 7; //56
        private const int P2WormsEmergeLife = 190;          //探身段时长(喷完两波即脱门)
        private const int P2WormsTotal = 360;

        //———P2-2s 三重爆弹:3 弹同凝(充能 120t)→ 30t 间隔依次掷出(节拍在 DeathBomb 模式 1~3)———
        private const int P2BombBeat = TeleTotal + 7; //56
        private const int P2BombTotal = P2BombBeat + DeathBomb.ChargeTimeP2 + DeathBomb.ShrinkTime + 60 + 40; //286

        //———P2-2ss 灯弹复合(<55%):收魂缩短 80t,结束瞬间阵雨与 2 爆弹投掷同时进行———
        private const int P2HarvestEnd = TeleTotal + 80;  //129(爆弹 56 生成 + 63 充能 + 10 骤缩恰在此掷出)
        private const int P2ComboBombBeat = TeleTotal + 7; //56
        private const int P2LanternTotal = P2HarvestEnd + RainWaves * RainGap + 40; //449

        //———P2-3s 万索追猎:左右交替齐射(每次 2 条)×3 波 → 第 4 波六索合围(留一缺口)———
        private static readonly int[] StormWaveBeats = { 45, 105, 165 };
        private const int StormSiegeBeat = 225;
        private const float StormSiegeRadius = 560f;
        private const float StormSiegeLength = 640f;
        private const int P2StormTotal = 360;

        //———P2-4s 双镰连突:3 次冲刺穿斩(刹车间隔 25t 重瞄)+ 上方法球吐魔焰弹 → 环爆 16 枚———
        private const int TSCondenseEnd = TeleTotal + 40;  //89
        private static readonly int[] TSLaunches = { 89, 146, 203 };
        private static readonly int[] TSSlashes = { 97, 111, 154, 168, 211, 225 }; //每冲 2 段旋斩
        private static readonly int[] TSBrakes = { 121, 178, 235 };
        private const int TSOrbBurst = 245;               //第 3 刺结束法球爆裂
        private const int P2ScytheTotal = 285;
        private static readonly Vector2 TSOrbOffset = new Vector2(88f, -162f); //法球位(±X)

        //———P2-5 维度魔盘·巨激光:升起 60t → (跟踪 40t → 锁定 20t → 射击 45t)×2 → 退场———
        private const int DiscRiseBeat = TeleTotal + 1;   //50
        private const int DiscAim1 = 110;
        private const int DiscLock1 = 150;
        private const int DiscFire1 = 170;
        private const int DiscAim2 = 215;
        private const int DiscLock2 = 255;
        private const int DiscFire2 = 275;
        private const int DiscRetire = 320;
        private const int P2DiscLaserTotal = 345;

        //———P2-6 全知之眼:3 眼 120° 均布,浮现 20t + 吐弹 240t;教皇先走,眼再公转 5s(弹幕自治)———
        private const int EyeSpawnBeat = TeleTotal + 7; //56
        private const int P2OmniTotal = EyeSpawnBeat + Projectiles.VoidInvasion.OmniscientEye.AppearTime
            + Projectiles.VoidInvasion.OmniscientEye.SpitTime + 20; //336

        //———P2-7 恶念之爪群:魔盘悬顶 → 3 波 ×7 爪向上扇形抛出,波间错拍 15t———
        private static readonly int[] ClawWaveBeats = { 80, 95, 110 };
        private const int ClawPerWave = 7;
        private const int ClawDiscRetire = 170;
        private const int P2ClawTotal = 200;

        //———P2-8 掷盘遁入:掷盘 → 渐隐 1s(判定关)→ 破门衔接镰/索释放段(跳过前摇)———
        private const int DiveThrowBeat = 30;
        private const int DiveVanishEnd = 90;   //渐隐完成(30~90 共 1s)
        private const int DiveReappear = 120;   //破门拍(固定,无死锁)
        private const int TwinScytheSkipTo = TSCondenseEnd - 1; //衔接镰:下一帧恰落点火拍(89)
        private const int ChainStormSkipTo = 35;                //衔接索:跳过起手,首波 10t 后即来

        //———P2→P3 换阶演出(§4.3,40% 触发,3.5s 无敌;transitionTimer 承载,按 phase 分流)———
        private const int TransP3Total = 210;
        private const int TransP3Shatter = 50;      //二阶段躯体过曝碎裂(白闪拍)
        private const int TransP3BodyEnd = 110;     //bodyP3 渐显拼合完成
        private const int TransP3HandOff = 90;      //多余四手渐隐起拍(本地视觉)
        internal const int TransP3HandGone = 110;   //服务端 despawn 多余四手 / 双巨手换装拍(手侧引用)
        private const int TransP3ShoulderEnd = 140; //侧结构飞入完成
        private const int TransP3DomainStart = 150; //领域展开起拍(60t 缓张至 210)

        //———领域(§4.3)———
        public const float DomainRMax = 1150f;
        public const float DomainRMin = 575f;
        private const int DomainExpand = 60;
        private const int P3HitboxSize = 250;       //P3 结晶主体判定框(双端同式改写)
        public const float WrapInset = 90f;         //环绕传回后离边内缩量

        //———P3-1 安全区轮转:5 轮 ×(预警 60 + 光涌 25 + 换位 10)———
        private const int SafeIntro = 20;
        private const int SafeRoundLen = 95;
        private const int SafeWarnLen = 60;
        private const int SafeBurstLen = 25;
        private const int SafeRounds = 5;
        private const int P3SafeTotal = SafeIntro + SafeRounds * SafeRoundLen + 30; //525

        //———P3-2 闪电球:5 颗扇形升起(球自治:每 50t 放电,12s 自爆;打球即减压)———
        private const int OrbSpawnBeat = 30;
        private const int P3OrbTotal = 420;

        //———P3-3 冲斩扇射:反向蓄势 → 扑击 8px/t → 急停 → 左右交替横扫 ×4(每扫 20t 前摇 + 7 枚扇形魔焰弹)→ 回中心———
        private const int PounceWindup = 34;
        private const int PounceLaunch = PounceWindup;
        private const int PounceEnd = PounceLaunch + 78;    //112
        private const int PounceBrakeEnd = PounceEnd + 14;  //126
        internal static readonly int[] SweepBeats = { 146, 178, 210, 242 }; //手侧编排引用(前摇 20t 在拍前)
        private const int SweepReturn = 262;
        private const int P3PounceTotal = 330;
        private const float PounceSpeed = 8f;

        //———P3-4 反射激光:胸口细警示线 30t → 四段反射(几何在 ReflectLaser 内一次算完)———
        private const int ReflectFireBeat = 40;
        private const int P3ReflectTotal = ReflectFireBeat + Projectiles.VoidInvasion.ReflectLaser.TotalLife + 50; //242

        //———P3-5 双爆弹终曲(<20%):双巨手各凝 110px 巨弹(模式 6/7,弹自管激光轮/引爆/追踪弹)———
        private const int FinalBombBeat = 30;
        private const int P3FinalTotal = FinalBombBeat + DeathBomb.ChargeTimeP3 + DeathBomb.ShrinkTime + DeathBomb.ExplodeExpand + 90; //330

        //———P3-6 铁索缚身(每第 4 招强制):八方铁索 2s 内依次钉入 → 缚身 4s DR 归零 → 挣脱———
        private const int BindChainGap = 13;
        private const int BindChains = 8;
        private const int BindFirstPin = 10;
        private const int BindPinned = 120;
        private const int BindDuration = 240;
        private const int BindBreakBeat = BindPinned + BindDuration; //360
        private const int P3BindTotal = BindBreakBeat + 40;          //400

        //———死亡演出(§4.4,5s;镜像 AbyssalWraith deathAnm 骨架)———
        private const int DeathCrackStart = 10;
        /// <summary>死亡演出双巨手坠落拍(手侧凭教皇状态同拍自演)</summary>
        internal const int DeathHandLBeat = 120;
        internal const int DeathHandRBeat = 160;
        private static readonly int[] DeathPieceBeats = { 40, 80, DeathHandLBeat, DeathHandRBeat }; //肩L/肩R/手L/手R(间隔 40t)
        private const int DeathScanStart = 160;
        private const int DeathScanLen = 100;
        private const int DeathWhiteFlash = 262;
        private const int DeathBurst = 264;
        private const int P3DeathTotal = 300;

        //———同步状态(§4.5,全进 SendExtraAI,双端同序;M7 尾插 discState+discPos)———
        public int seed = -1;
        public byte phase = 1;
        public byte attackID = (byte)PopeState.Hover;
        public int attackTimer = 0;
        /// <summary>换阶演出计时(§4.2:>0 = 演出中,双端各自逐帧 ++,180 收束落 phase=2)</summary>
        public int transitionTimer = 0;
        /// <summary>三位一体换位计数(客户端凭它与位置跳变各自演出白闪)</summary>
        public byte cloneSwapIndex = 0;
        /// <summary>瞬移目的地(服务端在招式首帧定夺后广播,预告法阵画在这里)</summary>
        public Vector2 teleTarget = Vector2.Zero;
        /// <summary>魔盘状态(§4.5 预留语义):0 无盘/1 升起/2 跟踪/3 锁定/4 射击/5 退场/6 悬顶/7 掷出/8 悬停/9 破门</summary>
        public byte discState = 0;
        /// <summary>魔盘位置镜像(服务端由 <see cref="DimensionDisc"/> 每帧写回;P2-8 破门点凭它)</summary>
        public Vector2 discPos = Vector2.Zero;
        /// <summary>领域展开进度 0→1(§4.3/§4.5,M8 尾插):换阶末 60t 缓张;半径本体由 life 推导,本字段只承载展开/收场</summary>
        public float domainRadiusFactor = 0f;
        /// <summary>铁索缚身剩余窗口(§4.3 P3-6,M8 尾插):>0 时 DamageReduction 归零(DPS 阀门);双端各自递减,包到达校正</summary>
        public int boundTimer = 0;

        //———服务端本地———
        private int attackCount = 0;
        private byte lastAttack = 255;
        private int hoverLen = HoverMin;
        public bool spawnHands = true;
        /// <summary>P2-6 是否已放过(§4.2:P2-6s 入池门槛;选招仅服务端,不需同步)</summary>
        private bool usedOmniscient = false;
        /// <summary>P3 选招序号(§4.3:每第 4 招强制铁索缚身;选招仅服务端)</summary>
        private int p3AttackIndex = 0;

        //———双端各自推进的视觉/杂项———
        private float flapCounter = 0;
        private Vector2 prevCenter = Vector2.Zero;
        private readonly List<Vector2> odp = new List<Vector2>();
        /// <summary>P3 自损的小数累计(双端同式推进作预测,服务端包到达校正 life)</summary>
        private float selfBurnAcc = 0f;
        /// <summary>领域锚点(P3 圆心):教皇静止时跟随本体缓校正,扑击(P3-3)期间冻结</summary>
        private Vector2 domainAnchor = Vector2.Zero;
        private bool anchorInit = false;
        /// <summary>P3-1 当前轮安全圈圆心(双端在同一拍由 seed+cloneSwapIndex+轮次推导;零 = 未定)</summary>
        private Vector2 safeZonePos = Vector2.Zero;
        /// <summary>死亡演出像素扫描进度(§4.4,双端各自由 attackTimer 推导)</summary>
        private float deathPer = 0f;
        private Color[] p3PixelData = null;
        /// <summary>本机玩家上一帧是否在领域内(环绕判定;只写本机)</summary>
        private static bool localWasInside = false;

        //———换阶羽影(§4.2:一阶段翼爆散 20 片,纯客户端视觉)———
        private struct FeatherFx
        {
            public Vector2 pos;
            public Vector2 vel;
            public float rot;
            public float rotVel;
            public float life;
        }
        private readonly List<FeatherFx> feathers = new List<FeatherFx>();

        //———死亡演出坠落部件(§4.4:侧结构逐个坠落碎裂;纯客户端视觉)———
        private struct FallingPiece
        {
            public int texId;     //0 = shoulderL / 1 = shoulderR
            public Vector2 pos;
            public Vector2 vel;
            public float rot;
            public float rotVel;
            public float life;
        }
        private readonly List<FallingPiece> fallingPieces = new List<FallingPiece>();

        //———P2→P3 结晶碎片(演出二迭:碎裂真实飞出 → 悬停一拍 → 逆聚拼合;纯客户端视觉)———
        private struct CrystalShard
        {
            public Vector2 pos;
            public Vector2 vel;
            public float rot;
            public float rotVel;
            public float scale;
            public float delay;   //逆聚错拍(0~1)
        }
        private readonly List<CrystalShard> shards = new List<CrystalShard>();

        //———死亡演出加速节拍(§4.4 演出二迭:间隔递缩的报警音,PACING 死亡电影拍)———
        private static readonly int[] DeathBeepBeats = { 16, 44, 68, 88, 105, 119, 131, 141, 149, 155 };

        public PopeState State => (PopeState)attackID;

        //部件贴图只在绘制路径读取(专用服务器上恒为 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/wingP1")]
        private static Asset<Texture2D> wingTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/bodyP2")]
        private static Asset<Texture2D> bodyP2Tex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/wingP2")]
        private static Asset<Texture2D> wingP2Tex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/ribbon")]
        private static Asset<Texture2D> ribbonTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/PopeScythe")]
        private static Asset<Texture2D> scytheTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph")]
        private static Asset<Texture2D> glyphTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/AbyssalWraithProjs/VoidFeather")]
        private static Asset<Texture2D> featherTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/MagicEyeBolt")]
        private static Asset<Texture2D> eyeOrbTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/bodyP3")]
        private static Asset<Texture2D> bodyP3Tex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/shoulderL")]
        private static Asset<Texture2D> shoulderLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Pope/shoulderR")]
        private static Asset<Texture2D> shoulderRTex;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[NPC.type] = 1;
            NPCID.Sets.MustAlwaysDraw[NPC.type] = true;
            NPCID.Sets.BossBestiaryPriority.Add(Type);
            //M9:教皇正式入图鉴(原型期的 HideFromBestiary 解除,FlavorText 键 VoidPopeBestiary)
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new IBestiaryInfoElement[]
            {
                new FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidPopeBestiary")
            });
        }

        public override void SetDefaults()
        {
            //§4.0 数值档:HP 3,200,000 / 防御 100 / P1 接触 170;难度加伤走房屋标准写法
            NPC.boss = true;
            NPC.aiStyle = -1;
            NPC.width = 110;
            NPC.height = 110;
            NPC.damage = 170;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.defense = 100;
            NPC.lifeMax = 3200000;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCHit1;
            NPC.value = 200000f;
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.Entropy().VoidTouchDR = 0.9f;
            NPC.dontCountMe = true;
            if (!Main.dedServ)
            {
                Music = MusicLoader.GetMusicSlot(Mod, "Assets/Sounds/Music/RepBossTrack");
            }
        }

        //自有 Boss 惯例:公有 DR 字段 + ModifyIncomingHit 结算(§4.0 常态 0.1)
        public float DamageReduction = 0.1f;
        public override void ModifyIncomingHit(ref NPC.HitModifiers modifiers)
        {
            modifiers.FinalDamage *= 1f - DamageReduction;
            //收魂期明示的输出窗口(§4.1 P1-6 / §4.2 P2-2ss:定身且受伤 +15%,P2 沿用)
            bool harvesting = State == PopeState.SoulLanterns && attackTimer > TeleTotal && attackTimer < HarvestEnd;
            bool harvestingP2 = State == PopeState.P2LanternBomb && attackTimer > TeleTotal && attackTimer < P2HarvestEnd;
            if (harvesting || harvestingP2)
            {
                modifiers.FinalDamage *= 1.15f;
            }
        }

        public override void OnSpawn(IEntitySource source)
        {
            seed = Main.rand.Next(0, 10000);
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(seed);
            writer.Write(phase);
            writer.Write(attackID);
            writer.Write(attackTimer);
            writer.Write(transitionTimer);
            writer.Write(cloneSwapIndex);
            writer.WriteVector2(teleTarget);
            //M7 尾插(§4.5:续 M6 顺序,只追加不重排)
            writer.Write(discState);
            writer.WriteVector2(discPos);
            //M8 尾插(§4.5 预留:domainRadiusFactor / boundTimer)
            writer.Write(domainRadiusFactor);
            writer.Write(boundTimer);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            seed = reader.ReadInt32();
            phase = reader.ReadByte();
            attackID = reader.ReadByte();
            attackTimer = reader.ReadInt32();
            transitionTimer = reader.ReadInt32();
            cloneSwapIndex = reader.ReadByte();
            teleTarget = reader.ReadVector2();
            discState = reader.ReadByte();
            discPos = reader.ReadVector2();
            domainRadiusFactor = reader.ReadSingle();
            boundTimer = reader.ReadInt32();
        }

        public override void OnKill()
        {
            //死亡演出末尾 StrikeInstantKill 走到这里落旗标(§4.4);掉落表在 ModifyNPCLoot(M9)
            NPC.SetEventFlagCleared(ref EDownedBosses.downedVoidPope, -1);
        }

        public override void BossLoot(ref int potionType)
        {
            //月后 Boss 惯例:超级治疗药水(镜像 CruiserHead)
            potionType = ItemID.SuperHealingPotion;
        }

        /// <summary>
        /// 教皇掉落表(§5.3,M9):经典 = 五武器 25% 出一件 + 魂髓 15~20;专家 = 宝藏箱;
        /// 大师 = 圣物;纪念章首杀按人必掉、后续 25%;传颂之物首杀按人必掉。
        /// 规则注册在加载期,条件在掉落时求值;掉落先于 OnKill 落旗标(镜像 NihilityTwin 首杀传记时序)。
        /// </summary>
        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.BossBag(ModContent.ItemType<Items.VoidInvasion.VoidPopeBag>()));

            LeadingConditionRule normalOnly = new LeadingConditionRule(new Conditions.NotExpert());
            {
                //五武器 25% 掉一件(OneFromOptions,§5.3)
                normalOnly.OnSuccess(ItemDropRule.OneFromOptionsNotScalingWithLuck(4, 1,
                    ModContent.ItemType<Items.Weapons.VoidInvasion.FallenVoidCodex>(),
                    ModContent.ItemType<Items.Weapons.VoidInvasion.VoidGodScythe>(),
                    ModContent.ItemType<Items.Weapons.VoidInvasion.PrisonKnife>(),
                    ModContent.ItemType<Items.Weapons.VoidInvasion.OmniscientTetrahedron>(),
                    ModContent.ItemType<Items.Weapons.VoidInvasion.CurseRoar>()));
                normalOnly.OnSuccess(ItemDropRule.Common(ModContent.ItemType<Items.WraithSoulEssence>(), 1, 15, 20));
            }
            npcLoot.Add(normalOnly);

            //大师圣物(difficulty-map:原灾厄复仇/大师条件对齐原版大师惯例)
            npcLoot.Add(ItemDropRule.ByCondition(new Conditions.IsMasterMode(), ModContent.ItemType<Items.VoidInvasion.VoidPopeRelic>()));

            //纪念章:首杀按人必掉,之后 25%(§5.3)
            npcLoot.Add(new DropPerPlayerOnThePlayer(ModContent.ItemType<Items.VoidInvasion.PopeMedal>(), 1, 1, 1, new MedalFirstKill()));
            npcLoot.Add(ItemDropRule.ByCondition(new MedalRepeatKill(), ModContent.ItemType<Items.VoidInvasion.PopeMedal>(), 4));

            //传颂之物:首杀按人掉落(承接现役 Lore 惯例,镜像 NihilityTwinLore)
            npcLoot.Add(new DropPerPlayerOnThePlayer(ModContent.ItemType<Items.VoidInvasion.VoidPopeLore>(), 1, 1, 1, new MedalFirstKill()));
        }

        //首杀条件:downedVoidPope 未置位(掉落先于 OnKill 落旗标)
        private class MedalFirstKill : IItemDropRuleCondition, IProvideItemConditionDescription
        {
            public bool CanDrop(DropAttemptInfo info) => !EDownedBosses.downedVoidPope;
            public bool CanShowItemDropInUI() => true;
            public string GetConditionDescription() => null;
        }

        //复杀条件:已击败过教皇后 25% 档生效
        private class MedalRepeatKill : IItemDropRuleCondition, IProvideItemConditionDescription
        {
            public bool CanDrop(DropAttemptInfo info) => EDownedBosses.downedVoidPope;
            public bool CanShowItemDropInUI() => true;
            public string GetConditionDescription() => null;
        }

        /// <summary>
        /// 死亡边界守卫(镜像 AbyssalWraith.CheckDead):演出没走完一律钳回 1 血不放行,
        /// 自损干死与玩家打死都从这里(或 AI 的 life&lt;2 检测)汇入死亡演出。
        /// </summary>
        public override bool CheckDead()
        {
            if (State == PopeState.P3Death && attackTimer >= P3DeathTotal - 5)
            {
                return true;
            }
            NPC.life = 1;
            NPC.dontTakeDamage = true;
            NPC.active = true;
            if (Main.netMode != NetmodeID.MultiplayerClient && State != PopeState.P3Death)
            {
                EnterDeath();
            }
            NPC.netUpdate = true;
            if (NPC.netSpam >= 10)
                NPC.netSpam = 9;
            return false;
        }

        /// <summary>
        /// 进入死亡演出(服务端;客户端凭 attackID/phase 同步跟进):清敌对弹幕,状态机切 P3Death。
        /// phase 强制落 3:极端一击从 P1/P2 直接打穿时,死亡演出也要以 P3 结晶躯体呈现(§4.4 只有一套)。
        /// </summary>
        private void EnterDeath()
        {
            boundTimer = 0;
            discState = 0;
            phase = 3;
            transitionTimer = 0;
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if (p.hostile && !p.friendly)
                {
                    p.Kill();
                }
            }
            SwitchState(PopeState.P3Death);
        }

        /// <summary>seed 盐化确定性随机快照(§4.0 保留 seed 选招模式;按盐取新实例,中途加入不错位)。</summary>
        private UnifiedRandom Rng(int salt)
        {
            return new UnifiedRandom(seed * 131 + salt * 47 + 7);
        }

        /// <summary>当前是否处于瞬移三拍内(此间接触伤害归零,本体一直可打);换阶演出的开场瞬移也算。</summary>
        public bool InTeleport => transitionTimer > 0
            ? transitionTimer <= TeleTotal
            : AttackHasTeleport(State) && attackTimer <= TeleTotal;

        private static bool AttackHasTeleport(PopeState s)
        {
            return s == PopeState.PortalWorms || s == PopeState.DeathBombCast
                || s == PopeState.ScytheDash || s == PopeState.Trinity || s == PopeState.SoulLanterns
                || s == PopeState.P2PortalWorms || s == PopeState.P2TripleBomb || s == PopeState.P2LanternBomb
                || s == PopeState.P2TwinScythe || s == PopeState.P2DiscLaser || s == PopeState.P2Omniscient
                || s == PopeState.P2TrinityEye;
        }

        /// <summary>瞬移渐隐/浮现的本体透明度(双端由计时确定性推导;换阶演出用 transitionTimer)。</summary>
        public float TeleAlpha
        {
            get
            {
                if (!InTeleport)
                {
                    return 1f;
                }
                int t = transitionTimer > 0 ? transitionTimer : attackTimer;
                if (t <= TeleWarn)
                {
                    return 1f;
                }
                if (t <= TeleWarn + TeleFadeOut)
                {
                    return 1f - (t - TeleWarn) / (float)TeleFadeOut;
                }
                return (t - TeleWarn - TeleFadeOut) / (float)TeleFadeIn;
            }
        }

        /// <summary>
        /// P2-8 遁入的本体透明度(§4.2:掷盘后渐隐 1s 悬念拍,破门帧恢复;此间判定与受击全关)。
        /// </summary>
        public float DiveAlpha
        {
            get
            {
                if (State != PopeState.P2DiscDive)
                {
                    return 1f;
                }
                int t = attackTimer;
                if (t < DiveThrowBeat)
                {
                    return 1f;
                }
                if (t < DiveVanishEnd)
                {
                    return 1f - (t - DiveThrowBeat) / (float)(DiveVanishEnd - DiveThrowBeat);
                }
                return t < DiveReappear ? 0f : 1f;
            }
        }

        /// <summary>P2-8 遁入隐身窗口(判定关闭、不可打;破门帧必然恢复,无死锁)。</summary>
        public bool DiveHidden => State == PopeState.P2DiscDive && attackTimer >= DiveThrowBeat && attackTimer < DiveReappear;

        /// <summary>本体综合透明度(瞬移 × 遁入;手与拖尾同步引用)。</summary>
        public float BodyAlpha => TeleAlpha * DiveAlpha;

        //———————————————— 领域推导(§4.3,双端同式)————————————————

        /// <summary>领域圆心:P3 静止期跟随本体缓校正,扑击期间冻结(扑击后教皇回这里)。</summary>
        public Vector2 DomainAnchor => anchorInit ? domainAnchor : NPC.Center;

        /// <summary>
        /// 领域半径(§4.3):基础半径由 life 原生同步量线性推导(40% 血 1150 → 0 血 575),
        /// 乘 domainRadiusFactor 的缓出展开;死亡演出时 life 恒 1 → 基础半径自然冻在 575。
        /// </summary>
        public float DomainRadius
        {
            get
            {
                float lifeFrac = NPC.life / (float)NPC.lifeMax;
                float shrink = MathHelper.Clamp((0.4f - lifeFrac) / 0.4f, 0f, 1f);
                float baseR = MathHelper.Lerp(DomainRMax, DomainRMin, shrink);
                float f = MathHelper.Clamp(domainRadiusFactor, 0f, 1f);
                return baseR * (1f - (1f - f) * (1f - f));
            }
        }

        /// <summary>领域视觉是否该画(展开途中就画,玩家要看着墙长出来)。</summary>
        public bool DomainVisible => (phase >= 3 || (phase == 2 && transitionTimer > 0)) && domainRadiusFactor > 0.02f;

        /// <summary>双巨手形态是否已揭示(换阶 110t 起手侧换贴图换参数;手侧引用)。</summary>
        public bool HandsP3 => phase >= 3 || (phase == 2 && transitionTimer >= TransP3HandGone);

        /// <summary>
        /// 玩家环绕(§4.3 领域规则,EModPlayer.PostUpdate 逐帧挂点):
        /// 只在本机客户端处理自己的玩家(服务器 myPlayer=255 恒不匹配;单人 myPlayer 即本人),
        /// 旁观端靠原生位置同步;领域内触边 → 从圆心对称点传回,0.3s 相位残影,无伤害;
        /// 域外玩家不受影响(可自由入场);死亡演出期领域停用,环绕失效。
        /// </summary>
        public static void HandleDomainWrap(Player player)
        {
            if (player.whoAmI != Main.myPlayer || Main.dedServ)
            {
                return;
            }
            VoidPope pope = null;
            foreach (NPC n in Main.ActiveNPCs)
            {
                if (n.ModNPC is VoidPope vp && vp.phase >= 3)
                {
                    pope = vp;
                    break;
                }
            }
            if (pope == null || pope.State == PopeState.P3Death || pope.domainRadiusFactor < 0.99f
                || player.dead || !player.active)
            {
                localWasInside = false;
                return;
            }
            Vector2 anchor = pope.DomainAnchor;
            float radius = pope.DomainRadius;
            Vector2 offset = player.Center - anchor;
            float dist = offset.Length();
            if (dist < radius - 24f)
            {
                localWasInside = true;
                return;
            }
            //远离边界(重生点/回忆镜/死亡期 PostUpdate 停跑导致的旗标残留):视为已离场,不环绕
            if (dist > radius + 240f)
            {
                localWasInside = false;
                return;
            }
            if (!localWasInside)
            {
                return;
            }
            //触边:传回圆心对称点(留 WrapInset 内缩),速度保留(动量穿环,虫洞感)
            Vector2 dir = offset.SafeNormalize(Vector2.UnitX);
            Vector2 exitPos = player.Center;
            Vector2 enterPos = anchor - dir * (radius - WrapInset);
            player.Center = enterPos;
            //位置跳变清坠落起点,防环绕结算坠落伤害
            player.fallStart = (int)(player.position.Y / 16f);
            player.fallStart2 = player.fallStart;
            WrapEchoFx(exitPos, enterPos);
        }

        /// <summary>环绕相位残影(纯本机客户端演出:两端方向性冲环 + 闪光 + 沿弦相位粒子 + 音效)。</summary>
        private static void WrapEchoFx(Vector2 exitPos, Vector2 enterPos)
        {
            SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.85f, Pitch = 0.35f }, enterPos);
            float chordRot = (enterPos - exitPos).ToRotation();
            for (int e = 0; e < 2; e++)
            {
                Vector2 pos = e == 0 ? exitPos : enterPos;
                var flash = PRTLoader.NewParticle<PRT_Light>(pos, Vector2.Zero, new Color(190, 120, 255), 1.5f);
                flash.Configure(0.8f, lifetime: 18);
                //出口环被"吸扁"向弦方向,入口环垂直展开——读作"从这边被抽到那边"
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(pos, Vector2.Zero, new Color(190, 120, 255), 0.08f);
                ring.Configure(e == 0 ? new Vector2(1.9f, 0.5f) : new Vector2(0.5f, 1.9f), chordRot, 1.4f, 14);
                for (int i = 0; i < 10; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(pos,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1.5f, 6f), Color.White, 0.9f);
                    v.Opacity = Main.rand.Next(30, 80) * 0.01f;
                }
            }
            //沿弦相位粒子(0.3s 内淡去,读作"从这边穿到那边")
            for (int i = 0; i < 26; i++)
            {
                Vector2 pos = Vector2.Lerp(exitPos, enterPos, (i + Main.rand.NextFloat()) / 26f);
                var p = PRTLoader.NewParticle<PRT_Light>(pos, Vector2.Zero, new Color(150, 90, 240), 0.4f);
                p.Configure(0.86f, lifetime: 16);
            }
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return !InTeleport && transitionTimer <= 0 && !DiveHidden && State != PopeState.P3Death;
        }

        private void SwitchState(PopeState next, int startTimer = 0)
        {
            attackID = (byte)next;
            attackTimer = startTimer;
            teleTarget = Vector2.Zero;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
                if (NPC.netSpam >= 10)
                    NPC.netSpam = 9;
            }
        }

        /// <summary>
        /// 选下一招(仅服务端):seed+计数盐化随机,不连发同招。
        /// P1 招 5/6 按血线 &lt;85%/&lt;75% 入池(§4.1);
        /// P2 招 2ss &lt;55% 入池,6s &lt;50% 且 P2-6 至少放过一次后入池(§4.2);
        /// P3(§4.3):每第 4 招强制铁索缚身;终曲 &lt;20% 才入池;cloneSwapIndex 复用为
        /// P3 选招纪元盐(随选招包同步,安全圈位置双端凭它推导)。
        /// </summary>
        private void NextAttack()
        {
            List<PopeState> pool;
            if (phase >= 3)
            {
                p3AttackIndex++;
                cloneSwapIndex++;
                if (p3AttackIndex % 4 == 0)
                {
                    //保底 DPS 窗口与喘息(§4.3 P3-6);不写 lastAttack,缚身前后两招也不相邻重复
                    attackCount++;
                    SwitchState(PopeState.P3ChainBind);
                    return;
                }
                pool = new List<PopeState>
                {
                    PopeState.P3SafeZones, PopeState.P3LightningOrbs,
                    PopeState.P3PounceSweep, PopeState.P3ReflectLaser
                };
                if (NPC.life < NPC.lifeMax * 0.2f)
                {
                    pool.Add(PopeState.P3FinalBombs);
                }
                pool.RemoveAll(s => (byte)s == lastAttack);
                PopeState nextP3 = pool[Rng(attackCount * 13 + 5).Next(pool.Count)];
                lastAttack = (byte)nextP3;
                attackCount++;
                SwitchState(nextP3);
                return;
            }
            if (phase >= 2)
            {
                pool = new List<PopeState>
                {
                    PopeState.P2PortalWorms, PopeState.P2TripleBomb, PopeState.P2ChainStorm,
                    PopeState.P2TwinScythe, PopeState.P2DiscLaser, PopeState.P2Omniscient,
                    PopeState.P2ClawSwarm, PopeState.P2DiscDive
                };
                if (NPC.life < NPC.lifeMax * 0.55f)
                {
                    pool.Add(PopeState.P2LanternBomb);
                }
                if (NPC.life < NPC.lifeMax * 0.5f && usedOmniscient)
                {
                    pool.Add(PopeState.P2TrinityEye);
                }
            }
            else
            {
                pool = new List<PopeState>
                {
                    PopeState.PortalWorms, PopeState.DeathBombCast, PopeState.ChainHunt, PopeState.ScytheDash
                };
                if (NPC.life < NPC.lifeMax * 0.85f)
                {
                    pool.Add(PopeState.Trinity);
                }
                if (NPC.life < NPC.lifeMax * 0.75f)
                {
                    pool.Add(PopeState.SoulLanterns);
                }
            }
            pool.RemoveAll(s => (byte)s == lastAttack);
            PopeState next = pool[Rng(attackCount * 13 + 5).Next(pool.Count)];
            if (next == PopeState.P2Omniscient)
            {
                usedOmniscient = true;
            }
            lastAttack = (byte)next;
            attackCount++;
            SwitchState(next);
        }

        /// <summary>巡航基线(§4.0 现值保留):朝玩家 0.44 加速 + 0.98 阻尼。accelScale 给"只漂移"的招降档。</summary>
        private void CruiseDrift(Player target, float accelScale = 1f)
        {
            NPC.velocity += (target.Center - NPC.Center).SafeNormalize(Vector2.Zero) * 0.44f * accelScale;
            NPC.velocity *= 0.98f;
        }

        /// <summary>玩家在本体哪侧(±1)。</summary>
        private int PlayerSide(Player target)
        {
            return target.Center.X >= NPC.Center.X ? 1 : -1;
        }

        /// <summary>按 (方向, 编队层) 找手。找不到返回 null(手被清场等边角);P1 双手恒层 0。</summary>
        public VoidPopeHand FindHand(int direction, int layer = 0)
        {
            foreach (NPC n in Main.ActiveNPCs)
            {
                if (n.ModNPC is VoidPopeHand hand && (int)n.ai[0] == NPC.whoAmI
                    && (int)n.ai[1] == direction && (int)n.ai[3] == layer)
                {
                    return hand;
                }
            }
            return null;
        }

        /// <summary>
        /// 手的当前 role(§4.5:0 巡航/1 举升/2 持镰/3 提灯/4 刺索;
        /// M8 增 P3 专用:5 拽体横扫/6 缚身垂落;role 语义 P2 不变)。
        /// 双端由同步状态确定性推导,手在自己 AI 里逐帧拉取写进 ai[2]。
        /// </summary>
        public byte CurrentHandRole(int handDirection, int handLayer)
        {
            if (transitionTimer > 0)
            {
                return 0;
            }
            if (phase >= 3)
            {
                switch (State)
                {
                    case PopeState.P3FinalBombs:
                        return 1; //举升托弹(弹吸附在 ±262,-96)
                    case PopeState.P3PounceSweep:
                        return 5; //拽体/横扫(细节位移手侧按 SweepBeats 同表推导)
                    case PopeState.P3ChainBind:
                        return 6; //缚身垂落
                    default:
                        return 0; //两侧远位悬浮待命(§4.3)
                }
            }
            switch (State)
            {
                //———P1———
                case PopeState.DeathBombCast:
                    return attackTimer <= BombBeat + DeathBomb.ThrowTick ? (byte)1 : (byte)0;
                case PopeState.ScytheDash:
                    return 2;
                case PopeState.SoulLanterns:
                    return 3;
                case PopeState.ChainHunt:
                    return NPC.HasValidTarget && handDirection == PlayerSide(Main.player[NPC.target]) ? (byte)4 : (byte)0;
                case PopeState.Trinity:
                    return attackTimer > TeleTotal ? (byte)4 : (byte)0;
                //———P2(§4.2)———
                case PopeState.P2TripleBomb:
                    //六手两两一组举升,掷完收回
                    return attackTimer <= P2BombBeat + DeathBomb.ChargeTimeP2 + DeathBomb.ShrinkTime + 60 ? (byte)1 : (byte)0;
                case PopeState.P2LanternBomb:
                    //下层双手提灯,上四手凝弹;阵雨段全体收回
                    if (attackTimer > P2HarvestEnd + 30)
                    {
                        return 0;
                    }
                    return handLayer == 0 ? (byte)3 : (byte)1;
                case PopeState.P2ChainStorm:
                    return 4;
                case PopeState.P2TwinScythe:
                    //两侧四手持镰,上两手举法球
                    return handLayer == 2 ? (byte)1 : (byte)2;
                case PopeState.P2TrinityEye:
                    return attackTimer > TeleTotal ? (byte)4 : (byte)0;
                default:
                    return 0;
            }
        }

        /// <summary>三位一体分身位(§4.1:本体相对玩家的偏移旋转 ±120°,确定性,双端同式)。k = 0/1。</summary>
        public Vector2 ClonePos(Vector2 targetCenter, int k)
        {
            Vector2 rel = NPC.Center - targetCenter;
            return targetCenter + rel.RotatedBy((k == 0 ? 1f : -1f) * MathHelper.TwoPi / 3f);
        }

        /// <summary>分身透明度(窗口首尾 20t 渐显/渐隐;三位一体与分身之眼共用)。</summary>
        public float CloneAlpha
        {
            get
            {
                if ((State != PopeState.Trinity && State != PopeState.P2TrinityEye) || attackTimer <= TeleTotal)
                {
                    return 0f;
                }
                float ramp = Math.Min(attackTimer - TeleTotal, TrinityTotal - attackTimer) / (float)TrinityFadeIn;
                return 0.8f * MathHelper.Clamp(ramp, 0f, 1f);
            }
        }

        public override void AI()
        {
            //首帧补手(镜像原型:服务端生成左右两只)
            if (spawnHands)
            {
                spawnHands = false;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y, ModContent.NPCType<VoidPopeHand>(), 0, NPC.whoAmI, 1);
                    NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y, ModContent.NPCType<VoidPopeHand>(), 0, NPC.whoAmI, -1);
                }
            }

            flapCounter += MathHelper.TwoPi / 50f;

            //———死亡演出优先于一切(§4.4:目标全灭也走完;不吃超时兜底)———
            if (State == PopeState.P3Death)
            {
                P3DeathAI();
                UpdateFallingPieces();
                prevCenter = NPC.Center;
                return;
            }

            //目标丢失:升空脱离(每招的出口之外的全局兜底;领域视觉与闪电球随本体消失一并清理)
            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient && State != PopeState.Hover)
                    {
                        boundTimer = 0;
                        SwitchState(PopeState.Hover);
                    }
                    NPC.velocity.Y -= 0.25f;
                    NPC.velocity *= 0.98f;
                    NPC.EncourageDespawn(10);
                    return;
                }
            }
            Player target = Main.player[NPC.target];

            //———换阶触发(服务端定夺,清弹幕 + 无敌演出):65% P1→P2(§4.2)/ 40% P2→P3(§4.3,只触发一次)———
            if (Main.netMode != NetmodeID.MultiplayerClient && transitionTimer == 0)
            {
                if (phase == 1 && NPC.life < NPC.lifeMax * 0.65f)
                {
                    BeginTransition();
                }
                else if (phase == 2 && NPC.life < NPC.lifeMax * 0.4f)
                {
                    BeginTransition();
                }
            }

            //———换阶演出分支(transitionTimer > 0,双端各自逐帧推进;按 phase 分流)———
            if (transitionTimer > 0)
            {
                if (phase == 1)
                {
                    TransitionAI(target);
                }
                else
                {
                    TransitionP3AI(target);
                }
                UpdateFeathers();
                prevCenter = NPC.Center;
                return;
            }

            attackTimer++;

            //———P3 常态维护(§4.3:判定框改写/锚点/自损/DR/领域展开;双端同式,服务端节流同步)———
            if (phase >= 3)
            {
                P3Upkeep();
            }

            //瞬移/遁入期间接触归零(§4.0:无敌帧不加,瞬移中本体一直可打;遁入隐身另关受击)
            //接触档:P1 170 / P2 190 / P3 本体 200、扑击期 220(§4.3)
            int contactDamage = NPC.defDamage;
            if (phase >= 3)
            {
                bool pouncing = State == PopeState.P3PounceSweep
                    && attackTimer >= PounceLaunch && attackTimer < PounceBrakeEnd;
                contactDamage = NPC.defDamage + (pouncing ? 50 : 30);
                //缚身窗口是给近战的 DPS 阀门:被铁索钉住的躯体不反伤(§4.3 公平阀)
                if (boundTimer > 0)
                {
                    contactDamage = 0;
                }
            }
            else if (phase >= 2)
            {
                contactDamage = NPC.defDamage + 20;
            }
            NPC.damage = InTeleport || DiveHidden ? 0 : contactDamage;
            //遁入隐身窗(§4.2 P2-8):判定关,玩家打不脱;破门帧必然恢复
            NPC.dontTakeDamage = DiveHidden;

            //瞬移三拍的通用演出(位置切换在拍点由服务端执行)
            if (AttackHasTeleport(State) && attackTimer <= TeleTotal)
            {
                TeleportBeat(target, attackTimer);
            }

            switch (State)
            {
                case PopeState.Hover: HoverAI(target); break;
                case PopeState.PortalWorms: PortalWormsAI(target); break;
                case PopeState.DeathBombCast: DeathBombCastAI(target); break;
                case PopeState.ChainHunt: ChainHuntAI(target); break;
                case PopeState.ScytheDash: ScytheDashAI(target); break;
                case PopeState.Trinity: TrinityAI(target, false); break;
                case PopeState.SoulLanterns: SoulLanternsAI(target); break;
                case PopeState.P2PortalWorms: P2PortalWormsAI(target); break;
                case PopeState.P2TripleBomb: P2TripleBombAI(target); break;
                case PopeState.P2LanternBomb: P2LanternBombAI(target); break;
                case PopeState.P2ChainStorm: P2ChainStormAI(target); break;
                case PopeState.P2TwinScythe: P2TwinScytheAI(target); break;
                case PopeState.P2DiscLaser: P2DiscLaserAI(target); break;
                case PopeState.P2Omniscient: P2OmniscientAI(target); break;
                case PopeState.P2TrinityEye: TrinityAI(target, true); break;
                case PopeState.P2ClawSwarm: P2ClawSwarmAI(target); break;
                case PopeState.P2DiscDive: P2DiscDiveAI(target); break;
                case PopeState.P3SafeZones: P3SafeZonesAI(target); break;
                case PopeState.P3LightningOrbs: P3LightningOrbsAI(target); break;
                case PopeState.P3PounceSweep: P3PounceSweepAI(target); break;
                case PopeState.P3ReflectLaser: P3ReflectLaserAI(target); break;
                case PopeState.P3FinalBombs: P3FinalBombsAI(target); break;
                case PopeState.P3ChainBind: P3ChainBindAI(target); break;
            }

            //超时兜底:任何招卡拍一律拉回选招(正常出口都在各招方法里,这里只防不可预见的停摆)
            if (attackTimer > 1200 && Main.netMode != NetmodeID.MultiplayerClient && State != PopeState.Hover)
            {
                discState = 0;
                boundTimer = 0;
                SwitchState(PopeState.Hover);
            }

            //冲刺拖尾采样与换位白闪检测(双端视觉)
            odp.Add(NPC.Center);
            if (odp.Count > 10)
            {
                odp.RemoveAt(0);
            }
            bool trinityLike = State == PopeState.Trinity || State == PopeState.P2TrinityEye;
            if (!Main.dedServ && trinityLike && attackTimer > TeleTotal + 5
                && prevCenter != Vector2.Zero && Vector2.Distance(prevCenter, NPC.Center) > 250f)
            {
                SwapFlash(prevCenter);
                SwapFlash(NPC.Center);
            }
            prevCenter = NPC.Center;

            //换阶羽影余烬(演出结束后继续飘落至消散)
            UpdateFeathers();

            if (phase >= 3)
            {
                //P3 结晶主体:缓慢自旋 ±4°(§4.3);缚身期被铁索拉扯倾斜 + 末段震颤
                float spin = (float)Math.Sin(flapCounter * 0.3f) * MathHelper.ToRadians(4);
                if (State == PopeState.P3ChainBind && attackTimer > BindFirstPin)
                {
                    float pinP = MathHelper.Clamp((attackTimer - BindFirstPin) / (float)(BindPinned - BindFirstPin), 0f, 1f);
                    spin += MathHelper.ToRadians(9) * pinP * (float)Math.Sin(flapCounter * 0.8f);
                    if (boundTimer > 0 && boundTimer < 40)
                    {
                        spin += (float)Math.Sin(attackTimer * 1.7f) * 0.05f; //挣脱前震颤
                    }
                }
                NPC.rotation = NPC.rotation * 0.85f + spin * 0.15f;
            }
            else
            {
                //身体朝速度微倾
                NPC.rotation = NPC.rotation * 0.9f + NPC.velocity.X * 0.003f;
            }
        }

        /// <summary>
        /// P3 常态维护(§4.3,双端同式推进,服务端 30t 节流 netUpdate 校正):
        /// 判定框放大一次(双端同式改写,位置补偿)、领域锚点缓校正(扑击期冻结)、
        /// 领域展开进度推进、每秒自损 1% 最大生命(双端预测,life&lt;2 汇入死亡演出)、
        /// 缚身窗口递减与 DR 切换、边界常燃粒子。
        /// </summary>
        private void P3Upkeep()
        {
            //判定框改写(680 结晶主体的合理接触框;双端同式 → 无需入包)
            if (NPC.width != P3HitboxSize)
            {
                NPC.position += new Vector2((NPC.width - P3HitboxSize) / 2f, (NPC.height - P3HitboxSize) / 2f);
                NPC.width = P3HitboxSize;
                NPC.height = P3HitboxSize;
            }
            //领域锚点:静止期跟随本体缓校正(自愈中途加入的偏差),扑击期冻结
            if (!anchorInit)
            {
                anchorInit = true;
                domainAnchor = NPC.Center;
            }
            else if (State != PopeState.P3PounceSweep)
            {
                domainAnchor = Vector2.Lerp(domainAnchor, NPC.Center, 0.05f);
            }
            //领域展开进度(换阶末已缓张到位,这里只兜底推满)
            if (domainRadiusFactor < 1f)
            {
                domainRadiusFactor = Math.Min(domainRadiusFactor + 1f / DomainExpand, 1f);
            }
            //缚身窗口递减(双端各自,包到达校正)与 DR 切换(§4.3:0.99 常态 / 缚身归零)
            if (boundTimer > 0)
            {
                boundTimer--;
            }
            DamageReduction = boundTimer > 0 ? 0f : 0.99f;
            //每秒自损 1% 最大生命(逐 tick 折算 32000/60;双端同式预测,服务端 life 原生同步校正)
            selfBurnAcc += NPC.lifeMax / 6000f;
            int burn = (int)selfBurnAcc;
            if (burn > 0)
            {
                selfBurnAcc -= burn;
                NPC.life -= burn;
            }
            if (NPC.life < 2)
            {
                NPC.life = 1;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    EnterDeath();
                    return;
                }
            }
            //同步节流(服务端每 30t 一拍,血条平滑靠客户端同公式预测)
            if (Main.netMode != NetmodeID.MultiplayerClient && attackTimer % 30 == 0)
            {
                NPC.netUpdate = true;
                if (NPC.netSpam >= 10)
                    NPC.netSpam = 9;
            }
            //边界常燃粒子(退化路线的一部分:EnablePixelEffect 关闭时环带 + 粒子仍在)
            if (!Main.dedServ && domainRadiusFactor > 0.5f && Main.rand.NextBool(2))
            {
                float ang = CEUtils.randomRot();
                Vector2 pos = DomainAnchor + ang.ToRotationVector2() * DomainRadius;
                var p = PRTLoader.NewParticle<PRT_Light>(pos, -ang.ToRotationVector2() * Main.rand.NextFloat(0.3f, 1.4f),
                    Main.rand.NextBool() ? new Color(170, 90, 255) : new Color(120, 60, 220), Main.rand.NextFloat(0.3f, 0.55f));
                p.Configure(0.9f, lifetime: 22);
            }
            //领域滤镜激活(演出二迭:域内轻微色偏/域外压暗;EnablePixelEffect 关闭即不激活,
            //滤镜数据侧 Update 自灭兜底——两态都优雅)
            if (!Main.dedServ && domainRadiusFactor > 0.02f
                && ModContent.GetInstance<Config>().EnablePixelEffect
                && !Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:PopeDomain"].IsActive())
            {
                Terraria.Graphics.Effects.Filters.Scene.Activate("CalamityEntropy:PopeDomain", NPC.Center);
            }
        }

        //———瞬移语言(§4.0):预告 25t → 渐隐 12t → 浮现 12t;计时由调用者给(招式用 attackTimer,换阶用 transitionTimer)———
        private void TeleportBeat(Player target, int t)
        {
            //首帧:服务端定夺目的地并广播
            if (t == 1 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                teleTarget = PickTeleTarget(target);
                NPC.netUpdate = true;
            }
            if (t == 1 && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.8f, Pitch = -0.35f }, teleTarget == Vector2.Zero ? NPC.Center : teleTarget);
            }
            //预告段:目的地法阵闪光(绘制在 PreDraw),本体减速让读招
            if (t <= TeleWarn)
            {
                NPC.velocity *= 0.9f;
                return;
            }
            //渐隐段:粒子从均匀散射改为流向落点(身体化作流光被"抽"过去)
            if (t <= TeleWarn + TeleFadeOut)
            {
                NPC.velocity *= 0.85f;
                if (!Main.dedServ)
                {
                    Vector2 toward = teleTarget != Vector2.Zero
                        ? (teleTarget - NPC.Center).SafeNormalize(Vector2.Zero)
                        : Vector2.Zero;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 vel = toward * Main.rand.NextFloat(4f, 10f)
                            + CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(0.5f, 2f);
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center + CEUtils.randomPointInCircle(60f), vel, Color.White, 1f);
                        v.Opacity = Main.rand.Next(30, 90) * 0.01f;
                    }
                }
                //渐隐末拍:服务端切位
                if (t == TeleWarn + TeleFadeOut && Main.netMode != NetmodeID.MultiplayerClient && teleTarget != Vector2.Zero)
                {
                    NPC.Center = teleTarget;
                    NPC.velocity = Vector2.Zero;
                    NPC.netUpdate = true;
                }
                return;
            }
            //浮现首帧:空间合拢的一记纵向冲环(到场的"啪")
            if (t == TeleWarn + TeleFadeOut + 1 && !Main.dedServ)
            {
                var snap = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(NPC.Center, Vector2.Zero, new Color(200, 130, 255), 0.1f);
                snap.Configure(new Vector2(0.35f, 1.8f), 0f, 1.8f, 13);
            }
            //浮现段:各向异性内聚流光(拉长的光被拽进身体,不再是均匀光点)
            NPC.velocity *= 0.9f;
            if (!Main.dedServ && Main.rand.NextBool())
            {
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(90f, 220f);
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.13f, new Color(170, 90, 255), 0.5f);
                p.Configure(0.9f, squishStrenght: 3.2f, maxSquish: 4.6f, lifetime: 13);
            }
        }

        /// <summary>各招的瞬移目的地规则(仅服务端调用);换阶演出 = 目标玩家上方屏心位。</summary>
        private Vector2 PickTeleTarget(Player target)
        {
            if (transitionTimer > 0)
            {
                return target.Center + new Vector2(0f, -260f);
            }
            int side = NPC.Center.X >= target.Center.X ? 1 : -1;
            switch (State)
            {
                case PopeState.PortalWorms:
                case PopeState.P2PortalWorms:
                    //玩家对侧(§4.1 P1-1 / §4.2 P2-1s)
                    return target.Center + new Vector2(-side * 460f, -120f);
                case PopeState.DeathBombCast:
                case PopeState.P2TripleBomb:
                    return target.Center + new Vector2(side * 300f, -80f);
                case PopeState.ScytheDash:
                case PopeState.P2TwinScythe:
                    return target.Center + new Vector2(side * 500f, -40f);
                case PopeState.Trinity:
                case PopeState.P2TrinityEye:
                    return target.Center + new Vector2(0f, -TrinityRadius);
                case PopeState.SoulLanterns:
                case PopeState.P2LanternBomb:
                    return target.Center + new Vector2(0f, -420f);
                case PopeState.P2DiscLaser:
                    //玩家侧 700px(§4.2 P2-5)
                    return target.Center + new Vector2(side * 700f, -60f);
                case PopeState.P2Omniscient:
                    //上方屏心位(§4.2 P2-6:定身于屏心)
                    return target.Center + new Vector2(0f, -320f);
                default:
                    return NPC.Center;
            }
        }

        //———换阶演出(§4.2:65% 触发,3s 全程无敌)———

        /// <summary>服务端触发换阶:清空敌对弹幕 + 起演出计时(客户端凭同步的 transitionTimer 进分支)。</summary>
        private void BeginTransition()
        {
            transitionTimer = 1;
            teleTarget = Vector2.Zero;
            discState = 0;
            SwitchState(PopeState.Hover);
            //清空敌对弹幕(§4.2 公平阀;服务端 Kill 原生同步)
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if (p.hostile && !p.friendly)
                {
                    p.Kill();
                }
            }
            NPC.netUpdate = true;
        }

        /// <summary>
        /// 换阶演出主体(双端各自逐帧推进 transitionTimer,确定性同式;演出二迭校拍):
        /// 瞬移屏心 49t → 悬停吸气拍(49~62,静止 + 微粒内聚)→ 羽影爆散(62,白闪 + 冲环,羽片先悬再坠)
        /// → 躯体交叉渐变(62~92)→ 二阶段翼弹性展开(92~142)→ 新增 4 手(96,服务端一次)
        /// → 咆哮 + 无伤冲击环(150)→ 180 落 phase=2 进 P2 选招。全程 dontTakeDamage、接触归零。
        /// </summary>
        private void TransitionAI(Player target)
        {
            transitionTimer++;
            int t = transitionTimer;
            NPC.dontTakeDamage = true; //只在这 3s(§4.2)
            NPC.damage = 0;

            if (t <= TeleTotal)
            {
                TeleportBeat(target, t);
            }
            else
            {
                NPC.velocity *= 0.9f;
            }

            //悬停吸气拍(演出二迭:爆散前 13t 静止,微粒向双翼内聚——碎裂前的静默)
            if (!Main.dedServ && t > TeleTotal && t < TransFeatherBurst && Main.rand.NextBool(2))
            {
                int side = Main.rand.NextBool() ? 1 : -1;
                Vector2 wing = NPC.Center + new Vector2(side * 70f, -50f);
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 150f);
                var inhale = PRTLoader.NewParticle<PRT_Light>(wing + offset, -offset * 0.11f, new Color(190, 110, 255), 0.45f);
                inhale.Configure(0.9f, squishStrenght: 2.6f, lifetime: 12);
            }
            //羽影爆散拍(§4.2:一阶段翼爆散 20 片羽影,纯客户端视觉;冲击帧配一档白闪)
            if (t == TransFeatherBurst && !Main.dedServ)
            {
                SpawnFeathers();
                SoundEngine.PlaySound(SoundID.NPCDeath6 with { Volume = 1f, Pitch = -0.3f }, NPC.Center);
                if (Main.LocalPlayer.Distance(NPC.Center) < 2200f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.25f;
                }
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(200, 130, 255), 0.1f).Configure(4.5f, 30);
            }
            //躯体渐变起拍音
            if (t == TransFeatherBurst + 2 && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.9f, Pitch = -0.6f }, NPC.Center);
            }
            //新增 4 只手(§4.2:服务端 NewNPC,只生成一次;渐显由手自管)
            if (t == TransHandSpawn && Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int layer = 1; layer <= 2; layer++)
                {
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        int np = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y,
                            ModContent.NPCType<VoidPopeHand>(), 0, NPC.whoAmI, dir, 0, layer);
                        if (np < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                        {
                            NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                        }
                    }
                }
            }
            //翼展开期的汇聚粒子
            if (!Main.dedServ && t > TransBodyFadeEnd && t < TransWingEnd && Main.rand.NextBool(2))
            {
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(80f, 200f);
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.08f, new Color(190, 100, 255), 0.5f);
                p.Configure(0.85f, lifetime: 14);
            }
            //咆哮 + 无伤冲击环(§4.2)
            if (t == TransRoar && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Roar with { Volume = 1.2f, Pitch = -0.15f }, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 10), Main.LocalPlayer.Distance(NPC.Center), 2400);
                if (Main.LocalPlayer.Distance(NPC.Center) < 2200f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.4f;
                }
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(9f, 50);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(6f, 40);
            }
            //演出收束:phase=2 落同步,进 P2 选招(双端同式推进,服务端包到达后覆盖一致值)
            if (t >= TransTotal)
            {
                phase = 2;
                transitionTimer = 0;
                NPC.dontTakeDamage = false;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    SwitchState(PopeState.Hover);
                }
            }
        }

        /// <summary>
        /// P2→P3 换阶演出(§4.3,40% 触发,3.5s 无敌,双端各自逐帧推进):
        /// 瞬移屏心 49t → 二阶段躯体过曝碎裂(50,白闪 + 粒子)→ bodyP3 自碎片拼合(55~110 渐显)
        /// → 多余四手渐隐(90 起)并在 110 服务端 despawn(保留层 0 双手换贴图换参数)
        /// → 侧结构飞入肩位(110~140)→ 领域展开(150 起 60t 缓张 0→1150)→ 210 落 phase=3。
        /// </summary>
        private void TransitionP3AI(Player target)
        {
            transitionTimer++;
            int t = transitionTimer;
            NPC.dontTakeDamage = true;
            NPC.damage = 0;

            if (t <= TeleTotal)
            {
                TeleportBeat(target, t);
            }
            else
            {
                NPC.velocity *= 0.88f;
            }

            //过曝碎裂拍(§4.3:二阶段躯体白闪 + 结晶碎片真实飞出——之后悬停一拍再逆聚拼合)
            if (t == TransP3Shatter && !Main.dedServ)
            {
                if (Main.LocalPlayer.Distance(NPC.Center) < 2200f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.5f;
                }
                SoundEngine.PlaySound(SoundID.Item27 with { Volume = 1.1f, Pitch = -0.5f }, NPC.Center);
                SoundEngine.PlaySound(SoundID.NPCDeath6 with { Volume = 1f, Pitch = -0.4f }, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 8), Main.LocalPlayer.Distance(NPC.Center), 2200);
                SpawnShards();
                for (int i = 0; i < 40; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center + CEUtils.randomPointInCircle(120f),
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 11f), Color.White, 1f);
                    v.Opacity = Main.rand.Next(30, 95) * 0.01f;
                }
                for (int i = 0; i < 24; i++)
                {
                    Dust.NewDust(NPC.Center + CEUtils.randomPointInCircle(130f), 1, 1, ModContent.DustType<Dusts.GlassBreak>());
                }
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(6f, 36);
            }
            //拼合段:碎片逆聚由 UpdateShards 承载,这里只补少量内聚流光
            if (!Main.dedServ && t > TransP3Shatter && t < TransP3BodyEnd && Main.rand.NextBool(3))
            {
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(120f, 320f);
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.09f, new Color(190, 100, 255), 0.5f);
                p.Configure(0.88f, squishStrenght: 2.8f, lifetime: 14);
            }
            UpdateShards(t);
            //多余四手 despawn(§4.3:服务端把 P2 新增的 4 只收回消失;保留层 0 双手换装)
            if (t == TransP3HandGone && Main.netMode != NetmodeID.MultiplayerClient)
            {
                foreach (NPC n in Main.ActiveNPCs)
                {
                    if (n.ModNPC is VoidPopeHand && (int)n.ai[0] == NPC.whoAmI && (int)n.ai[3] > 0)
                    {
                        n.active = false;
                        n.netUpdate = true;
                        if (Main.netMode == NetmodeID.Server)
                        {
                            NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, n.whoAmI);
                        }
                    }
                }
            }
            //侧结构飞入的汇聚粒子
            if (!Main.dedServ && t > TransP3HandGone && t < TransP3ShoulderEnd && Main.rand.NextBool(3))
            {
                int dir = Main.rand.NextBool() ? 1 : -1;
                Vector2 shoulder = NPC.Center + new Vector2(dir * 252f, -168f);
                var p = PRTLoader.NewParticle<PRT_Light>(shoulder + CEUtils.randomPointInCircle(50f), Vector2.Zero, new Color(200, 130, 255), 0.4f);
                p.Configure(0.85f, lifetime: 12);
            }
            //领域展开拍(§4.3:咆哮 + 圆环屏障 0→1150 缓张 60t)
            if (t == TransP3DomainStart && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Roar with { Volume = 1.3f, Pitch = -0.3f }, NPC.Center);
                SoundEngine.PlaySound(SoundID.Item122 with { Volume = 1f, Pitch = -0.5f }, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 11), Main.LocalPlayer.Distance(NPC.Center), 2600);
                if (Main.LocalPlayer.Distance(NPC.Center) < 2400f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.35f;
                }
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(10f, 55);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(7f, 42);
            }
            //领域缓张(双端同式;domainRadiusFactor 进同步包,包到达校正)
            if (t >= TransP3DomainStart)
            {
                domainRadiusFactor = MathHelper.Clamp((t - TransP3DomainStart) / (float)DomainExpand, 0f, 1f);
                //展开沿途的边界粒子潮
                if (!Main.dedServ && Main.rand.NextBool())
                {
                    float ang = CEUtils.randomRot();
                    Vector2 pos = NPC.Center + ang.ToRotationVector2() * DomainRadius;
                    var p = PRTLoader.NewParticle<PRT_Light>(pos, ang.ToRotationVector2() * 1.2f, new Color(170, 90, 255), 0.5f);
                    p.Configure(0.9f, lifetime: 18);
                }
            }
            //演出收束:phase=3 落同步(§4.3),锚点定于屏心落点
            if (t >= TransP3Total)
            {
                phase = 3;
                transitionTimer = 0;
                domainRadiusFactor = 1f;
                anchorInit = true;
                domainAnchor = NPC.Center;
                NPC.dontTakeDamage = false;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    SwitchState(PopeState.Hover);
                }
            }
        }

        /// <summary>生成 20 片羽影(§4.2:自双翼位四散,纯客户端;爆散初速上调配合悬停拍)。</summary>
        private void SpawnFeathers()
        {
            feathers.Clear();
            for (int i = 0; i < FeatherCount; i++)
            {
                int dir = i % 2 == 0 ? -1 : 1;
                Vector2 wingPos = NPC.Center + new Vector2(dir * Main.rand.NextFloat(30f, 120f), Main.rand.NextFloat(-110f, -10f));
                feathers.Add(new FeatherFx
                {
                    pos = wingPos,
                    vel = new Vector2(dir * Main.rand.NextFloat(3f, 10f), Main.rand.NextFloat(-6f, 0.5f)),
                    rot = CEUtils.randomRot(),
                    rotVel = Main.rand.NextFloat(-0.14f, 0.14f),
                    life = 1f,
                });
            }
        }

        /// <summary>羽影推进(客户端视觉:爆散急衰 → 12t 悬停一拍 → 重量弧线坠落,~90t 消散)。</summary>
        private void UpdateFeathers()
        {
            if (Main.dedServ || feathers.Count == 0)
            {
                return;
            }
            for (int i = feathers.Count - 1; i >= 0; i--)
            {
                FeatherFx f = feathers[i];
                if (f.life > 0.87f)
                {
                    //爆散后的悬停一拍:速度急衰,不吃重力(部件先停在空中)
                    f.vel *= 0.88f;
                }
                else
                {
                    f.vel *= 0.965f;
                    f.vel.Y += 0.055f;
                }
                f.pos += f.vel;
                f.rot += f.rotVel;
                f.life -= 1f / 90f;
                if (f.life <= 0f)
                {
                    feathers.RemoveAt(i);
                }
                else
                {
                    feathers[i] = f;
                }
            }
        }

        /// <summary>P2→P3 结晶碎片爆散(演出二迭:16 片自躯体位飞出,纯客户端)。</summary>
        private void SpawnShards()
        {
            shards.Clear();
            for (int i = 0; i < 16; i++)
            {
                Vector2 offset = CEUtils.randomPointInCircle(95f);
                shards.Add(new CrystalShard
                {
                    pos = NPC.Center + offset,
                    vel = offset.SafeNormalize(CEUtils.randomRot().ToRotationVector2()) * Main.rand.NextFloat(7f, 14f)
                        + CEUtils.randomRot().ToRotationVector2() * 1.5f,
                    rot = CEUtils.randomRot(),
                    rotVel = Main.rand.NextFloat(-0.2f, 0.2f),
                    scale = Main.rand.NextFloat(0.45f, 0.95f),
                    delay = i / 16f,
                });
            }
        }

        /// <summary>
        /// 结晶碎片推进(演出二迭三拍:飞出急衰 14t → 悬停微颤 12t → 错拍逆聚拼合;
        /// 到位或拼合窗结束即消,附一记白色小闪——"结晶拼合"由真实碎片讲出来)。
        /// </summary>
        private void UpdateShards(int t)
        {
            if (Main.dedServ || shards.Count == 0)
            {
                return;
            }
            int rel = t - TransP3Shatter;
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                CrystalShard s = shards[i];
                if (rel < 14)
                {
                    s.vel *= 0.88f; //飞出急衰
                }
                else if (rel < 26)
                {
                    s.vel *= 0.8f;  //悬停一拍
                    s.pos += CEUtils.randomRot().ToRotationVector2() * 0.5f;
                }
                else
                {
                    //逆聚:各片按 delay 错拍启动,吸附速率随进度平方加急
                    float cp = MathHelper.Clamp((rel - 26 - s.delay * 14f) / 26f, 0f, 1f);
                    s.vel *= 0.7f;
                    s.pos += (NPC.Center - s.pos) * (0.03f + 0.34f * cp * cp);
                }
                s.pos += s.vel;
                s.rot += s.rotVel;
                bool arrived = rel >= 26 && Vector2.Distance(s.pos, NPC.Center) < 26f;
                if (arrived || t >= TransP3BodyEnd || transitionTimer <= 0)
                {
                    var blip = PRTLoader.NewParticle<PRT_Light>(s.pos, Vector2.Zero, Color.White, 0.7f);
                    blip.Configure(0.85f, lifetime: 8);
                    shards.RemoveAt(i);
                }
                else
                {
                    shards[i] = s;
                }
            }
        }

        /// <summary>结晶碎片绘制(加法,Diamond 紫晶片 + 白芯)。</summary>
        private void DrawShards(SpriteBatch sb, Vector2 screenPos)
        {
            if (shards.Count == 0)
            {
                return;
            }
            sb.UseAdditive();
            Texture2D tex = CEExtraAssets.Diamond;
            foreach (CrystalShard s in shards)
            {
                sb.Draw(tex, s.pos - screenPos, null, new Color(200, 140, 255) * 0.95f, s.rot, tex.Size() / 2, s.scale, SpriteEffects.None, 0);
                sb.Draw(tex, s.pos - screenPos, null, Color.White * 0.55f, s.rot, tex.Size() / 2, s.scale * 0.55f, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
        }

        //———呼吸拍:48~72t 巡航漂移后选招———
        private void HoverAI(Player target)
        {
            //魔盘残留安全网:非正常出口(目标丢失/超时)进 Hover 时令盘退场(9 = 破门渐隐自管,不动)
            if (attackTimer == 1 && discState != 0 && discState != 5 && discState != 9)
            {
                discState = 5;
            }
            if (attackTimer == 1 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                hoverLen = HoverMin + Rng(attackCount * 5 + 3).Next(HoverSpan);
            }
            if (phase >= 3)
            {
                //P3:定于领域中心不动(§4.3),呼吸拍只回位
                HoldAtAnchor();
            }
            else
            {
                CruiseDrift(target);
            }
            if (attackTimer >= hoverLen && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NextAttack();
            }
        }

        /// <summary>P3 定点:向领域锚点缓吸(圆心站桩是领域生存战的舞台设定)。</summary>
        private void HoldAtAnchor()
        {
            Vector2 want = (DomainAnchor - NPC.Center) * 0.06f;
            if (want.Length() > 14f)
            {
                want = want.SafeNormalize(Vector2.Zero) * 14f;
            }
            NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.2f);
        }

        //———P1-1 门中蠕虫:瞬移对侧 → 身后弧形 4 门 → 每门一只蠕虫扫扇喷火;本体只漂移(输出窗口)———
        private void PortalWormsAI(Player target)
        {
            if (attackTimer <= TeleTotal)
            {
                return;
            }
            if (attackTimer == WormsSpawnBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 away = (NPC.Center - target.Center).SafeNormalize(Vector2.UnitX);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 pos = NPC.Center + away.RotatedBy((k - 1.5f) * 0.42f) * 185f;
                    VoidPortal.Open(NPC.GetSource_FromAI(), pos, target.Center - pos, WormsPortalLife, 1.1f);
                    int np = NPC.NewNPC(NPC.GetSource_FromAI(), (int)pos.X, (int)pos.Y, ModContent.NPCType<VoidWormlet>(),
                        0, NPC.whoAmI, WormsPortalLife, k * 15);
                    if (np < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                }
            }
            //只漂移不打(§4.1:输出窗口在他身上)
            CruiseDrift(target, 0.45f);
            if (attackTimer >= WormsTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P1-2 死亡爆弹:瞬移玩家侧 → 双手举升凝爆弹(爆弹自管激光/静默/投掷)→ 掷出后收招———
        private void DeathBombCastAI(Player target)
        {
            if (attackTimer <= TeleTotal)
            {
                return;
            }
            if (attackTimer == BombBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.765f + 0.5f); //爆弹 260 经典档(敌对弹幕命中 ×2)
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(0, -118), Vector2.Zero,
                    ModContent.ProjectileType<DeathBomb>(), damage, 6f, -1, NPC.whoAmI, 0f);
            }
            //凝弹期定身微漂(读招),掷出后恢复巡航
            if (attackTimer <= BombBeat + DeathBomb.ThrowTick)
            {
                NPC.velocity *= 0.92f;
            }
            else
            {
                CruiseDrift(target, 0.7f);
            }
            if (attackTimer >= BombCastTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P1-3 铁索追猎:5.5px/t 追逐 6s,靠玩家侧的手刺索,第 3 刺后提速———
        private void ChainHuntAI(Player target)
        {
            Vector2 want = (target.Center - NPC.Center).SafeNormalize(Vector2.Zero) * HuntSpeed;
            NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.1f);

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                foreach (int beat in HuntStabBeats)
                {
                    if (attackTimer == beat)
                    {
                        SpawnHandChain(target);
                        break;
                    }
                }
            }
            if (attackTimer >= HuntTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>从靠玩家侧的手刺一条铁索(仅服务端;手缺位时从本体侧位出,不挂抓握)。</summary>
        private void SpawnHandChain(Player target)
        {
            int damage = (int)(NPC.defDamage * 0.5f + 0.5f); //铁索 170 经典档(§4.0)
            int side = PlayerSide(target);
            VoidPopeHand hand = FindHand(side);
            Vector2 origin = hand != null ? hand.NPC.Center : NPC.Center + new Vector2(side * 100f, 0f);
            int sourceIndex = hand != null ? hand.NPC.whoAmI : -1;
            Vector2 dir = (target.Center - origin).SafeNormalize(Vector2.UnitX * side);
            Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, dir * 0.02f,
                ModContent.ProjectileType<PopeChain>(), damage, 4f, -1, sourceIndex, 0f, 0f);
        }

        //———P1-4 魔镰冲斩:凝镰 40t → 26px/t 锁线冲刺(2 段旋斩)→ 硬刹 → 回身补第 3 斩 → 散去———
        private void ScytheDashAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            if (t < ScytheCondenseEnd)
            {
                //凝聚魔镰:定身蓄势(§4.1:0→1.6 倍放大渐实),粒子向镰位内聚
                NPC.velocity *= 0.9f;
                if (!Main.dedServ)
                {
                    if (Main.rand.NextBool())
                    {
                        //碎光聚形(演出二迭:各向异性流光 + 偶发晶闪,凝聚有仪式感)
                        Vector2 scythePos = NPC.Center + new Vector2(0, -90f);
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 170f);
                        var p = PRTLoader.NewParticle<PRT_Light>(scythePos + offset, -offset * 0.1f, new Color(190, 100, 255), 0.5f);
                        p.Configure(0.9f, squishStrenght: 2.8f, lifetime: 13);
                        if (Main.rand.NextBool(4))
                        {
                            PRTLoader.NewParticle<PRT_SparkleCal>(scythePos + CEUtils.randomPointInCircle(60f),
                                Vector2.Zero, new Color(225, 180, 255), 0.6f).Configure(new Color(150, 80, 230), 18);
                        }
                    }
                    if (t % 14 == 5)
                    {
                        float charge = (t - TeleTotal) / 40f;
                        SoundEngine.PlaySound(SoundID.Item15 with { Volume = 0.5f + charge * 0.4f, Pitch = -0.4f + charge * 0.6f }, NPC.Center);
                    }
                }
                return;
            }
            if (t == ScytheDashLaunch)
            {
                //冲刺点火:路径锁定于起手(公平阀,§4.1),一帧设速
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.velocity = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX) * ScytheDashSpeed;
                    NPC.netUpdate = true;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.DD2_WyvernDiveDown with { Volume = 1.1f, Pitch = -0.3f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 6), Main.LocalPlayer.Distance(NPC.Center), 1800);
                }
                return;
            }
            if (t < ScytheBrake)
            {
                //直线冲刺:不追踪;速度门控速度线(演出二迭:冲刺帧的隐身速度感)
                SpawnDashSpeedLines();
                if (Main.netMode != NetmodeID.MultiplayerClient && (t == ScytheSlash1 || t == ScytheSlash2))
                {
                    SpawnScytheSlash(t == ScytheSlash1 ? 1 : -1, NPC.velocity.ToRotation());
                }
                return;
            }
            if (t < ScytheRelaunch)
            {
                //硬刹(急停出重量感)
                NPC.velocity *= 0.8f;
                return;
            }
            if (t < ScytheSlash3)
            {
                //回身小步进逼(抓后撤走位,§4.1)
                Vector2 want = (target.Center - NPC.Center).SafeNormalize(Vector2.Zero) * 12f;
                NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.25f);
                return;
            }
            if (t == ScytheSlash3)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    SpawnScytheSlash(1, (target.Center - NPC.Center).ToRotation());
                }
                return;
            }
            //收招:减速 + 虚影散去(绘制侧按计时渐隐)
            NPC.velocity *= 0.92f;
            if (t >= ScytheTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>旋斩判定弧(仅服务端):基准角借初速度通道,±1 旋向与虚影演出同表。</summary>
        private void SpawnScytheSlash(int sweepDir, float baseAngle)
        {
            int damage = (int)(NPC.defDamage * 0.559f + 0.5f); //旋斩 190 经典档
            Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, baseAngle.ToRotationVector2() * 0.02f,
                ModContent.ProjectileType<PopeScytheSlash>(), damage, 5f, -1, NPC.whoAmI, sweepDir);
        }

        /// <summary>冲刺速度线(演出二迭:速度门控 >18px/t 才出——门控本身就是速度感的开关;纯客户端)。</summary>
        private void SpawnDashSpeedLines()
        {
            if (Main.dedServ || NPC.velocity.Length() < 18f || !Main.rand.NextBool(2))
            {
                return;
            }
            Vector2 dir = NPC.velocity.SafeNormalize(Vector2.UnitX);
            Vector2 side = dir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-80f, 80f);
            var s = PRTLoader.NewParticle<PRT_Light>(NPC.Center + side - dir * 40f, -NPC.velocity * 0.4f,
                new Color(170, 90, 255), Main.rand.NextFloat(0.4f, 0.6f));
            s.Configure(0.9f, squishStrenght: 4f, maxSquish: 5f, lifetime: 10);
        }

        //———P1-5 三位一体 / P2-6s 分身之眼:瞬移上方 → ±120° 纯绘制分身 → 三位公转轮刺铁索 → 每 120t 真身换位———
        //withEyes = P2-6s(§4.2):节拍与换位规则不变,额外每位教皇身边各绕 1 只全知之眼(接触判定,弹幕自治)
        private void TrinityAI(Player target, bool withEyes)
        {
            int t = attackTimer;
            //分身眼(服务端一次性生成,ai[1] = 10+k:10 真身/11、12 分身)
            if (withEyes && t == EyeSpawnBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int eyeDamage = (int)(NPC.defDamage * 0.5f + 0.5f); //眼接触 170 经典档(敌对弹幕命中 ×2)
                for (int k = 0; k < 3; k++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero,
                        ModContent.ProjectileType<OmniscientEye>(), eyeDamage, 4f, -1, NPC.whoAmI, 10 + k);
                }
            }
            if (t <= TeleTotal)
            {
                return;
            }
            //公转(§4.1:r=560 @0.03rad/t,环心跟随玩家)
            float ang = -MathHelper.PiOver2 + TrinityOrbitSpeed * (t - TeleTotal);
            Vector2 wantPos = target.Center + ang.ToRotationVector2() * TrinityRadius;
            Vector2 drift = wantPos - NPC.Center;
            if (drift.Length() > 34f)
            {
                drift = drift.SafeNormalize(Vector2.Zero) * 34f;
            }
            NPC.velocity = drift;

            //真身头顶魂火冠(可读性阀门,纯客户端)
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Top + new Vector2(Main.rand.NextFloat(-12f, 12f), -18f),
                    new Vector2(0, -Main.rand.NextFloat(0.7f, 1.5f)),
                    Main.rand.NextBool() ? new Color(200, 120, 255) : new Color(255, 230, 160), 0.55f);
                p.Configure(0.85f, lifetime: 22);
            }

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                //轮刺:前 4s 至多 1 条活跃(单发),之后升到 2(双发,压力爬坡)
                foreach (int beat in TrinitySingleStabs)
                {
                    if (t == beat)
                    {
                        int n = Array.IndexOf(TrinitySingleStabs, beat);
                        SpawnTrinityChain(target, n % 3);
                    }
                }
                foreach (int beat in TrinityDoubleStabs)
                {
                    if (t == beat)
                    {
                        int n = Array.IndexOf(TrinityDoubleStabs, beat);
                        SpawnTrinityChain(target, n % 3);
                        SpawnTrinityChain(target, (n + 1) % 3);
                    }
                }
                //换位(§4.1:真身每 120t 与随机分身互换;两位置同时白闪由双端各自凭位置跳变演出)
                foreach (int beat in TrinitySwaps)
                {
                    if (t == beat)
                    {
                        int k = Rng(cloneSwapIndex * 31 + attackCount * 7).Next(2);
                        NPC.Center = ClonePos(target.Center, k);
                        cloneSwapIndex++;
                        NPC.netUpdate = true;
                        if (NPC.netSpam >= 10)
                            NPC.netSpam = 9;
                    }
                }
            }
            if (t >= TrinityTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>三位一体的一条铁索(仅服务端)。source:0 = 真身(经手,可抓握)/1、2 = 分身(无手来源)。</summary>
        private void SpawnTrinityChain(Player target, int source)
        {
            if (source == 0)
            {
                SpawnHandChain(target);
                return;
            }
            int damage = (int)(NPC.defDamage * 0.5f + 0.5f);
            Vector2 origin = ClonePos(target.Center, source - 1);
            Vector2 dir = (target.Center - origin).SafeNormalize(Vector2.UnitY);
            Projectile.NewProjectile(NPC.GetSource_FromAI(), origin + dir * 80f, dir * 0.02f,
                ModContent.ProjectileType<PopeChain>(), damage, 4f, -1, -1f, 0f, 0f);
        }

        /// <summary>换位白闪(§4.1:两位置同时白闪 + 粒子对撒;纯客户端演出)。</summary>
        private void SwapFlash(Vector2 pos)
        {
            var flash = PRTLoader.NewParticle<PRT_Light>(pos, Vector2.Zero, Color.White, 2.6f);
            flash.Configure(0.82f, lifetime: 14);
            var flash2 = PRTLoader.NewParticle<PRT_Light>(pos, Vector2.Zero, new Color(200, 120, 255), 1.8f);
            flash2.Configure(0.85f, lifetime: 18);
            for (int i = 0; i < 14; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(pos, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 8f), Color.White, 1f);
                v.Opacity = Main.rand.Next(30, 90) * 0.01f;
            }
        }

        //———P1-6 冥魂灯收魂:瞬移上空 → 双手提灯收魂 120t(定身,受伤 ×1.15)→ 5s 法阵铁索阵雨———
        private void SoulLanternsAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            if (t < HarvestEnd)
            {
                //收魂:定身;屏幕四缘魂魄流涌向双灯(纯客户端)
                NPC.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 spawnPos = RandomScreenEdgePos();
                        Vector2 lantern = NPC.Center + new Vector2(Main.rand.NextBool() ? 150f : -150f, -4f);
                        Vector2 vel = (lantern - spawnPos).SafeNormalize(Vector2.UnitY) * Main.rand.NextFloat(9f, 15f);
                        var p = PRTLoader.NewParticle<PRT_Light>(spawnPos, vel,
                            Main.rand.NextBool() ? new Color(190, 110, 255) : new Color(240, 235, 255), Main.rand.NextFloat(0.35f, 0.6f));
                        p.Configure(0.97f, lifetime: 40);
                    }
                    if (t % 20 == 5)
                    {
                        SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.4f, Pitch = 0.4f + (t - TeleTotal) / 240f }, NPC.Center);
                    }
                }
                return;
            }
            //阵雨:每 40t 玩家脚下法阵(25t 预警)→ 竖直刺出铁索(§4.1:离开阵面即安全)
            CruiseDrift(target, 0.3f);
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int k = 0; k < RainWaves; k++)
                {
                    if (t == HarvestEnd + k * RainGap)
                    {
                        SpawnGlyphChain(target);
                        break;
                    }
                }
            }
            if (t >= LanternTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>玩家脚下的法阵铁索(仅服务端):向下探地找基点,竖直向上刺。</summary>
        private void SpawnGlyphChain(Player target)
        {
            int tx = (int)(target.Center.X / 16f);
            int ty = (int)(target.Bottom.Y / 16f);
            int limit = ty + 60;
            while (ty < limit && ty < Main.maxTilesY - 10 && !WorldGen.SolidTile(tx, ty))
            {
                ty++;
            }
            Vector2 basePos = ty >= limit || ty >= Main.maxTilesY - 10
                ? target.Bottom + new Vector2(0, 380f)
                : new Vector2(tx * 16 + 8, ty * 16);
            int damage = (int)(NPC.defDamage * 0.5f + 0.5f);
            Projectile.NewProjectile(NPC.GetSource_FromAI(), basePos, -Vector2.UnitY * 0.02f,
                ModContent.ProjectileType<PopeChain>(), damage, 4f, -1, -1f, 560f, 1f);
        }

        //———————————————— P2 八招(§4.2)————————————————

        /// <summary>找到本教皇的魔盘实例(无则 null)。</summary>
        private DimensionDisc FindDisc()
        {
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if (p.ModProjectile is DimensionDisc disc && disc.OwnerIndex == NPC.whoAmI)
                {
                    return disc;
                }
            }
            return null;
        }

        /// <summary>服务端确保魔盘存在(招式起手调用),并置入指定状态。</summary>
        private void EnsureDisc(byte state)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient && FindDisc() == null)
            {
                int contactDamage = (int)(NPC.defDamage * 0.588f + 0.5f); //掷出接触 200 经典档(§4.2 P2-8)
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(0, -60f), Vector2.Zero,
                    ModContent.ProjectileType<DimensionDisc>(), contactDamage, 6f, -1, NPC.whoAmI);
                discPos = NPC.Center + new Vector2(0, -60f);
            }
            discState = state;
        }

        //———P2-1s 六门蠕虫:P1-1 强化为 6 门;蠕虫喷火 2 波后脱门追踪(脱门段在 VoidWormlet)———
        private void P2PortalWormsAI(Player target)
        {
            if (attackTimer <= TeleTotal)
            {
                return;
            }
            if (attackTimer == P2WormsSpawnBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 away = (NPC.Center - target.Center).SafeNormalize(Vector2.UnitX);
                for (int k = 0; k < 6; k++)
                {
                    Vector2 pos = NPC.Center + away.RotatedBy((k - 2.5f) * 0.34f) * 195f;
                    int wormLife = P2WormsEmergeLife + k * 8; //探身段微错拍,脱门此起彼伏
                    VoidPortal.Open(NPC.GetSource_FromAI(), pos, target.Center - pos, wormLife + VoidPortal.CloseTime, 1.1f);
                    int np = NPC.NewNPC(NPC.GetSource_FromAI(), (int)pos.X, (int)pos.Y, ModContent.NPCType<VoidWormlet>(),
                        0, NPC.whoAmI, wormLife, 1000 + k * 12); //+1000 = 脱门模式(§4.2)
                    if (np < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                }
            }
            //只漂移不打(输出窗口沿 P1-1;蠕虫脱门后自治追踪,教皇先走)
            CruiseDrift(target, 0.45f);
            if (attackTimer >= P2WormsTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-2s 三重爆弹:六手两两一组凝 3 弹(充能 120t + 3 轮放射激光)→ 30t 间隔依次掷出———
        private void P2TripleBombAI(Player target)
        {
            if (attackTimer <= TeleTotal)
            {
                return;
            }
            if (attackTimer == P2BombBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.765f + 0.5f); //爆炸 260 经典档(同 P1-2)
                for (int mode = 1; mode <= 3; mode++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(0, -118f), Vector2.Zero,
                        ModContent.ProjectileType<DeathBomb>(), damage, 6f, -1, NPC.whoAmI, 0f, mode);
                }
            }
            //凝弹期定身微漂(读招),掷出后恢复巡航
            if (attackTimer <= P2BombBeat + DeathBomb.ChargeTimeP2 + DeathBomb.ShrinkTime + 60)
            {
                NPC.velocity *= 0.92f;
            }
            else
            {
                CruiseDrift(target, 0.7f);
            }
            if (attackTimer >= P2BombTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-2ss 灯弹复合(<55%):双灯收魂 80t(定身受击 ×1.15)+ 上四手凝 2 弹;收魂末阵雨与投掷同发———
        private void P2LanternBombAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            //2 爆弹(复合模式 4/5:充能 63t,收魂结束瞬间恰好掷出)
            if (t == P2ComboBombBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.765f + 0.5f);
                for (int mode = 4; mode <= 5; mode++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(0, -118f), Vector2.Zero,
                        ModContent.ProjectileType<DeathBomb>(), damage, 6f, -1, NPC.whoAmI, 0f, mode);
                }
            }
            if (t < P2HarvestEnd)
            {
                //收魂:定身;魂魄流演出沿 P1-6(纯客户端)
                NPC.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 spawnPos = RandomScreenEdgePos();
                        Vector2 lantern = NPC.Center + new Vector2(Main.rand.NextBool() ? 150f : -150f, -4f);
                        Vector2 vel = (lantern - spawnPos).SafeNormalize(Vector2.UnitY) * Main.rand.NextFloat(9f, 15f);
                        var p = PRTLoader.NewParticle<PRT_Light>(spawnPos, vel,
                            Main.rand.NextBool() ? new Color(190, 110, 255) : new Color(240, 235, 255), Main.rand.NextFloat(0.35f, 0.6f));
                        p.Configure(0.97f, lifetime: 40);
                    }
                    if (t % 20 == 5)
                    {
                        SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.4f, Pitch = 0.4f + (t - TeleTotal) / 160f }, NPC.Center);
                    }
                }
                return;
            }
            //阵雨(P1-6 后半既有实现):收魂结束瞬间起,与爆弹投掷同时进行(§4.2 压力峰值)
            CruiseDrift(target, 0.3f);
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int k = 0; k < RainWaves; k++)
                {
                    if (t == P2HarvestEnd + k * RainGap)
                    {
                        SpawnGlyphChain(target);
                        break;
                    }
                }
            }
            if (t >= P2LanternTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-3s 万索追猎:追逐 5.5px/t,左右交替齐射(每次 2 条)×3 波 → 第 4 波六索合围留一缺口———
        private void P2ChainStormAI(Player target)
        {
            int t = attackTimer;
            Vector2 want = (target.Center - NPC.Center).SafeNormalize(Vector2.Zero) * HuntSpeed;
            NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.1f);

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                //三波左右交替齐射:该侧下/中两手各刺一条
                for (int w = 0; w < StormWaveBeats.Length; w++)
                {
                    if (t == StormWaveBeats[w])
                    {
                        int side = w % 2 == 0 ? -1 : 1;
                        SpawnHandChainAt(target, side, 0);
                        SpawnHandChainAt(target, side, 1);
                        break;
                    }
                }
                //第 4 波:六索合围(§4.2:以玩家为心六方位,警示 25t,留一缺口 = 只生成 5 条)
                if (t == StormSiegeBeat)
                {
                    int damage = (int)(NPC.defDamage * 0.5f + 0.5f);
                    int gap = Rng(attackCount * 17 + 11).Next(6); //缺口方位 seed 推导
                    Vector2 center = target.Center;
                    for (int k = 0; k < 6; k++)
                    {
                        if (k == gap)
                        {
                            continue;
                        }
                        float ang = -MathHelper.PiOver2 + k * MathHelper.TwoPi / 6f;
                        Vector2 origin = center + ang.ToRotationVector2() * StormSiegeRadius;
                        Vector2 dir = (center - origin).SafeNormalize(Vector2.UnitY);
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, dir * 0.02f,
                            ModContent.ProjectileType<PopeChain>(), damage, 4f, -1, -1f, StormSiegeLength, 2f);
                    }
                }
            }
            if (t >= P2StormTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>从指定 (方向, 层) 的手刺一条铁索(仅服务端;手缺位时从本体侧位出,不挂抓握)。</summary>
        private void SpawnHandChainAt(Player target, int side, int layer)
        {
            int damage = (int)(NPC.defDamage * 0.5f + 0.5f);
            VoidPopeHand hand = FindHand(side, layer);
            Vector2 origin = hand != null ? hand.NPC.Center : NPC.Center + new Vector2(side * 100f, -layer * 30f);
            int sourceIndex = hand != null ? hand.NPC.whoAmI : -1;
            Vector2 dir = (target.Center - origin).SafeNormalize(Vector2.UnitX * side);
            Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, dir * 0.02f,
                ModContent.ProjectileType<PopeChain>(), damage, 4f, -1, sourceIndex, 0f, 0f);
        }

        //———P2-4s 双镰连突:四手凝 2 镰 → 3 次冲刺穿斩(刹车 25t 重瞄)+ 上方法球每 20t 吐 2 追踪魔焰弹 → 环爆 16———
        private void P2TwinScytheAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            if (t < TSCondenseEnd)
            {
                //凝镰:定身蓄势(双镰位 ±55,-80 碎光聚形,与 P1 同款仪式)
                NPC.velocity *= 0.9f;
                if (!Main.dedServ)
                {
                    if (Main.rand.NextBool())
                    {
                        int side = Main.rand.NextBool() ? 1 : -1;
                        Vector2 scythePos = NPC.Center + new Vector2(side * 55f, -80f);
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 170f);
                        var p = PRTLoader.NewParticle<PRT_Light>(scythePos + offset, -offset * 0.1f, new Color(190, 100, 255), 0.5f);
                        p.Configure(0.9f, squishStrenght: 2.8f, lifetime: 13);
                        if (Main.rand.NextBool(4))
                        {
                            PRTLoader.NewParticle<PRT_SparkleCal>(scythePos + CEUtils.randomPointInCircle(60f),
                                Vector2.Zero, new Color(225, 180, 255), 0.6f).Configure(new Color(150, 80, 230), 18);
                        }
                    }
                    if (t % 14 == 5)
                    {
                        float charge = (t - TeleTotal) / 40f;
                        SoundEngine.PlaySound(SoundID.Item15 with { Volume = 0.5f + charge * 0.4f, Pitch = -0.4f + charge * 0.6f }, NPC.Center);
                    }
                }
                return;
            }
            //三次冲刺点火(每次重瞄:路径锁定于点火帧,公平阀)
            foreach (int beat in TSLaunches)
            {
                if (t == beat)
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        NPC.velocity = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX) * ScytheDashSpeed;
                        NPC.netUpdate = true;
                    }
                    if (!Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.DD2_WyvernDiveDown with { Volume = 1.1f, Pitch = -0.3f }, NPC.Center);
                        ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 6), Main.LocalPlayer.Distance(NPC.Center), 1800);
                    }
                }
            }
            //旋斩拍(每冲 2 段,双镰对旋 = 每拍 ±1 两条判定弧)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < TSSlashes.Length; i++)
                {
                    if (t == TSSlashes[i])
                    {
                        float baseAngle = NPC.velocity.Length() > 4f ? NPC.velocity.ToRotation() : (target.Center - NPC.Center).ToRotation();
                        SpawnScytheSlash(1, baseAngle);
                        SpawnScytheSlash(-1, baseAngle + MathHelper.Pi);
                        break;
                    }
                }
                //上方法球:每 20t 吐 2 枚小幅度追踪魔焰弹(§4.2:12px/t,转向 0.015rad/t)
                if (t >= TSCondenseEnd && t < TSBrakes[2] && (t - TSCondenseEnd) % 20 == 0)
                {
                    int damage = (int)(NPC.defDamage * 0.5f + 0.5f); //魔焰弹 170 经典档
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        Vector2 orbPos = NPC.Center + new Vector2(dir * TSOrbOffset.X, TSOrbOffset.Y);
                        Vector2 vel = (target.Center - orbPos).SafeNormalize(Vector2.UnitY) * 12f;
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), orbPos, vel,
                            ModContent.ProjectileType<MagicEyeBolt>(), damage, 3f, -1, 1f);
                    }
                }
                //第 3 刺结束:法球爆裂 16 枚环形魔焰弹(直飞)
                if (t == TSOrbBurst)
                {
                    int damage = (int)(NPC.defDamage * 0.5f + 0.5f);
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        Vector2 orbPos = NPC.Center + new Vector2(dir * TSOrbOffset.X, TSOrbOffset.Y);
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = i * MathHelper.TwoPi / 8f + (dir > 0 ? MathHelper.Pi / 8f : 0f);
                            Projectile.NewProjectile(NPC.GetSource_FromAI(), orbPos, ang.ToRotationVector2() * 10f,
                                ModContent.ProjectileType<MagicEyeBolt>(), damage, 3f, -1, 0f);
                        }
                    }
                }
            }
            if (t == TSOrbBurst && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item27 with { Volume = 1.1f, Pitch = -0.4f }, NPC.Center);
            }
            //冲刺速度线(速度门控,只在真的冲起来时出现)
            SpawnDashSpeedLines();

            //刹车与重瞄段:硬刹 → 缓慢转向目标(下一拍点火重瞄)
            bool braking = false;
            foreach (int brake in TSBrakes)
            {
                if (t >= brake && t < brake + 25)
                {
                    braking = true;
                    break;
                }
            }
            if (braking)
            {
                NPC.velocity *= 0.8f;
            }
            //收招段(第 3 刺刹车后):减速 + 虚影散去(绘制侧按计时渐隐)
            if (t >= TSBrakes[2] + 25)
            {
                NPC.velocity *= 0.92f;
            }
            if (t >= P2ScytheTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-5 维度魔盘·巨激光:瞬移侧 700px → 盘升起 → (跟踪 40t → 锁定 20t → 射击 45t)×2 → 退场———
        private void P2DiscLaserAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            //教皇全程定身微漂(输出窗口:魔盘才是主角)
            NPC.velocity *= 0.92f;

            if (t == DiscRiseBeat)
            {
                EnsureDisc(1);
            }
            else if (t == DiscAim1 || t == DiscAim2)
            {
                discState = 2; //跟踪
            }
            else if (t == DiscLock1 || t == DiscLock2)
            {
                discState = 3; //锁定(§4.2:盘心亮起+咔哒,停止跟踪)
                //激光在锁定帧生成(服务端;方向 = 服务端盘朝向,20t 警示窗与锁定窗对齐)
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    DimensionDisc disc = FindDisc();
                    if (disc != null)
                    {
                        int damage = (int)(NPC.defDamage * 0.765f + 0.5f); //激光 260 经典档
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), disc.Projectile.Center,
                            disc.AimAngle.ToRotationVector2() * 0.02f,
                            ModContent.ProjectileType<DiscBeam>(), damage, 5f, -1);
                    }
                }
            }
            else if (t == DiscFire1 || t == DiscFire2)
            {
                discState = 4; //射击
            }
            else if (t == DiscRetire)
            {
                discState = 5; //退场
            }
            if (t >= P2DiscLaserTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                discState = 0;
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-6 全知之眼:瞬移屏心定身 → 3 眼 120° 均布浮现吐弹;教皇先走,眼再公转 5s 后碎裂———
        private void P2OmniscientAI(Player target)
        {
            int t = attackTimer;
            if (t <= TeleTotal)
            {
                return;
            }
            //定身(§4.2:教皇定身,眼是威胁主体)
            NPC.velocity *= 0.85f;
            if (t == EyeSpawnBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.5f + 0.5f); //眼接触 170 经典档
                for (int k = 0; k < 3; k++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero,
                        ModContent.ProjectileType<OmniscientEye>(), damage, 4f, -1, NPC.whoAmI, k);
                }
            }
            if (t >= P2OmniTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                //教皇恢复行动进下一招;眼自治继续公转 5s(§4.2)
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-7 恶念之爪群:魔盘悬顶 → 3 波 ×7 爪向上扇形抛出 → 悬滞 20t → 分批追踪(波间错拍 15t)———
        private void P2ClawSwarmAI(Player target)
        {
            int t = attackTimer;
            if (t == 1)
            {
                EnsureDisc(6); //悬顶
            }
            CruiseDrift(target, 0.4f);

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int w = 0; w < ClawWaveBeats.Length; w++)
                {
                    if (t == ClawWaveBeats[w])
                    {
                        int damage = (int)(NPC.defDamage * 0.47f + 0.5f); //爪 160 经典档
                        Vector2 origin = discPos != Vector2.Zero ? discPos : NPC.Center + new Vector2(0, -180f);
                        for (int i = 0; i < ClawPerWave; i++)
                        {
                            //向上扇形(±55°),波内微分层
                            float ang = -MathHelper.PiOver2 + (i - (ClawPerWave - 1) * 0.5f) * MathHelper.ToRadians(18);
                            float speed = 8.5f + w * 1.2f;
                            Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, ang.ToRotationVector2() * speed,
                                ModContent.ProjectileType<MaliceClawProj>(), damage, 3f, -1, 20f);
                        }
                        break;
                    }
                }
            }
            if (t == ClawWaveBeats[0] && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item8 with { Volume = 1f, Pitch = -0.2f }, NPC.Center);
            }
            if (t == ClawDiscRetire)
            {
                discState = 5;
            }
            if (t >= P2ClawTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                discState = 0;
                SwitchState(PopeState.Hover);
            }
        }

        //———P2-8 掷盘遁入:掷盘(接触 200)→ 渐隐 1s(判定关)→ 破门衔接镰/索释放段(跳过前摇)———
        private void P2DiscDiveAI(Player target)
        {
            int t = attackTimer;
            if (t == 1)
            {
                EnsureDisc(6); //盘先悬顶 30t(掷出预告)
            }
            if (t < DiveThrowBeat)
            {
                NPC.velocity *= 0.92f;
                return;
            }
            if (t == DiveThrowBeat)
            {
                //掷出:服务端点火盘速度(12px/t 朝玩家,弹幕原生同步)
                discState = 7;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    DimensionDisc disc = FindDisc();
                    if (disc != null)
                    {
                        disc.Projectile.velocity = (target.Center - disc.Projectile.Center).SafeNormalize(Vector2.UnitY) * DimensionDisc.ThrowSpeed;
                        disc.Projectile.netUpdate = true;
                    }
                    NPC.netUpdate = true;
                }
                return;
            }
            if (t < DiveReappear)
            {
                //渐隐悬念拍:定身,判定与受击在 AI 顶部统一关闭(DiveHidden)
                NPC.velocity *= 0.9f;
                //盘接近玩家即减速悬停(服务端切态,§4.2:魔盘在玩家附近减速悬停 = 1s 预告)
                if (Main.netMode != NetmodeID.MultiplayerClient && discState == 7
                    && discPos != Vector2.Zero && Vector2.Distance(discPos, target.Center) < 220f)
                {
                    discState = 8;
                    NPC.netUpdate = true;
                }
                return;
            }
            if (t == DiveReappear)
            {
                //破门(固定拍,无死锁):教皇自盘心现身;爆闪与音效由魔盘凭 discState 跳变双端各自播
                discState = 9; //拍点双端一致,客户端同帧本地写(演出同拍),服务端包兜底
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    if (discPos != Vector2.Zero)
                    {
                        NPC.Center = discPos;
                        NPC.velocity = Vector2.Zero;
                    }
                    //衔接(§4.2:seed 定二选一,跳过其前摇直接进释放段)
                    bool scythe = Rng(attackCount * 23 + 3).Next(2) == 0;
                    lastAttack = (byte)(scythe ? PopeState.P2TwinScythe : PopeState.P2ChainStorm);
                    attackCount++;
                    SwitchState(scythe ? PopeState.P2TwinScythe : PopeState.P2ChainStorm,
                        scythe ? TwinScytheSkipTo : ChainStormSkipTo);
                }
            }
        }

        //———————————————— P3 六招(§4.3)————————————————

        /// <summary>
        /// P3-1 安全区轮转:5 轮 ×(预警 60t + 圈外全域光涌 25t + 换位 10t);
        /// 圈心 = seed + cloneSwapIndex(选招纪元)+ 轮次确定性推导(§4.5:不同步坐标),
        /// 光涌判定 = 领域内且圈外,各端只结算本机玩家(镜像 M5 熵爆写法);第 5 轮圈缩至 140。
        /// </summary>
        private void P3SafeZonesAI(Player target)
        {
            int t = attackTimer;
            HoldAtAnchor();
            if (t < SafeIntro)
            {
                return;
            }
            int round = Math.Min((t - SafeIntro) / SafeRoundLen, SafeRounds - 1);
            int tin = (t - SafeIntro) % SafeRoundLen;
            float circleR = SafeCircleRadius(round);

            //轮起拍:双端同式定圈(不贴圆心;确保整圈都在领域内)
            if (tin == 1)
            {
                var rnd = Rng(cloneSwapIndex * 43 + round * 17 + 901);
                float ang = rnd.NextFloat() * MathHelper.TwoPi;
                float radius = DomainRadius;
                float dist = MathHelper.Clamp(radius * (0.28f + 0.5f * rnd.NextFloat()), 250f, Math.Max(radius - circleR - 80f, 250f));
                safeZonePos = DomainAnchor + ang.ToRotationVector2() * dist;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.9f, Pitch = 0.2f }, safeZonePos);
                }
            }
            if (safeZonePos == Vector2.Zero)
            {
                return; //中途加入:本轮只观望,下一轮起拍接上
            }
            //预警段音调爬升(60t 领域边缘同步脉动在 DrawDomain 里读状态)
            if (tin < SafeWarnLen)
            {
                if (!Main.dedServ && tin % 20 == 10)
                {
                    SoundEngine.PlaySound(SoundID.Item15 with { Volume = 0.6f, Pitch = -0.3f + tin / 90f }, safeZonePos);
                }
                return;
            }
            //光涌段(25t):爆发首拍演出,窗口内各端只结算本机玩家(240 档,i 帧防重复)
            if (tin == SafeWarnLen && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item122 with { Volume = 1.2f, Pitch = -0.35f }, Main.LocalPlayer.Center);
                SoundEngine.PlaySound(SoundID.DD2_KoboldExplosion with { Volume = 0.9f, Pitch = -0.6f }, Main.LocalPlayer.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 9), Main.LocalPlayer.Distance(DomainAnchor), DomainRMax + 600f);
                if (Main.LocalPlayer.Distance(DomainAnchor) < DomainRadius + 300f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.35f;
                }
                PRTLoader.NewParticle<PRT_PulseRing>(DomainAnchor, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(12f, 50);
                for (int i = 0; i < 50; i++)
                {
                    float pang = CEUtils.randomRot();
                    float pdist = Main.rand.NextFloat() * DomainRadius;
                    Vector2 pos = DomainAnchor + pang.ToRotationVector2() * pdist;
                    if (Vector2.Distance(pos, safeZonePos) < circleR)
                    {
                        continue;
                    }
                    var v = PRTLoader.NewParticle<PRT_Void>(pos, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 7f), Color.White, 1f);
                    v.Opacity = Main.rand.Next(30, 90) * 0.01f;
                }
            }
            if (tin < SafeWarnLen + SafeBurstLen && !Main.dedServ)
            {
                Player lp = Main.LocalPlayer;
                if (lp.active && !lp.dead && lp.immuneTime <= 0
                    && Vector2.Distance(lp.Center, DomainAnchor) < DomainRadius
                    && Vector2.Distance(lp.Center, safeZonePos) > circleR)
                {
                    int damage = (int)(NPC.defDamage * (240f / 170f) + 0.5f); //光涌 240 直伤档(§4.3)
                    lp.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPC.whoAmI), damage, 0);
                }
            }
            if (t >= P3SafeTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>本轮安全圈半径(§4.3:常规 170,第 5 轮缩至 140)。</summary>
        private static float SafeCircleRadius(int round)
        {
            return round >= SafeRounds - 1 ? 140f : 170f;
        }

        /// <summary>P3-2 闪电球:背后扇形升起 5 颗可击破 <see cref="LightningOrb"/>,球自治放电/自爆,教皇先走。</summary>
        private void P3LightningOrbsAI(Player target)
        {
            int t = attackTimer;
            HoldAtAnchor();
            if (t == OrbSpawnBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int k = 0; k < 5; k++)
                {
                    int np = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y,
                        ModContent.NPCType<LightningOrb>(), 0, NPC.whoAmI, k);
                    if (np < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                }
            }
            if (t == OrbSpawnBeat && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item121 with { Volume = 1f, Pitch = -0.3f }, NPC.Center);
            }
            //升起段的电花氛围(纯客户端)
            if (!Main.dedServ && t > OrbSpawnBeat && t < OrbSpawnBeat + 40 && Main.rand.NextBool(2))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + CEUtils.randomPointInCircle(180f),
                    new Vector2(0, -Main.rand.NextFloat(1f, 3f)), new Color(185, 140, 255), 0.45f);
                p.Configure(0.88f, lifetime: 14);
            }
            if (t >= P3OrbTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>
        /// P3-3 冲斩扇射(P3 唯一位移):反向蓄势(第 8 次幂晚吸)→ 扑击 8px/t(接触 220)→ 急停
        /// → 双手左右交替横扫 4 次(每扫 20t 前摇,向该侧甩 7 枚扇形魔焰弹直飞版)→ 退回圆心。
        /// 左右左右节奏可背(§4.3);手部横扫编排在 <see cref="VoidPopeHand"/> 凭 SweepBeats 同表推导。
        /// </summary>
        private void P3PounceSweepAI(Player target)
        {
            int t = attackTimer;
            if (t < PounceWindup)
            {
                //反向蓄势:多数时间静止,最后几帧猛然后吸(MOTION 晚吸式反向运动)
                float p = t / (float)PounceWindup;
                Vector2 away = (NPC.Center - target.Center).SafeNormalize(Vector2.UnitX);
                Vector2 reel = DomainAnchor + away * (float)Math.Pow(p, 8) * 130f;
                NPC.velocity = (reel - NPC.Center) * 0.3f;
                return;
            }
            if (t == PounceLaunch)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.velocity = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX) * PounceSpeed;
                    NPC.netUpdate = true;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Roar with { Volume = 1f, Pitch = 0.1f }, NPC.Center);
                    SoundEngine.PlaySound(SoundID.DD2_WyvernDiveDown with { Volume = 1f, Pitch = -0.5f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 7), Main.LocalPlayer.Distance(NPC.Center), 2200);
                }
                return;
            }
            if (t < PounceEnd)
            {
                //扑击巡航:贴近即提前减速(双端各自从同步位置推,近似一致;原生同步兜底)
                if (Vector2.Distance(NPC.Center, target.Center) < 110f)
                {
                    NPC.velocity *= 0.85f;
                }
                return;
            }
            if (t < PounceBrakeEnd)
            {
                NPC.velocity *= 0.72f; //急停(§4.3)
                return;
            }
            //四次交替横扫:左右左右;每扫向该侧甩 7 枚扇形魔焰弹(直飞版,§4.3)
            for (int i = 0; i < SweepBeats.Length; i++)
            {
                if (t != SweepBeats[i])
                {
                    continue;
                }
                int side = i % 2 == 0 ? -1 : 1;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item71 with { Volume = 1.1f, Pitch = -0.25f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 5), Main.LocalPlayer.Distance(NPC.Center), 1800);
                }
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = (int)(NPC.defDamage * 0.5f + 0.5f); //魔焰弹 170 经典档
                    Vector2 origin = NPC.Center + new Vector2(side * 170f, -20f);
                    float baseAng = side > 0 ? 0f : MathHelper.Pi;
                    for (int j = 0; j < 7; j++)
                    {
                        float ang = baseAng + (j - 3) * 0.27f * (side > 0 ? 1f : -1f);
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, ang.ToRotationVector2() * 11f,
                            ModContent.ProjectileType<MagicEyeBolt>(), damage, 3f, -1, 0f);
                    }
                }
                break;
            }
            if (t < SweepReturn)
            {
                NPC.velocity *= 0.9f;
                return;
            }
            //退回圆心(§4.3:扑击后回中心;锚点在扑击期冻结)
            HoldAtAnchor();
            if (t >= P3PounceTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>P3-4 反射激光:胸口细警示线 → 四段反射(几何/节拍/判定全在 <see cref="ReflectLaser"/>);初始方向服务端定夺借初速度通道同步。</summary>
        private void P3ReflectLaserAI(Player target)
        {
            int t = attackTimer;
            HoldAtAnchor();
            //前摇:胸口内聚粒子(纯客户端)
            if (!Main.dedServ && t < ReflectFireBeat && Main.rand.NextBool(2))
            {
                Vector2 chest = NPC.Center + new Vector2(0, -30f);
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 190f);
                var p = PRTLoader.NewParticle<PRT_Light>(chest + offset, -offset * 0.09f, new Color(200, 120, 255), 0.5f);
                p.Configure(0.85f, lifetime: 13);
            }
            if (t == ReflectFireBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.676f + 0.5f); //激光 230 经典档
                Vector2 chest = NPC.Center + new Vector2(0, -30f);
                Vector2 dir = (target.Center - chest).SafeNormalize(Vector2.UnitX);
                Projectile.NewProjectile(NPC.GetSource_FromAI(), chest, dir * 0.02f,
                    ModContent.ProjectileType<ReflectLaser>(), damage, 4f, -1, NPC.whoAmI);
            }
            if (t >= P3ReflectTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>P3-5 双爆弹终曲(&lt;20%):双巨手各凝一枚 110px 巨弹(DeathBomb 模式 6/7,弹自管激光轮/原地引爆/28 枚追踪弹)。</summary>
        private void P3FinalBombsAI(Player target)
        {
            int t = attackTimer;
            HoldAtAnchor();
            if (t == FinalBombBeat && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.765f + 0.5f); //爆炸 260 经典档
                for (int mode = 6; mode <= 7; mode++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(mode == 6 ? -262f : 262f, -96f),
                        Vector2.Zero, ModContent.ProjectileType<DeathBomb>(), damage, 6f, -1, NPC.whoAmI, 0f, mode);
                }
            }
            if (t == FinalBombBeat && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Zombie104 with { Volume = 0.9f, Pitch = -0.6f }, NPC.Center);
            }
            if (t >= P3FinalTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        /// <summary>
        /// P3-6 铁索缚身(每第 4 招强制):领域边缘八方 <see cref="PopeChain"/> 样式 3 反向刺向教皇本体
        /// (2s 内 8 条依次钉入,姿态拉扯倾斜在 AI 尾部旋转段;弹幕全停 = 本状态不再生成任何攻击)
        /// → 缚身 4s:boundTimer 窗口 DamageReduction 归零(P3Upkeep 切换;血条旁发光提示走 EntropyBossbar)
        /// → 挣脱:震颤 → 链崩断 + 无伤冲击环 → 恢复 0.99,回选招。
        /// </summary>
        private void P3ChainBindAI(Player target)
        {
            int t = attackTimer;
            HoldAtAnchor();
            //八方铁索依次钉入(服务端;链的定格时长对齐挣脱拍,崩断由链自演)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int k = 0; k < BindChains; k++)
                {
                    if (t != BindFirstPin + k * BindChainGap)
                    {
                        continue;
                    }
                    float ang = MathHelper.PiOver4 * k + MathHelper.PiOver4 * 0.5f;
                    Vector2 origin = DomainAnchor + ang.ToRotationVector2() * DomainRadius;
                    Vector2 dir = (NPC.Center - origin).SafeNormalize(Vector2.UnitY);
                    float length = Vector2.Distance(origin, NPC.Center) + 30f;
                    int hold = BindBreakBeat - (t + PopeChain.LineWarnTime + PopeChain.ExtendTime);
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, dir * 0.02f,
                        ModContent.ProjectileType<PopeChain>(), 1, 0f, -1, hold, length, 3f);
                    break;
                }
            }
            //缚身起拍(双端各自同拍落 boundTimer,服务端包校正;§4.3:缚身 4s DR 归零)
            if (t == BindPinned)
            {
                boundTimer = BindDuration;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.netUpdate = true;
                    if (NPC.netSpam >= 10)
                        NPC.netSpam = 9;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Unlock with { Volume = 1.2f, Pitch = -0.5f }, NPC.Center);
                    SoundEngine.PlaySound(SoundID.NPCDeath6 with { Volume = 0.8f, Pitch = -0.7f }, NPC.Center);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(255, 220, 140), 0.1f).Configure(5f, 34);
                }
            }
            //缚身窗口的金色火花(可读性:现在能打出真伤)
            if (!Main.dedServ && boundTimer > 0 && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + CEUtils.randomPointInCircle(150f),
                    new Vector2(0, -Main.rand.NextFloat(0.5f, 1.6f)), new Color(255, 220, 150), 0.5f);
                p.Configure(0.88f, lifetime: 16);
            }
            //挣脱拍(§4.3:链崩断 + 无伤冲击环;链的崩断动画由各链同拍自演)
            if (t == BindBreakBeat)
            {
                boundTimer = 0;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Roar with { Volume = 1.2f, Pitch = -0.1f }, NPC.Center);
                    SoundEngine.PlaySound(SoundID.Item27 with { Volume = 1.1f, Pitch = -0.3f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 10), Main.LocalPlayer.Distance(NPC.Center), 2400);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(9f, 46);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(6f, 36);
                    for (int i = 0; i < 30; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 10f), Color.White, 1f);
                        v.Opacity = Main.rand.Next(30, 90) * 0.01f;
                    }
                }
            }
            if (t >= P3BindTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(PopeState.Hover);
            }
        }

        //———————————————— 死亡演出(§4.4,5s,镜像 AbyssalWraith deathAnm 骨架)————————————————

        /// <summary>
        /// 死亡演出:攻击停止/清弹幕(EnterDeath 已做)→ 领域停止收缩(life 恒 1)并龟裂
        /// → 侧结构、双巨手逐个坠落碎裂(间隔 40t;手侧凭状态同拍自演,服务端限时 despawn)
        /// → bodyP3 自顶向下像素扫描消散(AWDeath 技法)→ 白闪定格 → 领域向外爆碎
        /// → StrikeInstantKill(掉落与旗标走 OnKill)。自损干死与玩家打死都走这条;
        /// 期间玩家环绕失效(HandleDomainWrap 见 P3Death 即停用)。
        /// </summary>
        private void P3DeathAI()
        {
            attackTimer++;
            int t = attackTimer;
            NPC.dontTakeDamage = true;
            NPC.damage = 0;
            boundTimer = 0;
            NPC.velocity *= 0.9f;
            NPC.rotation *= 0.95f;

            if (t == 1 && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath6 with { Volume = 1.2f, Pitch = -0.8f }, NPC.Center);
            }
            //领域龟裂(§4.4:圆周 GlassBreak 尘 + 裂纹音;领域没展开过就无环可裂)
            if (!Main.dedServ && t >= DeathCrackStart && t < DeathBurst && domainRadiusFactor > 0.05f)
            {
                if (t % 3 == 0)
                {
                    float ang = CEUtils.randomRot();
                    Vector2 pos = DomainAnchor + ang.ToRotationVector2() * (DomainRadius + Main.rand.NextFloat(-14f, 14f));
                    Dust.NewDust(pos, 1, 1, ModContent.DustType<Dusts.GlassBreak>());
                }
                if (t % 40 == 20)
                {
                    SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.9f, Pitch = -0.55f + t * 0.001f }, Main.LocalPlayer.Center);
                }
            }
            //加速节拍(演出二迭:间隔递缩、音高爬升的报警音,坍缩在倒数)
            if (!Main.dedServ && t <= DeathScanStart)
            {
                for (int i = 0; i < DeathBeepBeats.Length; i++)
                {
                    if (t == DeathBeepBeats[i])
                    {
                        SoundEngine.PlaySound(SoundID.MaxMana with { Volume = 0.75f, Pitch = -0.35f + i * 0.09f }, NPC.Center);
                        break;
                    }
                }
            }
            //部件坠落拍(§4.4:间隔 40t;肩 L/R 转本地坠落件,手 L/R 由手侧凭同拍自演)
            for (int k = 0; k < 2; k++)
            {
                if (t == DeathPieceBeats[k] && !Main.dedServ)
                {
                    SpawnFallingShoulder(k);
                    SoundEngine.PlaySound(SoundID.NPCHit2 with { Volume = 1f, Pitch = -0.5f }, NPC.Center);
                }
            }
            //像素扫描消散(§4.4:AWDeath 技法,bodyP3 逐行取色撒尘;VaultLoaden 服务器判空防护)
            Texture2D deathTex = Main.dedServ ? null : bodyP3Tex.Value;
            if (t >= DeathScanStart && t < DeathScanStart + DeathScanLen)
            {
                deathPer = MathHelper.Clamp((t - DeathScanStart) / (float)DeathScanLen, 0f, 1f - 0.005f);
                if (!Main.dedServ && deathTex != null)
                {
                    if (p3PixelData == null)
                    {
                        p3PixelData = new Color[deathTex.Width * deathTex.Height];
                        deathTex.GetData(p3PixelData);
                    }
                    int row = (int)(deathTex.Height * deathPer);
                    for (int i = 0; i < deathTex.Width; i += 6)
                    {
                        int index = row * deathTex.Width + i;
                        if (index >= 0 && index < p3PixelData.Length && p3PixelData[index].A != 0)
                        {
                            Dust.NewDust(NPC.Center + (-deathTex.Size() / 2 + new Vector2(i, deathTex.Height * deathPer)) * NPC.scale,
                                1, 1, ModContent.DustType<Dusts.AwDeath>());
                        }
                    }
                }
            }
            else if (t >= DeathScanStart + DeathScanLen)
            {
                deathPer = 1f;
            }
            //白闪定格(§4.4:2 帧全白,EModSys 衰减自然给出 ~0.4s 渐退)
            if (t == DeathWhiteFlash && !Main.dedServ)
            {
                if (Main.LocalPlayer.Distance(NPC.Center) < 3200f)
                {
                    CalamityEntropy.FlashEffectStrength = 1f;
                }
                SoundEngine.PlaySound(SoundID.Item122 with { Volume = 1.2f, Pitch = -0.7f }, NPC.Center);
            }
            //领域向外爆碎(§4.4:震屏 + 圆周玻璃尘暴 + 冲击环)
            if (t == DeathBurst && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Shatter with { Volume = 1.3f, Pitch = -0.4f }, Main.LocalPlayer.Center);
                SoundEngine.PlaySound(SoundID.Roar with { Volume = 1f, Pitch = -0.5f }, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 16), Main.LocalPlayer.Distance(NPC.Center), 3200);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(14f, 60);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(10f, 50);
                if (domainRadiusFactor > 0.05f)
                {
                    for (int i = 0; i < 72; i++)
                    {
                        float ang = i * MathHelper.TwoPi / 72f;
                        Vector2 pos = DomainAnchor + ang.ToRotationVector2() * DomainRadius;
                        Dust.NewDust(pos, 1, 1, ModContent.DustType<Dusts.GlassBreak>());
                        if (i % 3 == 0)
                        {
                            var v = PRTLoader.NewParticle<PRT_Void>(pos, ang.ToRotationVector2() * Main.rand.NextFloat(3f, 12f), Color.White, 1f);
                            v.Opacity = Main.rand.Next(30, 90) * 0.01f;
                        }
                    }
                }
                //全屏径向速度线(演出二迭:领域爆碎的空间被向外撕开)
                for (int i = 0; i < 30; i++)
                {
                    float ang = CEUtils.randomRot();
                    Vector2 pos = NPC.Center + ang.ToRotationVector2() * Main.rand.NextFloat(60f, 260f);
                    var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(pos, ang.ToRotationVector2() * Main.rand.NextFloat(16f, 34f),
                        Main.rand.NextBool() ? Color.White : new Color(200, 130, 255), Main.rand.NextFloat(0.8f, 1.4f));
                    s.Configure(false, 22, new Vector2(3.4f, 0.4f), quickShrink: true);
                }
            }
            //爆碎后领域快速收场(视觉)
            if (t > DeathBurst)
            {
                domainRadiusFactor = Math.Max(domainRadiusFactor - 1f / 8f, 0f);
            }
            //真死(服务端;OnKill 落旗标,掉落 M9)
            if (t >= P3DeathTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.StrikeInstantKill();
                NPC.netSpam = 9;
                NPC.netUpdate = true;
            }
        }

        /// <summary>肩位侧结构转本地坠落件(§4.4:逐个坠落碎裂;纯客户端)。</summary>
        private void SpawnFallingShoulder(int texId)
        {
            int dir = texId == 0 ? -1 : 1;
            fallingPieces.Add(new FallingPiece
            {
                texId = texId,
                pos = NPC.Center + new Vector2(dir * 252f, -168f) * NPC.scale,
                vel = new Vector2(dir * Main.rand.NextFloat(0.5f, 1.6f), Main.rand.NextFloat(-1.5f, 0f)),
                rot = 0f,
                rotVel = dir * Main.rand.NextFloat(0.03f, 0.07f),
                life = 1f,
            });
        }

        /// <summary>坠落部件推进(客户端:重力 + 自旋,落速 50t 后碎裂成玻璃尘)。</summary>
        private void UpdateFallingPieces()
        {
            if (Main.dedServ || fallingPieces.Count == 0)
            {
                return;
            }
            for (int i = fallingPieces.Count - 1; i >= 0; i--)
            {
                FallingPiece f = fallingPieces[i];
                f.vel.Y += 0.32f;
                f.pos += f.vel;
                f.rot += f.rotVel;
                f.life -= 1f / 50f;
                if (f.life <= 0f)
                {
                    for (int d = 0; d < 10; d++)
                    {
                        Dust.NewDust(f.pos + CEUtils.randomPointInCircle(30f), 1, 1, ModContent.DustType<Dusts.GlassBreak>());
                    }
                    SoundEngine.PlaySound(SoundID.Shatter with { Volume = 0.7f, Pitch = -0.2f }, f.pos);
                    fallingPieces.RemoveAt(i);
                }
                else
                {
                    fallingPieces[i] = f;
                }
            }
        }

        private static Vector2 RandomScreenEdgePos()
        {
            Vector2 tl = Main.screenPosition;
            float w = Main.screenWidth;
            float h = Main.screenHeight;
            return Main.rand.Next(4) switch
            {
                0 => tl + new Vector2(Main.rand.NextFloat(w), -30f),
                1 => tl + new Vector2(Main.rand.NextFloat(w), h + 30f),
                2 => tl + new Vector2(-30f, Main.rand.NextFloat(h)),
                _ => tl + new Vector2(w + 30f, Main.rand.NextFloat(h)),
            };
        }

        //———程序化组装绘制———

        /// <summary>P1 翼展开度(换阶羽影爆散后为 0);P2 翼展开度(§4.2:弹性展开 0→1 过冲回弹)。</summary>
        private (float p1Wing, float p2Wing, float p2Body) TransitionBlend
        {
            get
            {
                if (phase >= 2 && transitionTimer <= 0)
                {
                    return (0f, 1f, 1f);
                }
                //P2→P3 换阶(phase 仍为 2):P2 装配保持满形态,消隐由 P3Blend.p2Fade 承载
                if (phase >= 2)
                {
                    return (0f, 1f, 1f);
                }
                int t = transitionTimer;
                if (t <= 0 || t < TransFeatherBurst)
                {
                    return (1f, 0f, 0f);
                }
                //躯体交叉渐变(50~80)
                float body = MathHelper.Clamp((t - TransFeatherBurst) / (float)(TransBodyFadeEnd - TransFeatherBurst), 0f, 1f);
                //二阶段翼弹性展开(80~130:elastic ease-out,过冲 ~1.14 回弹)
                float wing = 0f;
                if (t > TransBodyFadeEnd)
                {
                    float p = MathHelper.Clamp((t - TransBodyFadeEnd) / (float)(TransWingEnd - TransBodyFadeEnd), 0f, 1f);
                    wing = p >= 1f ? 1f : 1f - (float)(Math.Pow(2, -10 * p) * Math.Cos(p * 9.4f));
                }
                return (0f, wing, body);
            }
        }

        /// <summary>
        /// P3 揭示进度(§4.3 换阶演出):p3Body = bodyP3 渐显拼合,shoulders = 侧结构飞入,
        /// p2Fade = 二阶段躯体过曝碎裂后的快速消隐。phase 3 稳态 = (1,1,0)。
        /// </summary>
        private (float p3Body, float shoulders, float p2Fade) P3Blend
        {
            get
            {
                if (phase >= 3 && transitionTimer <= 0)
                {
                    return (1f, 1f, 0f);
                }
                if (phase == 2 && transitionTimer > 0)
                {
                    int t = transitionTimer;
                    float p2Fade = t < TransP3Shatter ? 1f : MathHelper.Clamp(1f - (t - TransP3Shatter) / 10f, 0f, 1f);
                    float p3Body = MathHelper.Clamp((t - TransP3Shatter - 5) / (float)(TransP3BodyEnd - TransP3Shatter - 5), 0f, 1f);
                    float shoulders = MathHelper.Clamp((t - TransP3HandGone) / (float)(TransP3ShoulderEnd - TransP3HandGone), 0f, 1f);
                    return (p3Body, shoulders, p2Fade);
                }
                return (0f, 0f, 1f);
            }
        }

        /// <summary>翼 + 躯体(+P2 飘带)一次装配(真身与分身共用);换阶期做交叉渐变与翼弹性展开;P3 走结晶装配分支。</summary>
        private void DrawPopeAssembly(SpriteBatch sb, Vector2 screenPos, Vector2 center, Color drawColor, float alpha)
        {
            if (alpha <= 0.01f)
            {
                return;
            }
            //———P3 结晶装配(§4.3/§4.5:bodyP3 缓慢自旋 ±4°,侧结构肩位轨道微漂)———
            (float p3Body, float p3Shoulders, float p2FadeOut) = P3Blend;
            if (p3Body > 0.01f || (phase == 2 && transitionTimer > 0))
            {
                //换阶期残余的 P2 装配(过曝碎裂拍前照常,拍后 10t 内消隐)
                if (p2FadeOut > 0.01f && p3Body < 1f)
                {
                    DrawP2LegacyAssembly(sb, screenPos, center, drawColor, alpha * p2FadeOut);
                }
                if (p3Body > 0.01f)
                {
                    DrawP3Assembly(sb, screenPos, center, drawColor, alpha, p3Body, p3Shoulders);
                }
                return;
            }
            DrawP2LegacyAssembly(sb, screenPos, center, drawColor, alpha);
        }

        /// <summary>P1/P2 装配本体(原 DrawPopeAssembly 主体,P3 揭示前的形态)。</summary>
        private void DrawP2LegacyAssembly(SpriteBatch sb, Vector2 screenPos, Vector2 center, Color drawColor, float alpha)
        {
            if (alpha <= 0.01f)
            {
                return;
            }
            (float p1Wing, float p2Wing, float p2Body) = TransitionBlend;
            Texture2D bodyP1 = TextureAssets.Npc[NPC.type].Value;

            float flap = (float)Math.Sin(flapCounter) * MathHelper.ToRadians(14);
            float squashX = 1f - 0.16f * (0.5f + 0.5f * (float)Math.Cos(flapCounter));

            //P1 翼(§4.5:躯体后层,扑动 = 旋转 ±14° + 横向压缩,周期 50t;源图为左翼,右翼水平镜像)
            if (p1Wing > 0.01f)
            {
                Texture2D wing = wingTex.Value;
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    Vector2 anchor = center + new Vector2(dir * 36f, -58f).RotatedBy(NPC.rotation);
                    Vector2 origin = dir == -1 ? new Vector2(wing.Width * 0.85f, wing.Height * 0.22f) : new Vector2(wing.Width * 0.15f, wing.Height * 0.22f);
                    SpriteEffects fx = dir == -1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                    float rot = NPC.rotation + dir * flap;
                    sb.Draw(wing, anchor - screenPos, null, drawColor * (alpha * p1Wing), rot, origin, new Vector2(squashX, 1f) * NPC.scale, fx, 0);
                }
            }
            //P2 翼(§4.2/§4.5:扑动参数沿用;换阶期弹性展开 0→1 过冲回弹)
            if (p2Wing > 0.01f)
            {
                Texture2D wing = wingP2Tex.Value;
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    Vector2 anchor = center + new Vector2(dir * 40f, -70f).RotatedBy(NPC.rotation);
                    Vector2 origin = dir == -1 ? new Vector2(wing.Width * 0.85f, wing.Height * 0.25f) : new Vector2(wing.Width * 0.15f, wing.Height * 0.25f);
                    SpriteEffects fx = dir == -1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                    float rot = NPC.rotation + dir * flap;
                    sb.Draw(wing, anchor - screenPos, null, drawColor * (alpha * Math.Min(p2Wing, 1f)),
                        rot, origin, new Vector2(squashX * p2Wing, p2Wing) * NPC.scale, fx, 0);
                }
            }

            //P2 飘带(§4.5:躯体两侧 ×2,顶点锚定、末端滞后二段摆,布感;翼前躯体后)
            if (p2Body > 0.5f)
            {
                DrawRibbons(sb, screenPos, center, drawColor, alpha);
            }

            //躯体(呼吸浮动 ±3px sin,§4.5;换阶期 P1→P2 交叉渐变 30t)
            float breathe = (float)Math.Sin(flapCounter * 0.5f) * 3f;
            Vector2 bodyPos = center + new Vector2(0, breathe) - screenPos;
            if (p2Body < 0.99f)
            {
                sb.Draw(bodyP1, bodyPos, null, drawColor * (alpha * (1f - p2Body)),
                    NPC.rotation, bodyP1.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            }
            if (p2Body > 0.01f)
            {
                Texture2D bodyP2 = bodyP2Tex.Value;
                sb.Draw(bodyP2, bodyPos, null, drawColor * (alpha * p2Body),
                    NPC.rotation, bodyP2.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            }
        }

        /// <summary>
        /// 飘带 ×2(§4.5):顶点锚在躯体两侧,上下两段各取源图一半;
        /// 下段相位滞后 0.9rad 且摆幅稍大(末端滞后的布感),随横向速度整体后甩。
        /// </summary>
        private void DrawRibbons(SpriteBatch sb, Vector2 screenPos, Vector2 center, Color drawColor, float alpha)
        {
            Texture2D ribbon = ribbonTex.Value;
            int halfH = ribbon.Height / 2;
            Rectangle upperSrc = new Rectangle(0, 0, ribbon.Width, halfH);
            Rectangle lowerSrc = new Rectangle(0, halfH, ribbon.Width, ribbon.Height - halfH);
            float drag = MathHelper.Clamp(-NPC.velocity.X * 0.012f, -0.35f, 0.35f);
            for (int dir = -1; dir <= 1; dir += 2)
            {
                float sway1 = (float)Math.Sin(flapCounter * 0.6f + dir * 0.5f) * 0.14f + drag;
                float sway2 = (float)Math.Sin(flapCounter * 0.6f + dir * 0.5f - 0.9f) * 0.2f + drag * 1.5f;
                Vector2 anchor = center + new Vector2(dir * 58f, -36f).RotatedBy(NPC.rotation);
                //上段:顶点锚定,rotation = 主摆(源图竖直下垂)
                sb.Draw(ribbon, anchor - screenPos, upperSrc, drawColor * (alpha * 0.95f),
                    NPC.rotation + sway1, new Vector2(ribbon.Width / 2f, 2f), NPC.scale, SpriteEffects.None, 0);
                //下段:锚在上段末端,rotation = 主摆 + 滞后摆
                Vector2 upperEnd = anchor + new Vector2(0f, (halfH - 4) * NPC.scale).RotatedBy(NPC.rotation + sway1);
                sb.Draw(ribbon, upperEnd - screenPos, lowerSrc, drawColor * (alpha * 0.95f),
                    NPC.rotation + sway1 + sway2, new Vector2(ribbon.Width / 2f, 2f), NPC.scale, SpriteEffects.None, 0);
            }
        }

        /// <summary>换阶羽影绘制(§4.2:一阶段翼爆散 20 片,自旋飘落渐隐)。</summary>
        private void DrawFeathers(SpriteBatch sb, Vector2 screenPos, Color drawColor)
        {
            if (feathers.Count == 0)
            {
                return;
            }
            Texture2D tex = featherTex.Value;
            foreach (FeatherFx f in feathers)
            {
                sb.Draw(tex, f.pos - screenPos, null, drawColor * (0.9f * f.life), f.rot, tex.Size() / 2,
                    0.9f, SpriteEffects.None, 0);
            }
        }

        /// <summary>
        /// P3 结晶装配(§4.3/§4.5):bodyP3 缓慢自旋 ±4°(NPC.rotation 已承载)+ 呼吸浮动,
        /// 侧结构肩位悬浮轨道微漂;换阶揭示期 = 渐显 + 双侧碎片幽影内聚拼合,侧结构自两侧远端飞入;
        /// 死亡演出扫描期 = 自顶向下源矩形裁切(AWDeath 同款),扫描行上叠亮光。
        /// </summary>
        private void DrawP3Assembly(SpriteBatch sb, Vector2 screenPos, Vector2 center, Color drawColor, float alpha, float reveal, float shoulders)
        {
            Texture2D body = bodyP3Tex.Value;
            float breathe = (float)Math.Sin(flapCounter * 0.5f) * 3f;
            Vector2 bodyPos = center + new Vector2(0, breathe);

            //侧结构(先画,压在主体后;§4.4 坠落拍之后不再挂肩)
            for (int k = 0; k < 2; k++)
            {
                if (State == PopeState.P3Death && attackTimer >= DeathPieceBeats[k])
                {
                    continue; //已转坠落件
                }
                int dir = k == 0 ? -1 : 1;
                Texture2D shoulder = k == 0 ? shoulderLTex.Value : shoulderRTex.Value;
                float sp = MathHelper.Clamp(shoulders, 0f, 1f);
                if (sp <= 0.01f)
                {
                    continue;
                }
                float ease = 1f - (1f - sp) * (1f - sp);
                //轨道微漂(§4.3:肩位悬浮)
                Vector2 drift = new Vector2((float)Math.Cos(flapCounter * 0.5f + k * 2.4f), (float)Math.Sin(flapCounter * 0.65f + k * 1.7f)) * 10f;
                Vector2 anchorPos = center + new Vector2(dir * 252f, -168f) * NPC.scale + drift;
                Vector2 fromPos = center + new Vector2(dir * 860f, -320f);
                Vector2 pos = Vector2.Lerp(fromPos, anchorPos, ease);
                float rot = NPC.rotation * 0.6f + (1f - ease) * dir * 0.8f;
                sb.Draw(shoulder, pos - screenPos, null, drawColor * (alpha * sp), rot, shoulder.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            }

            //死亡演出扫描期:源矩形裁切绘制(镜像 AbyssalWraith 死亡绘制姿势)
            if (State == PopeState.P3Death && attackTimer >= DeathScanStart)
            {
                float per = MathHelper.Clamp(deathPer, 0f, 1f);
                if (per >= 0.995f)
                {
                    return; //扫描完成,主体已尽数化尘
                }
                Rectangle src = new Rectangle(0, (int)(body.Height * per), body.Width, (int)(body.Height * (1 - per)));
                //扫描期残躯微颤(被逐行拆解的挣扎感,纯视觉)
                Vector2 scanJitter = CEUtils.randomPointInCircle(2.2f);
                Vector2 drawPos = center + scanJitter - screenPos + new Vector2(0, body.Height / 2f * per * NPC.scale);
                Vector2 origin = new Vector2(body.Width / 2f, body.Height * (1 - per) / 2f);
                sb.Draw(body, drawPos, src, Color.White, 0f, origin, NPC.scale, SpriteEffects.None, 0);
                //扫描行亮光
                sb.UseAdditive();
                Texture2D pixel = TextureAssets.MagicPixel.Value;
                Vector2 linePos = center - screenPos + new Vector2(0, (-body.Height / 2f + body.Height * per) * NPC.scale);
                sb.Draw(pixel, linePos, new Rectangle(0, 0, 1, 1), Color.White * 0.85f, 0f,
                    new Vector2(0.5f), new Vector2(body.Width * NPC.scale, 3f), SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
                return;
            }

            //揭示期:双侧碎片幽影内聚(§4.3:自碎片中拼合)
            if (reveal < 0.99f)
            {
                float gap = (1f - reveal) * 52f;
                sb.Draw(body, bodyPos + new Vector2(-gap, -gap * 0.4f) - screenPos, null, drawColor * (alpha * reveal * 0.4f),
                    NPC.rotation - (1f - reveal) * 0.12f, body.Size() / 2, NPC.scale, SpriteEffects.None, 0);
                sb.Draw(body, bodyPos + new Vector2(gap, gap * 0.4f) - screenPos, null, drawColor * (alpha * reveal * 0.4f),
                    NPC.rotation + (1f - reveal) * 0.12f, body.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            }
            //主体(死亡演出前段过曝发白在 DrawStateOverlays 叠加;倒数期微颤随节拍加剧)
            if (State == PopeState.P3Death && attackTimer < DeathScanStart)
            {
                bodyPos += CEUtils.randomPointInCircle(attackTimer / (float)DeathScanStart * 2.4f);
            }
            sb.Draw(body, bodyPos - screenPos, null, drawColor * (alpha * reveal), NPC.rotation, body.Size() / 2, NPC.scale, SpriteEffects.None, 0);
        }

        /// <summary>死亡演出坠落部件绘制(§4.4:侧结构翻滚坠落渐隐)。</summary>
        private void DrawFallingPieces(SpriteBatch sb, Vector2 screenPos, Color drawColor)
        {
            if (fallingPieces.Count == 0)
            {
                return;
            }
            foreach (FallingPiece f in fallingPieces)
            {
                Texture2D tex = f.texId == 0 ? shoulderLTex.Value : shoulderRTex.Value;
                sb.Draw(tex, f.pos - screenPos, null, drawColor * (0.95f * f.life), f.rot, tex.Size() / 2,
                    NPC.scale, SpriteEffects.None, 0);
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            float bodyAlpha = BodyAlpha;

            //领域边界(§4.3:先画环带,压在一切部件之下)
            if (DomainVisible)
            {
                DrawDomain(spriteBatch, screenPos);
            }

            //冲刺拖尾(速度门控;贴图随相位)
            if (NPC.velocity.Length() > 18f && odp.Count > 1)
            {
                Texture2D body = phase >= 3 ? bodyP3Tex.Value : (phase >= 2 ? bodyP2Tex.Value : TextureAssets.Npc[NPC.type].Value);
                for (int i = 0; i < odp.Count - 1; i++)
                {
                    float fade = (i + 1f) / odp.Count * 0.35f;
                    spriteBatch.Draw(body, odp[i] - screenPos, null, new Color(130, 70, 255) * (fade * bodyAlpha),
                        NPC.rotation, body.Size() / 2, NPC.scale, SpriteEffects.None, 0);
                }
            }

            //三位一体/分身之眼分身(纯绘制,0.8 alpha;真身另有魂火冠区分)
            float cloneAlpha = CloneAlpha;
            if (cloneAlpha > 0.01f && NPC.HasValidTarget)
            {
                Vector2 tc = Main.player[NPC.target].Center;
                for (int k = 0; k < 2; k++)
                {
                    DrawPopeAssembly(spriteBatch, screenPos, ClonePos(tc, k), drawColor, cloneAlpha);
                }
            }

            //真身(遁入隐身段 alpha 归零 = body 不绘制,§4.2 P2-8)
            DrawPopeAssembly(spriteBatch, screenPos, NPC.Center, drawColor, bodyAlpha);

            //换阶羽影(爆散后自旋飘落)、P2→P3 结晶碎片与死亡演出坠落部件
            DrawFeathers(spriteBatch, screenPos, drawColor);
            DrawShards(spriteBatch, screenPos);
            DrawFallingPieces(spriteBatch, screenPos, drawColor);

            DrawStateOverlays(spriteBatch, screenPos);
            return false;
        }

        /// <summary>
        /// 领域边界视觉(§4.3;演出二迭升格):EnablePixelEffect 开 = PopeDomainRing 着色器环带
        /// (裂隙纹理双向流动 + 亮丝裂纹 + 白芯读线 + 内壁泛光/外侧沉暗的双色域分界);
        /// 关 = 原顶点软边环带退化路线,可读性不缺。圆周旋转小法阵 ×10 两路共用;
        /// 粒子环由 P3Upkeep 常燃补充。安全区轮转预警窗领域边缘同步脉动(§4.3 P3-1);
        /// 死亡演出龟裂期裂纹闪白。
        /// </summary>
        private void DrawDomain(SpriteBatch sb, Vector2 screenPos)
        {
            float radius = DomainRadius;
            if (radius < 30f)
            {
                return;
            }
            Vector2 center = DomainAnchor;

            //状态调制:P3-1 预警窗脉动 / 死亡龟裂闪烁
            float pulse = 0f;
            if (State == PopeState.P3SafeZones && attackTimer >= SafeIntro)
            {
                int tin = (attackTimer - SafeIntro) % SafeRoundLen;
                if (tin < SafeWarnLen)
                {
                    pulse = 0.35f * (0.5f + 0.5f * (float)Math.Sin(tin * 0.35f));
                }
            }
            bool deathCracking = State == PopeState.P3Death;
            float baseAlpha = 0.5f + pulse;
            Color edgeColor = new Color(150, 70, 240);
            Color coreColor = new Color(220, 170, 255);
            if (deathCracking)
            {
                float flicker = 0.5f + 0.5f * (float)Math.Sin(attackTimer * 0.7f);
                edgeColor = Color.Lerp(edgeColor, Color.White, 0.4f * flicker);
                baseAlpha *= 0.85f + 0.3f * flicker;
            }

            bool fancyRing = Terraria.ModLoader.ModContent.GetInstance<Config>().EnablePixelEffect;
            sb.End();
            if (fancyRing)
            {
                //———着色器环带(演出二迭:PopeDomainRing 裂隙纹理流动 + 亮丝裂纹 + 内外双色域)———
                Effect ringFx = Core.Graphics.CEFxcEffects.Get("PopeDomainRing");
                sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, ringFx, Main.GameViewMatrix.TransformationMatrix);
                float halfSize = radius + 130f;
                ringFx.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly);
                ringFx.Parameters["uOpacity"]?.SetValue(baseAlpha * 1.15f);
                ringFx.Parameters["uRadius"]?.SetValue(radius / halfSize);
                ringFx.Parameters["uThick"]?.SetValue(42f / halfSize);
                ringFx.Parameters["uPulse"]?.SetValue(pulse * 2.2f);
                ringFx.Parameters["uCrackFlash"]?.SetValue(deathCracking ? 0.5f + 0.5f * (float)Math.Sin(attackTimer * 0.7f) : 0f);
                ringFx.Parameters["uColorEdge"]?.SetValue(edgeColor.ToVector3());
                ringFx.Parameters["uColorCore"]?.SetValue(coreColor.ToVector3());
                ringFx.Parameters["uColorIn"]?.SetValue(new Color(215, 150, 255).ToVector3());
                Texture2D ringNoise = CEExtraAssets.TurbulentNoise;
                sb.Draw(ringNoise, center - screenPos, null, Color.White, 0f, ringNoise.Size() / 2, halfSize * 2f / ringNoise.Width, SpriteEffects.None, 0);
                sb.End();
                sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }
            else
            {
                //———退化路线(EnablePixelEffect 关闭):原顶点软边环带,可读性完整保留———
                sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                GraphicsDevice gd = Main.graphics.GraphicsDevice;

                const int segs = 96;
                float scroll = flapCounter * 0.12f;
                //双层软边环带:内缘带(R-40→R)+ 外缘带(R→R+40),径向两端渐隐,合成 80px 软墙
                for (int band = 0; band < 2; band++)
                {
                    float r0 = band == 0 ? radius - 40f : radius;
                    float r1 = band == 0 ? radius : radius + 40f;
                    Color c0 = band == 0 ? Color.Transparent : edgeColor * baseAlpha;
                    Color c1 = band == 0 ? edgeColor * baseAlpha : Color.Transparent;
                    List<ColoredVertex> ve = new List<ColoredVertex>();
                    for (int i = 0; i <= segs; i++)
                    {
                        float ang = i * MathHelper.TwoPi / segs;
                        Vector2 dir = ang.ToRotationVector2();
                        float u = i * 6f / segs + scroll;
                        ve.Add(new ColoredVertex(center + dir * r0 - screenPos, new Vector3(u, 0, 1), c0));
                        ve.Add(new ColoredVertex(center + dir * r1 - screenPos, new Vector3(u, 1, 1), c1));
                    }
                    gd.Textures[0] = CEExtraAssets.white;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
                }
                //亮芯细环(边界读线:玩家一眼知道墙在哪)
                {
                    List<ColoredVertex> ve = new List<ColoredVertex>();
                    for (int i = 0; i <= segs; i++)
                    {
                        float ang = i * MathHelper.TwoPi / segs;
                        Vector2 dir = ang.ToRotationVector2();
                        float u = i * 6f / segs - scroll * 0.7f;
                        ve.Add(new ColoredVertex(center + dir * (radius - 5f) - screenPos, new Vector3(u, 0, 1), coreColor * (baseAlpha * 0.9f)));
                        ve.Add(new ColoredVertex(center + dir * (radius + 5f) - screenPos, new Vector3(u, 1, 1), coreColor * (baseAlpha * 0.9f)));
                    }
                    gd.Textures[0] = CEExtraAssets.white;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
                }
                //内侧微光带(§4.3:内壁泛光,R-170→R-40 渐入)
                {
                    List<ColoredVertex> ve = new List<ColoredVertex>();
                    for (int i = 0; i <= segs; i++)
                    {
                        float ang = i * MathHelper.TwoPi / segs;
                        Vector2 dir = ang.ToRotationVector2();
                        float u = i * 3f / segs + scroll * 0.4f;
                        ve.Add(new ColoredVertex(center + dir * (radius - 170f) - screenPos, new Vector3(u, 0, 1), Color.Transparent));
                        ve.Add(new ColoredVertex(center + dir * (radius - 40f) - screenPos, new Vector3(u, 1, 1), edgeColor * (0.12f + pulse * 0.3f)));
                    }
                    gd.Textures[0] = CEExtraAssets.white;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
                }
            }

            //圆周旋转小法阵(退化方案的主可读件之一,常开)
            Texture2D glyph = glyphTex.Value;
            for (int i = 0; i < 10; i++)
            {
                float ang = i * MathHelper.TwoPi / 10f + flapCounter * 0.045f;
                Vector2 pos = center + ang.ToRotationVector2() * radius - screenPos;
                sb.Draw(glyph, pos, null, edgeColor * (baseAlpha * 0.8f), flapCounter * 0.4f + i, glyph.Size() / 2, 0.3f, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <summary>
        /// 状态叠加层(加法批次):瞬移预告法阵、魔镰虚影(P1 单/P2 双)、法球虚影、收魂发光标记;
        /// M8 增:P2→P3 过曝碎裂前的白化、P3-1 安全圈(预警渐亮 + 光涌泛白)、
        /// 缚身金色发光(DR 归零的输出窗口标记)、死亡演出前段过曝发白。
        /// </summary>
        private void DrawStateOverlays(SpriteBatch sb, Vector2 screenPos)
        {
            int teleTimer = transitionTimer > 0 ? transitionTimer : attackTimer;
            bool teleGlyph = InTeleport && teleTimer <= TeleWarn + TeleFadeOut && teleTarget != Vector2.Zero;
            bool scythe = State == PopeState.ScytheDash && attackTimer > TeleTotal && transitionTimer <= 0;
            bool twinScythe = State == PopeState.P2TwinScythe && attackTimer > TeleTotal && transitionTimer <= 0;
            bool harvestGlow = State == PopeState.SoulLanterns && attackTimer > TeleTotal && attackTimer <= HarvestEnd && transitionTimer <= 0;
            bool harvestGlowP2 = State == PopeState.P2LanternBomb && attackTimer > TeleTotal && attackTimer <= P2HarvestEnd && transitionTimer <= 0;
            bool transP3Overexpose = phase == 2 && transitionTimer > TeleTotal && transitionTimer <= TransP3Shatter + 8;
            bool safeZone = State == PopeState.P3SafeZones && transitionTimer <= 0 && safeZonePos != Vector2.Zero && attackTimer >= SafeIntro;
            bool bindGlow = boundTimer > 0 && phase >= 3;
            bool deathGlow = State == PopeState.P3Death && attackTimer < DeathScanStart + DeathScanLen;
            if (!teleGlyph && !scythe && !twinScythe && !harvestGlow && !harvestGlowP2
                && !transP3Overexpose && !safeZone && !bindGlow && !deathGlow)
            {
                return;
            }
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            if (transP3Overexpose)
            {
                //二阶段躯体过曝(§4.3:碎裂前渐白到炸);末 8t 预坍缩——东西先变小再变响(MOTION §6)
                Texture2D bodyP2 = bodyP2Tex.Value;
                float p = MathHelper.Clamp((transitionTimer - TeleTotal) / (float)(TransP3Shatter - TeleTotal), 0f, 1f);
                float scaleMod = 1f + p * 0.05f;
                int toShatter = TransP3Shatter - transitionTimer;
                if (toShatter > 0 && toShatter <= 8)
                {
                    float c = (8 - toShatter) / 8f;
                    scaleMod -= 0.10f * c;
                    scaleMod += 0.02f * (float)Math.Cos(transitionTimer * 1.9f) * c;
                }
                sb.Draw(bodyP2, NPC.Center - screenPos, null, Color.White * (p * 0.95f), NPC.rotation, bodyP2.Size() / 2, NPC.scale * scaleMod, SpriteEffects.None, 0);
            }

            if (safeZone)
            {
                DrawSafeZoneOverlay(sb, screenPos);
            }

            if (bindGlow)
            {
                //缚身金光(§4.3 P3-6:输出窗口一眼可读;随剩余窗口收束脉动加急)
                Texture2D body = bodyP3Tex.Value;
                float urgency = 1f - boundTimer / (float)BindDuration;
                float pulse = 0.22f + 0.16f * (float)Math.Sin(attackTimer * (0.18f + urgency * 0.2f));
                sb.Draw(body, NPC.Center - screenPos, null, new Color(255, 220, 140) * pulse, NPC.rotation, body.Size() / 2, NPC.scale * 1.02f, SpriteEffects.None, 0);
            }

            if (deathGlow)
            {
                //死亡演出前段:主体过曝发白渐强(§4.4)
                Texture2D body = bodyP3Tex.Value;
                float p = MathHelper.Clamp(attackTimer / (float)DeathScanStart, 0f, 1f);
                float per = MathHelper.Clamp(deathPer, 0f, 1f);
                if (per < 0.995f)
                {
                    Rectangle src = new Rectangle(0, (int)(body.Height * per), body.Width, (int)(body.Height * (1 - per)));
                    Vector2 drawPos = NPC.Center - screenPos + new Vector2(0, body.Height / 2f * per * NPC.scale);
                    Vector2 origin = new Vector2(body.Width / 2f, body.Height * (1 - per) / 2f);
                    sb.Draw(body, drawPos, src, Color.White * (0.75f * p), 0f, origin, NPC.scale, SpriteEffects.None, 0);
                }
            }

            if (teleGlyph)
            {
                //瞬移预告(§4.0:玩家永远先看到"他要去哪"):法阵渐大渐亮,双层反旋
                Texture2D glyph = glyphTex.Value;
                float p = MathHelper.Clamp(teleTimer / (float)TeleWarn, 0f, 1f);
                float spin = teleTimer * 0.06f;
                Vector2 pos = teleTarget - screenPos;
                sb.Draw(glyph, pos, null, new Color(190, 100, 255) * (0.35f + 0.55f * p), spin, glyph.Size() / 2, 0.45f + 0.45f * p, SpriteEffects.None, 0);
                sb.Draw(glyph, pos, null, Color.White * (0.3f * p), -spin * 0.7f, glyph.Size() / 2, 0.3f + 0.32f * p, SpriteEffects.None, 0);
                //收束线(演出二迭):空间被"拧"向落点——五条切向内旋细线随预告收拢
                Texture2D lineTex = CEExtraAssets.vlbw;
                for (int i = 0; i < 5; i++)
                {
                    float ang = i * MathHelper.TwoPi / 5f + teleTimer * 0.1f + i * 1.7f;
                    float rOut = MathHelper.Lerp(300f, 48f, p) * (0.82f + 0.18f * (i % 2));
                    Vector2 outer = teleTarget + ang.ToRotationVector2() * rOut;
                    float lineRot = (teleTarget - outer).ToRotation() + 0.55f;
                    sb.Draw(lineTex, outer - screenPos, null, new Color(190, 110, 255) * (0.18f + 0.5f * p), lineRot,
                        lineTex.Size() / 2 * new Vector2(0, 1), new Vector2(rOut * 0.62f / lineTex.Width, 0.13f), SpriteEffects.None, 0);
                }
            }

            if (scythe)
            {
                //P1-4 单镰:旋向与判定弧同表(97:+1 / 111:-1 / 146:+1)
                DrawScythePhantomAt(sb, screenPos, new Vector2(0, -90f),
                    new[] { ScytheSlash1, ScytheSlash2, ScytheSlash3 }, new[] { 1, -1, 1 }, ScytheTotal);
            }

            if (twinScythe)
            {
                //P2-4s 双镰对旋(§4.2):左镰正旋,右镰反旋,拍表同判定
                DrawScythePhantomAt(sb, screenPos, new Vector2(-55f, -80f), TSSlashes, new[] { 1, 1, 1, 1, 1, 1 }, P2ScytheTotal);
                DrawScythePhantomAt(sb, screenPos, new Vector2(55f, -80f), TSSlashes, new[] { -1, -1, -1, -1, -1, -1 }, P2ScytheTotal);
                DrawTwinOrbs(sb, screenPos);
            }

            if (harvestGlow || harvestGlowP2)
            {
                //收魂发光标记(§4.1 P1-6 / §4.2 P2-2ss:明示输出窗口)
                Texture2D body = phase >= 2 ? bodyP2Tex.Value : TextureAssets.Npc[NPC.type].Value;
                float pulse = 0.3f + 0.18f * (float)Math.Sin(attackTimer * 0.25f);
                sb.Draw(body, NPC.Center - screenPos, null, new Color(220, 180, 255) * pulse, NPC.rotation, body.Size() / 2, NPC.scale * 1.03f, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <summary>
        /// P3-1 安全圈叠加(§4.3,加法批次内):安全圈 = VoidGlyph 亮光 + 双层圈环渐亮(预警 60t),
        /// 光涌窗 = 圈外全域泛白(读作"只有圈里活");判定与视觉同源(同一 safeZonePos/半径)。
        /// </summary>
        private void DrawSafeZoneOverlay(SpriteBatch sb, Vector2 screenPos)
        {
            int round = Math.Min((attackTimer - SafeIntro) / SafeRoundLen, SafeRounds - 1);
            int tin = (attackTimer - SafeIntro) % SafeRoundLen;
            float circleR = SafeCircleRadius(round);
            Vector2 pos = safeZonePos - screenPos;
            Texture2D glyph = glyphTex.Value;
            Texture2D ring = CEExtraAssets.BloomRing;

            if (tin < SafeWarnLen)
            {
                //预警:法阵亮光渐大渐亮 + 圈环收拢(从 1.35 倍收到 1 倍,催促入圈)
                float p = tin / (float)SafeWarnLen;
                float spin = attackTimer * 0.05f;
                sb.Draw(glyph, pos, null, new Color(200, 160, 255) * (0.3f + 0.6f * p), spin, glyph.Size() / 2,
                    circleR * 2f / glyph.Width * (0.7f + 0.3f * p), SpriteEffects.None, 0);
                sb.Draw(glyph, pos, null, Color.White * (0.28f * p), -spin * 0.7f, glyph.Size() / 2,
                    circleR * 1.5f / glyph.Width, SpriteEffects.None, 0);
                float shrink = MathHelper.Lerp(1.35f, 1f, p);
                sb.Draw(ring, pos, null, new Color(255, 240, 200) * (0.35f + 0.5f * p), 0, ring.Size() / 2,
                    circleR * 2f / ring.Width * shrink, SpriteEffects.None, 0);
                //圣光竖幕(演出二迭:金白细光柱自圈内升起,与全场虚空紫形成圣域对比)
                Texture2D shaft = CEExtraAssets.vlbw;
                for (int i = 0; i < 3; i++)
                {
                    float xoff = (float)Math.Sin(attackTimer * 0.02f + i * 2.4f) * circleR * 0.5f;
                    float shaftA = (0.16f + 0.16f * (float)Math.Sin(attackTimer * 0.06f + i * 1.9f)) * p;
                    sb.Draw(shaft, pos + new Vector2(xoff, circleR * 0.85f), null, new Color(255, 242, 195) * shaftA,
                        -MathHelper.PiOver2, shaft.Size() / 2 * new Vector2(0, 1),
                        new Vector2(circleR * 2.1f / shaft.Width, 0.35f), SpriteEffects.None, 0);
                }
                return;
            }
            if (tin < SafeWarnLen + SafeBurstLen + 8)
            {
                //光涌:圈外泛白(以领域为心的大光斑,加法下读作"全场爆发"),安全圈保持亮环白芯(可读的幸存区)
                float bp = MathHelper.Clamp((tin - SafeWarnLen) / (float)SafeBurstLen, 0f, 1f);
                float flood = (1f - bp) * 0.55f;
                sb.Draw(ring, DomainAnchor - screenPos, null, new Color(230, 200, 255) * flood, 0, ring.Size() / 2,
                    DomainRadius * 2.6f / ring.Width, SpriteEffects.None, 0);
                sb.Draw(ring, pos, null, Color.White * (0.85f - 0.5f * bp), 0, ring.Size() / 2,
                    circleR * 2f / ring.Width, SpriteEffects.None, 0);
                sb.Draw(glyph, pos, null, new Color(200, 160, 255) * (0.7f - 0.5f * bp), attackTimer * 0.05f, glyph.Size() / 2,
                    circleR * 2f / glyph.Width, SpriteEffects.None, 0);
            }
        }

        /// <summary>
        /// 魔镰虚影(§4.1 P1-4 / §4.2 P2-4s):凝聚段 0→1.6 放大渐实(紫光),
        /// 旋斩窗口 12t 快转一圈(与判定弧同表),收招段(末 30t)散去。
        /// 判定在 <see cref="Projectiles.VoidInvasion.PopeScytheSlash"/>,这里只是演出。
        /// </summary>
        private void DrawScythePhantomAt(SpriteBatch sb, Vector2 screenPos, Vector2 offset, int[] slashBeats, int[] slashDirs, int totalEnd)
        {
            int t = attackTimer;
            Texture2D scythe = scytheTex.Value;
            float scale;
            float alpha;
            float gripFlash = 0f;
            int condenseEnd = TeleTotal + 40;
            if (t <= condenseEnd)
            {
                float p = MathHelper.Clamp((t - TeleTotal) / 40f, 0f, 1f);
                float ease = 1f - (1f - p) * (1f - p);
                scale = 1.6f * ease;
                alpha = 0.9f * ease;
            }
            else if (t < totalEnd - 30)
            {
                scale = 1.6f;
                alpha = 0.9f;
                //凝聚完成的"握定"拍(演出二迭):5t 过冲回稳 + 白闪一帧,仪式收在一个响指上
                if (t - condenseEnd < 5)
                {
                    float snapP = (t - condenseEnd) / 5f;
                    scale = 1.6f + 0.14f * (1f - snapP);
                    gripFlash = 1f - snapP;
                }
            }
            else
            {
                float p = (t - (totalEnd - 30)) / 30f;
                scale = 1.6f + p * 0.4f;
                alpha = 0.9f * (1f - p);
            }
            if (alpha <= 0.01f)
            {
                return;
            }
            Vector2 pos = NPC.Center + offset - screenPos;
            //凝聚仪式法阵(演出二迭:碎光聚形的地台,凝聚段渐显,握定后淡去)
            if (t > TeleTotal && t < condenseEnd + 20)
            {
                Texture2D glyph = glyphTex.Value;
                float gp = MathHelper.Clamp((t - TeleTotal) / 40f, 0f, 1f);
                float gFade = t > condenseEnd ? MathHelper.Clamp(1f - (t - condenseEnd) / 20f, 0f, 1f) : 1f;
                sb.Draw(glyph, pos, null, new Color(190, 110, 255) * (0.55f * gp * gFade), t * 0.045f,
                    glyph.Size() / 2, 0.5f + 0.25f * gp, SpriteEffects.None, 0);
            }
            //旋斩窗口:12t 快转一圈,旋向与判定弧同表;其余时间缓慢自旋
            float rot = t * 0.05f;
            for (int i = 0; i < slashBeats.Length; i++)
            {
                if (t >= slashBeats[i] && t < slashBeats[i] + PopeScytheSlash.SweepTime)
                {
                    float p = (t - slashBeats[i]) / (float)PopeScytheSlash.SweepTime;
                    rot += slashDirs[i] * MathHelper.TwoPi * (1f - (float)Math.Pow(1f - p, 5));
                }
            }
            sb.Draw(scythe, pos, null, new Color(190, 100, 255) * alpha, rot, scythe.Size() / 2, scale, SpriteEffects.None, 0);
            sb.Draw(scythe, pos, null, Color.White * (alpha * 0.55f), rot, scythe.Size() / 2, scale * 0.98f, SpriteEffects.None, 0);
            if (gripFlash > 0f)
            {
                sb.Draw(scythe, pos, null, Color.White * (0.9f * gripFlash), rot, scythe.Size() / 2, scale * 1.02f, SpriteEffects.None, 0);
            }
        }

        /// <summary>P2-4s 法球虚影(§4.2:上方两手举法球,魔眼弹贴图 ×2 放大,脉动;吐弹与环爆的可见源头)。</summary>
        private void DrawTwinOrbs(SpriteBatch sb, Vector2 screenPos)
        {
            int t = attackTimer;
            //环爆(245)后炸散消失
            float alpha = t >= TSOrbBurst ? MathHelper.Clamp(1f - (t - TSOrbBurst) / 12f, 0f, 1f) : 0.95f;
            //起手渐显(凝镰段同步)
            alpha *= MathHelper.Clamp((t - TeleTotal) / 40f, 0f, 1f);
            if (alpha <= 0.01f)
            {
                return;
            }
            Texture2D orb = eyeOrbTex.Value;
            float pulse = 1f + 0.12f * (float)Math.Sin(t * 0.3f);
            float burstGrow = t >= TSOrbBurst ? 1f + (t - TSOrbBurst) * 0.06f : 1f;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                Vector2 pos = NPC.Center + new Vector2(dir * TSOrbOffset.X, TSOrbOffset.Y) - screenPos;
                sb.Draw(orb, pos, null, new Color(200, 110, 255) * alpha, t * 0.04f * dir, orb.Size() / 2, 2f * pulse * burstGrow, SpriteEffects.None, 0);
                sb.Draw(orb, pos, null, Color.White * (alpha * 0.5f), -t * 0.03f * dir, orb.Size() / 2, 1.3f * pulse * burstGrow, SpriteEffects.None, 0);
            }
        }
    }
}
