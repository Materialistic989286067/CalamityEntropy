using InnoVault;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace CalamityEntropy.Content.Particles
{
    /// <summary>
    /// 禁止在 PreDraw 里 ModContent.Request；下面按粒子族分组，加贴图先找对应组
    /// 原先指向灾厄的跨模组入口已全部换成 Assets/Particles 下的自制同名贴图
    /// </summary>
    internal static class PRTSharedAssets
    {
        //本模组粒子贴图,Texture属性能直接认CalamityEntropy/...路径,PreDraw也可走这拿
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light2")]
        internal static Asset<Texture2D> PRT_Light2;

        [VaultLoaden("CalamityEntropy/Content/Particles/StarTrail")]
        internal static Asset<Texture2D> StarTrail;

        [VaultLoaden("CalamityEntropy/Content/Particles/AntivoidTrail")]
        internal static Asset<Texture2D> AntivoidTrail;

        [VaultLoaden("CalamityEntropy/Content/Particles/DashBeam")]
        internal static Asset<Texture2D> DashBeam;

        [VaultLoaden("CalamityEntropy/Content/Particles/UpdraftParticle")]
        internal static Asset<Texture2D> UpdraftParticle;

        [VaultLoaden("CalamityEntropy/Content/Particles/ShadeDashParticle")]
        internal static Asset<Texture2D> ShadeDashParticle;

        [VaultLoaden("CalamityEntropy/Content/Particles/LargeSpark")]
        internal static Asset<Texture2D> LargeSpark;

        [VaultLoaden("CalamityEntropy/Content/Particles/Trail")]
        internal static Asset<Texture2D> Trail;

        [VaultLoaden("CalamityEntropy/Content/Particles/Smoke")]
        internal static Asset<Texture2D> Smoke;

        [VaultLoaden("CalamityEntropy/Content/Particles/Wind")]
        internal static Asset<Texture2D> Wind;

        //Effect shader,ProminenceTrail/ShadeDashParticle/Trail PreDraw里用;Vortex 已收编进 CEEffectAssets

        [VaultLoaden("CalamityEntropy/Assets/Effects/Prominence", AssetMode.Effects, "EffectPass")]
        internal static Asset<Effect> Prominence;

        [VaultLoaden("CalamityEntropy/Assets/Effects/ShadeDashParticle", AssetMode.Effects, "EffectPass")]
        internal static Asset<Effect> ShadeDashParticleShader;

        //下面这批是Assets/Particles的自制贴图,尺寸帧数对齐灾厄原图,同名换库
        //Bloom光晕,CustomPulse/BloomCal/SparkleCal/CritSparkCal叠层
        [VaultLoaden("CalamityEntropy/Assets/Particles/BloomCircle")]
        internal static Asset<Texture2D> BloomCircle;

        //Spark/GlowSpark系,VoidSparkCal/CustomSpark/GlowSparkCal/AltSpark/SparkCal
        [VaultLoaden("CalamityEntropy/Assets/Particles/GlowSpark")]
        internal static Asset<Texture2D> GlowSpark;

        [VaultLoaden("CalamityEntropy/Assets/Particles/GlowSpark2")]
        internal static Asset<Texture2D> GlowSpark2;

        [VaultLoaden("CalamityEntropy/Assets/Particles/ThinSparkle")]
        internal static Asset<Texture2D> ThinSparkle;

        [VaultLoaden("CalamityEntropy/Assets/Particles/Sparkle2")]
        internal static Asset<Texture2D> Sparkle2;

        [VaultLoaden("CalamityEntropy/Assets/Particles/StarProj")]
        internal static Asset<Texture2D> StarProj;

        [VaultLoaden("CalamityEntropy/Assets/Particles/MammothParticle")]
        internal static Asset<Texture2D> MammothParticle;   //天顶周二彩蛋,CustomPulse会用到,别删

        //Trail streak,PRT_TrailGunShot三角带用
        [VaultLoaden("CalamityEntropy/Assets/Particles/BasicTrail")]
        internal static Asset<Texture2D> BasicTrail;

        //HeavySmoke/Mist烟雾,HeavySmokeCal/MediumMistCal
        [VaultLoaden("CalamityEntropy/Assets/Particles/HeavySmoke")]
        internal static Asset<Texture2D> HeavySmoke;

        [VaultLoaden("CalamityEntropy/Assets/Particles/MediumMist")]
        internal static Asset<Texture2D> MediumMist;

        //Line/Drain线型,LineCal/AltLineCal
        [VaultLoaden("CalamityEntropy/Assets/Particles/DrainLineBloom")]
        internal static Asset<Texture2D> DrainLineBloom;

        [VaultLoaden("CalamityEntropy/Assets/Particles/DrainLine")]
        internal static Asset<Texture2D> DrainLine;

        //Pulse环,PulseRing/DirectionalPulseRing
        [VaultLoaden("CalamityEntropy/Assets/Particles/HollowCircleHardEdge")]
        internal static Asset<Texture2D> HollowCircleHardEdge;

        //Explosion,DetailedExplosionCal/PlasmaExplosionCal
        [VaultLoaden("CalamityEntropy/Assets/Particles/DetailedExplosion")]
        internal static Asset<Texture2D> DetailedExplosion;

        [VaultLoaden("CalamityEntropy/Assets/Particles/PlasmaExplosion")]
        internal static Asset<Texture2D> PlasmaExplosion;

        //Flame,FlameCal
        [VaultLoaden("CalamityEntropy/Assets/Particles/Flames")]
        internal static Asset<Texture2D> Flames;

        //Holosquare,护盾/科技方块VFX,TechyHolosquare
        [VaultLoaden("CalamityEntropy/Assets/Particles/TechyHolosquare")]
        internal static Asset<Texture2D> TechyHolosquare;

        //Point/Orb/Square光点基元,PointCal/GlowOrbCal/GlowSquare系
        [VaultLoaden("CalamityEntropy/Assets/Particles/PointParticle")]
        internal static Asset<Texture2D> PointParticle;

        [VaultLoaden("CalamityEntropy/Assets/Particles/GlowSquareParticle")]
        internal static Asset<Texture2D> GlowSquareParticle;

        [VaultLoaden("CalamityEntropy/Assets/Particles/GlowOrbParticle")]
        internal static Asset<Texture2D> GlowOrbParticle;

        //Blood粒子
        [VaultLoaden("CalamityEntropy/Assets/Particles/Blood")]
        internal static Asset<Texture2D> Blood;
    }
}

