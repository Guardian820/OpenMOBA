﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClipperLib;
using OpenMOBA;
using OpenMOBA.DataStructures;
using OpenMOBA.DevTool.Debugging;
using OpenMOBA.Foundation;
using OpenMOBA.Foundation.Terrain;
using OpenMOBA.Foundation.Terrain.Snapshots;
using OpenMOBA.Foundation.Terrain.Visibility;
using OpenMOBA.Geometry;

namespace PolyNodeCrossoverPointManagerBenchmark {
   public static class Program {
      private static readonly Size bounds = new Size(1280, 720);
      private static readonly Random random = new Random(3);
      private static readonly DebugMultiCanvasHost host = DebugMultiCanvasHost.CreateAndShowCanvas(
         bounds, 
         new Point(50, 50), 
         new OrthographicXYProjector(0.8));

      public static void Main(string[] args) {
         var sectorMetadataPresets = SectorMetadataPresets.HashCircle2;
         var terrainStaticMetadata = new TerrainStaticMetadata {
            LocalBoundary = sectorMetadataPresets.LocalBoundary,
            LocalIncludedContours = sectorMetadataPresets.LocalIncludedContours,
            LocalExcludedContours = sectorMetadataPresets.LocalExcludedContours
         };

         var (localGeometryView, landPolyNode, crossoverPointManager) = BenchmarkAddCrossoverPoints(terrainStaticMetadata);
         var canvas = host.CreateAndAddCanvas(0);
         //canvas.DrawPolyTree((PolyTree)landPolyNode.Parent);
         canvas.DrawPoints(crossoverPointManager.CrossoverPoints, StrokeStyle.RedThick5Solid);
         canvas.DrawVisibilityGraph(landPolyNode.ComputeVisibilityGraph());
         canvas.DrawLineList(landPolyNode.FindContourAndChildHoleBarriers(), StrokeStyle.BlackHairLineSolid);

         // var a = landPolyNode.FindAggregateContourCrossoverWaypoints()[6];
         // var b = landPolyNode.FindAggregateContourCrossoverWaypoints()[13];
         // var q = new IntLineSegment2(a, b);
         // canvas.DrawPoint(a, StrokeStyle.RedThick5Solid);
         // canvas.DrawPoint(b, StrokeStyle.RedThick5Solid);
         // var bvh = landPolyNode.FindContourAndChildHoleBarriersBvh();
         // canvas.DrawBvh(bvh);
         // foreach (var (i, val) in bvh.Segments.Enumerate()) {
         //    if (val.Intersects(q)) Console.WriteLine(i + " " + val);
         // }
         // var intersects = bvh.Intersects(q);
         // canvas.DrawLine(a, b, intersects ? StrokeStyle.RedHairLineSolid : StrokeStyle.LimeHairLineSolid);

         Console.WriteLine(
            PolyNodeCrossoverPointManager.CrossoverPointsAdded + " " + 
            PolyNodeCrossoverPointManager.FindOptimalLinksToCrossoversInvocationCount + " " + 
            PolyNodeCrossoverPointManager.FindOptimalLinksToCrossovers_CandidateWaypointVisibilityCheck + " " + 
            PolyNodeCrossoverPointManager.FindOptimalLinksToCrossovers_CostToWaypointCount + " " + 
            PolyNodeCrossoverPointManager.ProcessCpiInvocationCount + " " + 
            PolyNodeCrossoverPointManager.ProcessCpiInvocation_CandidateBarrierIntersectCount + " " + 
            PolyNodeCrossoverPointManager.ProcessCpiInvocation_DirectCount + " " + 
            PolyNodeCrossoverPointManager.ProcessCpiInvocation_IndirectCount);

         while (true) {
            const int ntrials = 100;
            var sw = new Stopwatch();
            sw.Start();
            for (var i = 0; i < ntrials; i++) {
               BenchmarkAddCrossoverPoints(terrainStaticMetadata);
            }
            Console.WriteLine($"{ntrials} trials in {sw.ElapsedMilliseconds} ms");
         }
      }

      private static (LocalGeometryView, PolyNode, PolyNodeCrossoverPointManager) BenchmarkAddCrossoverPoints(TerrainStaticMetadata terrainStaticMetadata) {
         var localGeometryJob = new LocalGeometryJob(terrainStaticMetadata);
         var localGeometryViewManager = new LocalGeometryViewManager(localGeometryJob);
         var localGeometryView = localGeometryViewManager.GetErodedView(0.0);
         var landPolyNode = localGeometryView.PunchedLand.Childs.First();

         // Precompute polynode geometry structures to isolate in profiling results.
         landPolyNode.FindAggregateContourCrossoverWaypoints();
         landPolyNode.ComputeVisibilityGraph();
         landPolyNode.ComputeWaypointVisibilityPolygons();
         landPolyNode.FindContourAndChildHoleBarriers();
         landPolyNode.FindContourAndChildHoleBarriersBvh();

         // Then build CPM, which uses cached results from above.
         var crossoverPointManager = new PolyNodeCrossoverPointManager(landPolyNode);

         var spacing = 10;
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(200, 0), new DoubleVector2(400, 0)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(600, 0), new DoubleVector2(800, 0)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(200, 1000), new DoubleVector2(400, 1000)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(600, 1000), new DoubleVector2(800, 1000)), spacing);
         
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(0, 200), new DoubleVector2(0, 400)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(0, 600), new DoubleVector2(0, 800)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(1000, 200), new DoubleVector2(1000, 400)), spacing);
         AddCrossoverPoints(crossoverPointManager, new DoubleLineSegment2(new DoubleVector2(1000, 600), new DoubleVector2(1000, 800)), spacing);
         
         //Console.WriteLine(crossoverPointManager.CrossoverPoints.Count + " " + crossoverPointManager.CrossoverPoints.Count / 8);

         return (localGeometryView, landPolyNode, crossoverPointManager);
      }

      private static void AddCrossoverPoints(PolyNodeCrossoverPointManager cpm, DoubleLineSegment2 segment, int spacing) {
         var npoints = (int)Math.Ceiling(segment.First.To(segment.Second).Norm2D() / spacing) + 1;
         var points = Util.Generate(npoints, i => segment.PointAt(i / (double)(npoints - 1)).LossyToIntVector2());
         cpm.AddMany(segment, points);
      }
   }
}