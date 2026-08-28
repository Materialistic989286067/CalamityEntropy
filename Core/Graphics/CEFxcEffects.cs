using Microsoft.Xna.Framework.Graphics;
using System.Collections.Concurrent;
using Terraria;

namespace CalamityEntropy.Core.Graphics
{
    /// <summary>
    /// 运行时加载 Assets/Effects 下由 CompileFX.ps1 产出的 .fxc 着色器(fxc.exe / fx_2_0 档)。
    /// 与旧的 .fx+.xnb(EasyXnb)链路并存:新着色器一律走本类,老 69 个 .xnb 不迁移。
    /// 用法:CEFxcEffects.Get("PopeChainLink") — 带缓存;服务器返回 null;只在绘制路径调用。
    /// </summary>
    public class CEFxcEffects : ICELoader
    {
        private static readonly ConcurrentDictionary<string, Effect> cache = new();

        /// <summary>按文件基名取 Effect;失败会抛出(缺 .fxc 属于打包/编译问题,不该被吞)</summary>
        public static Effect Get(string name)
        {
            if (Main.dedServ)
            {
                return null;
            }
            return cache.GetOrAdd(name, static n =>
            {
                byte[] bytes = CalamityEntropy.Instance.GetFileBytes("Assets/Effects/" + n + ".fxc");
                return new Effect(Main.graphics.GraphicsDevice, bytes);
            });
        }

        void ICELoader.UnLoadData()
        {
            foreach (Effect e in cache.Values)
            {
                e?.Dispose();
            }
            cache.Clear();
        }
    }
}
