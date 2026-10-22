using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>原版饰品与材料在微光中的双向互换</summary>
    public class VanillaShimmerSwaps : ModSystem
    {
        public override void PostSetupContent() {
            Link(ItemID.PutridScent, ItemID.FleshKnuckles);
            Link(ItemID.Ichor, ItemID.CursedFlame);
            Link(ItemID.WormScarf, ItemID.BrainOfConfusion);
        }

        private static void Link(int a, int b) {
            ItemID.Sets.ShimmerTransformToItem[a] = b;
            ItemID.Sets.ShimmerTransformToItem[b] = a;
        }
    }
}
