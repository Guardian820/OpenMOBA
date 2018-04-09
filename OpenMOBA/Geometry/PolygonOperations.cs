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

      public static List<Polygon2> FlattenToPolygons(this PolyNode polytree, bool includeOuterPolygon = true) {
         var results = new List<Polygon2>();
         var depthFilter = includeOuterPolygon ? 0 : 2; // 2 for outer void level and outer land poly level
         FlattenPolyTreeToPolygonsHelper(polytree, polytree.IsHole, results, depthFilter);
         return results;
      }

      private static void FlattenPolyTreeToPolygonsHelper(PolyNode current, bool isHole, List<Polygon2> results, int depthFilter) {
         if (current.Contour.Count > 0 && depthFilter <= 0) {
            results.Add(new Polygon2(current.Contour, isHole));
         }
         foreach (var child in current.Childs) {
            // We avoid node.isHole as that traverses upwards recursively and wastefully.
            FlattenPolyTreeToPolygonsHelper(child, !isHole, results, depthFilter - 1);
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

         public PolyTree Execute(double additionalErosionDilation = 0.0) {
            var polytree = new PolyTree();
            clipper.Execute(ClipType.ctDifference, polytree, PolyFillType.pftPositive, PolyFillType.pftPositive);
            
            // Used to remove degeneracies where additionalErosion is 0.
            const double baseErosion = 0.05;
            return Offset().Include(FlattenToPolygons(polytree))
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
