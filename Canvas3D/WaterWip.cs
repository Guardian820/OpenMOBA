﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Canvas3D.LowLevel;
using SharpDX;
using SharpDX.Direct3D;

namespace Canvas3D {
   using TIndex = Int32;

   public class WaterWip {
      public const int GridResolution = 1024;//128;
      public const float GridSpacing = 0.01f;

      private readonly IGraphicsFacade graphicsFacade;
      private readonly IGraphicsDevice device;
      private IBuffer<VertexPositionNormalColorTexture> vertexBuffer;
      private IBuffer<TIndex> indexBuffer;
      private int indexCount;

      public WaterWip(IGraphicsFacade graphicsFacade) {
         this.graphicsFacade = graphicsFacade;
         this.device = graphicsFacade.Device;
      }

      public void Initialize() {
         var (vertices, indices) = GenerateBufferData();
         vertexBuffer = device.CreateVertexBuffer(vertices);
         indexBuffer = device.CreateIndexBuffer(indices);
         indexCount = indices.Length;
      }

      public void Render(IDeviceContext context) {
         context.SetRasterizerConfiguration(RasterizerConfiguration.FillFrontBack);
         context.SetVertexBuffer(0, vertexBuffer);
         context.SetIndexBuffer(0, indexBuffer);
         context.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
         context.DrawIndexedInstanced(indexCount, 1, 0, 0, 0);
      }

      private static (VertexPositionNormalColorTexture[] vertices, TIndex[] indices) GenerateBufferData() {
         // convention is X -> y ^ and Z to sky
         // So first verts are at bottom left.
         // front-face is CW
         var vertices = new VertexPositionNormalColorTexture[GridResolution * GridResolution];
         var i = 0;
         for (var y = 0; y < GridResolution; y++) {
            for (var x = 0; x < GridResolution; x++) {
               vertices[i] = new VertexPositionNormalColorTexture(
                  new Vector3(x * GridSpacing, y * GridSpacing, 0),
                  new Vector3(0, 0, 1),
                  Color.White,
                  new Vector2(x / (float)(GridResolution - 1), y / (float)(GridResolution - 1)));
               i++;
            }
         }
         Trace.Assert(i == vertices.Length);

         var ntriangles = (GridResolution - 1) * (GridResolution - 1) * 2;
         var indices = new TIndex[ntriangles * 3];
         i = 0;
         for (var y = 0; y < GridResolution - 1; y++) {
            for (var x = 0; x < GridResolution - 1; x++) {
               var bl = checked((TIndex)(GridResolution * y + x));
               var br = checked((TIndex)(GridResolution * y + (x + 1)));
               var tl = checked((TIndex)(GridResolution * (y + 1) + x));
               var tr = checked((TIndex)(GridResolution * (y + 1) + (x + 1)));

               indices[i++] = tl;
               indices[i++] = tr;
               indices[i++] = bl;

               indices[i++] = bl;
               indices[i++] = tr;
               indices[i++] = br;
            }
         }
         Trace.Assert(i == indices.Length);

         return (vertices, indices);
      }
   }
}