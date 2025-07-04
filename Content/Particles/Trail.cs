﻿using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Particles
{
    public class TrailParticle : EParticle
    {
        public List<Vector2> odp = new List<Vector2>();
        public override Texture2D Texture => ModContent.Request<Texture2D>("CalamityEntropy/Content/Particles/Trail").Value;
        public override void OnSpawn()
        {
            this.Lifetime = 13;
        }
        public int maxLength = 64;
        public override void AI()
        {
            base.AI();
        }

        public void AddPoint(Vector2 pos)
        {
            odp.Insert(0, pos);
            if (odp.Count > maxLength)
            {
                odp.RemoveAt(odp.Count - 1);
            }
        }

        public override void Draw()
        {
            if (odp.Count < 3)
            {
                return;
            }
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            List<ColoredVertex> ve = new List<ColoredVertex>();
            Color b = this.Color * ((float)this.Lifetime / 12f);
            ve.Add(new ColoredVertex(odp[0] - Main.screenPosition + (odp[1] - odp[0]).ToRotation().ToRotationVector2().RotatedBy(MathHelper.ToRadians(90)) * 12 * this.Scale,
                      new Vector3((((float)0) / odp.Count), 1, 1),
                      b));
            ve.Add(new ColoredVertex(odp[0] - Main.screenPosition + (odp[1] - odp[0]).ToRotation().ToRotationVector2().RotatedBy(MathHelper.ToRadians(-90)) * 12 * this.Scale,
                  new Vector3((((float)0) / odp.Count), 0, 1),
                  b));
            for (int i = 1; i < odp.Count; i++)
            {
                ve.Add(new ColoredVertex(odp[i] - Main.screenPosition + (odp[i] - odp[i - 1]).ToRotation().ToRotationVector2().RotatedBy(MathHelper.ToRadians(90)) * 12 * this.Scale,
                      new Vector3((((float)i) / odp.Count), 1, 1),
                      b * ((odp.Count - i) / (float)odp.Count)));
                ve.Add(new ColoredVertex(odp[i] - Main.screenPosition + (odp[i] - odp[i - 1]).ToRotation().ToRotationVector2().RotatedBy(MathHelper.ToRadians(-90)) * 12 * this.Scale,
                      new Vector3((((float)i) / odp.Count), 0, 1),
                      b * ((odp.Count - i) / (float)odp.Count)));
            }
            SpriteBatch sb = Main.spriteBatch;
            GraphicsDevice gd = Main.graphics.GraphicsDevice;
            if (ve.Count >= 3)
            {
                gd.Textures[0] = this.Texture;
                gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, ve.ToArray(), 0, ve.Count - 2);
            }
        }
    }
}
