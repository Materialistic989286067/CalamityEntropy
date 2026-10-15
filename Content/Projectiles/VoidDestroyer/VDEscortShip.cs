using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using VoidDestroyerNPC = CalamityEntropy.Content.NPCs.VoidDestroyer.VoidDestroyer;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 轰炸掠袭的护航幻影舰(纯演出,无伤害):弹性跟随本体 + 编队偏移 (ai[1], ai[2]),缩成本体的 0.8 倍。
    /// ai[0] 本体。本体不在掠袭状态时淡出消失;炸弹由状态从它当下的位置代投
    /// </summary>
    public class VDEscortShip : VDDepthProjectile
    {
        [InnoVault.VaultLoaden("CalamityEntropy/Content/NPCs/VoidDestroyer/VoidDestroyerP2")]
        private static Asset<Texture2D> p2Tex = null;

        public const float ShipScale = 0.8f;

        public override string Texture => "CalamityEntropy/Content/NPCs/VoidDestroyer/VoidDestroyer";
        public override int DefaultTimeLeft => 400;

        private float fade;

        private VoidDestroyerNPC Owner {
            get {
                int idx = (int)Projectile.ai[0];
                if (idx < 0 || idx >= Main.maxNPCs || !Main.npc[idx].active || Main.npc[idx].ModNPC is not VoidDestroyerNPC boss) {
                    return null;
                }
                return boss;
            }
        }

        public override void SetExtraDefaults() {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.hostile = false;
        }

        public override bool? CanDamage() => false;
        public override bool ShouldUpdatePosition() => false;

        protected override void DepthAI() {
            VoidDestroyerNPC boss = Owner;
            bool alive = boss != null && boss.CurrentStateIndex == VDStateIndex.BombingRun && !boss.Dying;
            fade = MathHelper.Clamp(fade + (alive ? 0.08f : -0.1f), 0f, 1f);
            if (!alive && fade <= 0f) {
                Projectile.Kill();
                return;
            }
            if (boss != null) {
                Vector2 want = boss.NPC.Center + new Vector2(Projectile.ai[1], Projectile.ai[2]);
                //编队用弹性跟随,队形留一点滞后,看起来才像三艘船
                Vector2 before = Projectile.Center;
                Projectile.Center = Vector2.Lerp(Projectile.Center, want, 0.25f);
                float vx = Projectile.Center.X - before.X;
                Projectile.rotation = MathHelper.Lerp(Projectile.rotation, MathHelper.Clamp(vx * 0.012f, -0.25f, 0.25f), 0.2f);
            }
            if (!Main.dedServ && fade > 0.5f && Main.rand.NextBool(3)) {
                Vector2 pos = Projectile.Center + new Vector2(0f, 26f);
                VDVfx.Spark(pos, new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), Main.rand.NextFloat(1f, 2.5f)), VDVfx.VoidPink, 0.6f, 0.8f, 14);
            }
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            VoidDestroyerNPC boss = Owner;
            Texture2D tex = boss != null && boss.Phase >= 2 && p2Tex != null ? p2Tex.Value : TextureAssets.Projectile[Type].Value;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            float alpha = 0.85f * fade;
            if (alpha <= 0.01f) {
                return;
            }
            spriteBatch.UseAdditive();
            Texture2D glow = CEUtils.getExtraTex("Glow");
            spriteBatch.Draw(glow, pos, null, VDVfx.VoidPurple * (0.35f * alpha), 0f, glow.Size() / 2f, 0.9f * ShipScale, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
            VDHologramDraw.Draw(tex, pos, null, VDVfx.VoidPurple, alpha, Projectile.rotation, tex.Size() / 2f, ShipScale, SpriteEffects.None);
        }
    }
}
