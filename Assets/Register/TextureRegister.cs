using InnoVault;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace CalamityEntropy.Assets.Register
{
    // 原自动属性一并改为普通静态字段,避免私有 setter 反射赋值的不确定性。
    // 注意:专用服务器上字段恒为 null,只能在绘制等客户端路径读取。
    public class TextureRegister : ModSystem
    {
        //轨迹
        [VaultLoaden("CalamityEntropy/Assets/Extra/Slash_Wrap")]
        public static Asset<Texture2D> Trail_SlashWrap;
        [VaultLoaden("CalamityEntropy/Assets/DoubleLineTrail")]
        public static Asset<Texture2D> Trail_DoubleLine;
        //原注册器从未给它赋值,保持无标签、恒为 null 的原状
        public static Asset<Texture2D> Trail_MotionTrail1;
        [VaultLoaden("CalamityEntropy/Assets/MotionTrail2")]
        public static Asset<Texture2D> Trail_MotionTrail2;
        [VaultLoaden("CalamityEntropy/Assets/MotionTrail3")]
        public static Asset<Texture2D> Trail_MotionTrail3;
        [VaultLoaden("CalamityEntropy/Assets/MotionTrail4")]
        public static Asset<Texture2D> Trail_MotionTrail4;

        //噪声
        [VaultLoaden("CalamityEntropy/Assets/MiscNoise01")]
        public static Asset<Texture2D> Noise_Misc1;
        [VaultLoaden("CalamityEntropy/Assets/MiscNoise02")]
        public static Asset<Texture2D> Noise_Misc2;

        //通用形状
        [VaultLoaden("CalamityEntropy/Assets/Extra/ShinyOrbParticle")]
        public static Asset<Texture2D> General_WhiteOrb;
        [VaultLoaden("CalamityEntropy/Assets/Extra/WhiteCube")]
        public static Asset<Texture2D> General_WhiteCube;
    }
}
