﻿using System;
using System.Collections.Generic;
using System.Linq;
using ClipperLib;
using Poly2Tri.Triangulation;

using IntPoint = OpenMOBA.Geometry.IntVector3;

namespace OpenMOBA.Geometry {
   public static class PolygonOperations {
      public static DoubleVector2 ToOpenMobaPointD(this TriangulationPoint input) {
         return new DoubleVector2(input.X, input.Y);
      }

      public static UnionOperation Union() => new UnionOperation();

      public static PunchOperation Punch() => new PunchOperation();

      public static OffsetOperation Offset() => new OffsetOperation();

      /// <summary>
      /// https://en.wikipedia.org/wiki/Sutherland%E2%80%93Hodgman_algorithm#Pseudo_code
      /// Fails if result would be empty
      /// </summary>
      public static bool TryConvexClip(Polygon2 subject, Polygon2 clip, out Polygon2 result) {
         bool Inside(IntVector2 p, IntLineSegment2 edge) => GeometryOperations.Clockness(edge.First, edge.Second, p) != Clockness.CounterClockwise;

         List<IntVector2> outputList = subject.Points;
         for (var i = 0; i < clip.Points.Count - 1; i++) {
            var clipEdge = new IntLineSegment2(clip.Points[i], clip.Points[i + 1]);
            List<IntVector2> inputList = outputList;
            outputList = new List<IntVector2>();

            var S = inputList[inputList.Count - 2];
            for (var j = 0; j < inputList.Count - 1; j++) {
               var E = inputList[j];
               if (Inside(E, clipEdge)) {
                  if (!Inside(S, clipEdge)) {
                     var SE = new IntLineSegment2(S, E);
                     if (!GeometryOperations.TryFindLineLineIntersection(SE, clipEdge, out var intersection)) {
                        throw new NotImplementedException();
                     }
                     outputList.Add(intersection.LossyToIntVector2());
                  }
                  outputList.Add(E);
               } else if (Inside(S, clipEdge)) {
                  var SE = new IntLineSegment2(S, E);
                  if (!GeometryOperations.TryFindLineLineIntersection(SE, clipEdge, out var intersection)) {
                     throw new NotImplementedException();
                  }
                  outputList.Add(intersection.LossyToIntVector2());
               }
               S = E;
            }

            if (outputList.Count == 0) {
               result = null;
               return false;
            }

            outputList.Add(outputList[0]);
         }

         result = new Polygon2(outputList);
         return true;
      }

      public static PolyTree CleanPolygons(List<Polygon2> polygons) {
         return Offset().Include(polygons)
                        .Erode(0.05)
                        .Dilate(0.05)
                        .Execute();
      }

      public static List<IReadOnlyList<IntVector2>> FlattenToContours(this PolyNode polytree, bool includeOuterPolygon = true) {
         var results = new List<IReadOnlyList<IntVector2>>();
         var depthFilter = includeOuterPolygon ? 0 : 2; // 2 for outer void level and outer land poly level
         FlattenToContoursHelper(polytree, polytree.IsHole, results, depthFilter);
         return results;
      }

      private static void FlattenToContoursHelper(PolyNode current, bool isHole, List<IReadOnlyList<IntVector2>> results, int depthFilter) {
         if (current.Contour.Count > 0 && depthFilter <= 0) {
            results.Add(current.Contour);
         }
         foreach (var child in current.Childs) {
            // We avoid node.isHole as that traverses upwards recursively and wastefully.
            FlattenToContoursHelper(child, !isHole, results, depthFilter - 1);
         }
      }

      public static List<(Polygon2 polygon, bool isHole)> FlattenToPolygonAndIsHoles(this PolyNode polytree, bool includeOuterPolygon = true, bool flipIsHoleResult = false) {
         var results = new List<(Polygon2, bool)>();
         var depthFilter = includeOuterPolygon ? 0 : 2; // 2 for outer void level and outer land poly level
         FlattenPolyTreeToPolygonsHelper(polytree, polytree.IsHole, results, depthFilter, flipIsHoleResult);
         return results;
      }

      private static void FlattenPolyTreeToPolygonsHelper(PolyNode current, bool isHole, List<(Polygon2, bool)> results, int depthFilter, bool flipIsHoleResult) {
         if (current.Contour.Count > 0 && depthFilter <= 0) {
            var contour = current.Contour;
            if (isHole) {
               contour = contour.ToList();
               contour.Reverse();
            }
            results.Add((new Polygon2(contour), isHole ^ flipIsHoleResult));
         }
         foreach (var child in current.Childs) {
            // We avoid node.isHole as that traverses upwards recursively and wastefully.
            FlattenPolyTreeToPolygonsHelper(child, !isHole, results, depthFilter - 1, flipIsHoleResult);
         }
      }

      public class UnionOperation {
         private readonly Clipper clipper = new Clipper { StrictlySimple = true };

         public UnionOperation Include(params Polygon2[] polygons) => Include((IReadOnlyList<Polygon2>)polygons);

         public UnionOperation Include(IReadOnlyList<Polygon2> polygons) {
            foreach (var polygon in polygons) {
               clipper.AddPath(polygon.Points, PolyType.ptSubject, polygon.IsClosed);
            }
            return this;
         }

         public PolyTree Execute() {
            var polytree = new PolyTree();
            clipper.Execute(ClipType.ctUnion, polytree, PolyFillType.pftPositive, PolyFillType.pftPositive);
            return polytree;
         }
      }

      public class PunchOperation {
         private readonly Clipper clipper = new Clipper { StrictlySimple = true };

         public PunchOperation IncludeOrExclude(params (Polygon2 polygon, bool isHole)[] polygonAndIsHoles) => IncludeOrExclude((IReadOnlyList<(Polygon2 polygon, bool isHole)>)polygonAndIsHoles);

         public PunchOperation IncludeOrExclude(IReadOnlyList<(Polygon2 polygon, bool isHole)> polygonAndIsHoles, bool includeHolesExcludeLand = false) {
            foreach (var (polygon, isHole) in polygonAndIsHoles) {
               if (isHole == includeHolesExcludeLand) {
                  Include(polygon);
               } else {
                  Exclude(polygon);
               }
            }
            return this;
         }

         public PunchOperation Include(params Polygon2[] polygons) => Include((IEnumerable<Polygon2>)polygons);

         public PunchOperation Include(IEnumerable<Polygon2> polygons) {
            foreach (var polygon in polygons) {
               clipper.AddPath(polygon.Points, PolyType.ptSubject, polygon.IsClosed);
            }
            return this;
         }

         public PunchOperation Exclude(params Polygon2[] polygons) => Exclude((IEnumerable<Polygon2>)polygons);

         public PunchOperation Exclude(IEnumerable<Polygon2> polygons) {
            foreach (var polygon in polygons) {
               clipper.AddPath(polygon.Points, PolyType.ptClip, polygon.IsClosed);
            }
            return this;
         }

         // excludes the polygon/isHole pairs of holes
         public PunchOperation Exclude(IReadOnlyList<(Polygon2 polygon, bool isHole)> polygonAndIsHoles) {
            foreach (var (polygon, isHole) in polygonAndIsHoles) {
               var points = polygon.Points;
               if (isHole) {
                  points = points.ToList();
                  points.Reverse();
               }
               clipper.AddPath(points, PolyType.ptClip, polygon.IsClosed);

            }
            return this;
         }


         public PolyTree Execute(double additionalErosionDilation = 0.0) {
            var polytree = new PolyTree();
            clipper.Execute(ClipType.ctDifference, polytree, PolyFillType.pftPositive, PolyFillType.pftPositive);
            
            // Used to remove degeneracies where additionalErosion is 0.
            const double baseErosion = 0.05;
            return Offset().Include(FlattenToPolygonAndIsHoles(polytree))
                           .Erode(baseErosion)
                           .Dilate(baseErosion)
                           .ErodeOrDilate(additionalErosionDilation)
                           .Execute();
         }
      }

      public class OffsetOperation {
         private readonly double kSpecialOffsetCleanup = double.NegativeInfinity;
         private readonly List<IReadOnlyList<IntVector2>> includedContours = new List<IReadOnlyList<IntVector2>>();
         private readonly List<double> offsets = new List<double>();

         /// <param name="delta">Positive dilates, negative erodes</param>
         public OffsetOperation ErodeOrDilate(double delta) {
            if (double.IsInfinity(delta) || double.IsNaN(delta)) {
               throw new ArgumentException();
            }
            offsets.Add(delta);
            return this;
         }

         public OffsetOperation Erode(double delta) {
            if (double.IsInfinity(delta) || double.IsNaN(delta)) {
               throw new ArgumentException();
            }
            if (delta < 0) {
               throw new ArgumentOutOfRangeException();
            }

            offsets.Add(-delta);
            return this;
         }

         public OffsetOperation Dilate(double delta) {
            if (double.IsInfinity(delta) || double.IsNaN(delta)) {
               throw new ArgumentException();
            }
            if (delta < 0) {
               throw new ArgumentOutOfRangeException();
            }

            offsets.Add(delta);
            return this;
         }

         public OffsetOperation Cleanup() {
            offsets.Add(kSpecialOffsetCleanup);
            return this;
         }

         public OffsetOperation Include(params Polygon2[] polygons) => Include((IReadOnlyList<Polygon2>)polygons);

         public OffsetOperation Include(params IReadOnlyList<IntVector2>[] contours) {
            foreach (var contour in contours) {
               includedContours.Add(contour);
            }
            return this;
         }

         public OffsetOperation Include(IEnumerable<Polygon2> polygons) {
            return Include(polygons.Select(p => p.Points));
         }

         public OffsetOperation Include(IEnumerable<IReadOnlyList<IntVector2>> contours) {
            foreach (var contour in contours) {
               includedContours.Add(contour);
            }
            return this;
         }

         public OffsetOperation Include(params (Polygon2 polygon, bool isHole)[] polygons) => Include((IReadOnlyList<(Polygon2, bool)>)polygons);

         public OffsetOperation Include(params (IReadOnlyList<IntVector2>, bool isHole)[] contourAndIsHoles) {
            foreach (var (contour, isHole) in contourAndIsHoles) {
               includedContours.Add(contour);
            }
            return this;
         }

         public OffsetOperation Include(IEnumerable<(Polygon2 polygon, bool isHole)> polygonAndIsHoles) {
            return Include(polygonAndIsHoles.Select(pair => ReverseIfIsHole(pair.polygon.Points, pair.isHole)));
         }

         private Polygon2 ReverseIfIsHole(IReadOnlyList<IntVector2> points, bool isHole) {
            if (isHole) {
               var copy = new List<IntVector2>(points);
               copy.Reverse();
               return new Polygon2(copy);
            }
            return new Polygon2(points.ToList());
         }

         public OffsetOperation Include(IEnumerable<(IReadOnlyList<IntVector2> polygon, bool isHole)> polygonAndIsHoles) {
            foreach (var (polygon, isHole) in polygonAndIsHoles) {
               includedContours.Add(ReverseIfIsHole(polygon, isHole).Points);
            }
            return this;
         }

         public PolyTree Execute() {
            var currentContours = includedContours;
            for (var i = 0; i < offsets.Count; i++) {
               var offset = offsets[i];
               
               // ReSharper disable once CompareOfFloatsByEqualityOperator
               if (offset == kSpecialOffsetCleanup) {
                  continue;
               }

               var polytree = new PolyTree();
               var clipper = new ClipperOffset();
               foreach (var contour in currentContours) {
                  clipper.AddPath(contour, JoinType.jtMiter, EndType.etClosedPolygon);
               }
               clipper.Execute(ref polytree, offset);

               // hack: cleanup
               while (i + 1 != offsets.Count && offsets[i + 1] == kSpecialOffsetCleanup) {
                  i++;
                  polytree.Prune(0);
               }

               if (i + 1 == offsets.Count) {
                  return polytree;
               } else {
                  currentContours = polytree.FlattenToContours();
               }
            }
            throw new ArgumentException("Must specify some polygons to include!");
         }
      }
   }
}
