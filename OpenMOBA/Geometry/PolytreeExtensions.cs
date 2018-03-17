﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClipperLib;
using OpenMOBA.Foundation.Terrain;

namespace OpenMOBA.Geometry {
   public static class PolytreeExtensions {
      public static void AssertIsContourlessRootHolePunchResult(this PolyNode polytree) {
         // Clipper's punch operation can potentially return two nonoverlapping polygons.
         // For that reason, its returned polytree's root is a hole which contains
         // non-hole polynodes. The child of this root hole node will be the borders of the
         // distinct polygons in the result and from their childs, holes and islands within
         // those borders.
         if (!polytree.IsHole || polytree.Contour.Count != 0) {
            throw new ArgumentException("Expected land punch result polytree root to be a countourless hole?");
         }
      }

      /// <summary>
      /// Note: Containment definition varies by hole vs terrain: Containment for holes
      /// does not include the hole edge, containment for terrain includes the terrain edge.
      /// This is important, else e.g. knockback + terrain push placing an entity on an edge
      /// would potentially infinite loop.
      /// </summary>
      public static void PickDeepestPolynode(this PolyNode polyTree, IntVector2 query, out PolyNode result, out bool isHole) {
         polyTree.AssertIsContourlessRootHolePunchResult();
         PolyNode current = polyTree;
         while (true) {
            // current is a hole
            PolyNode match;

            // if we fail to find the first child land node border-inclusively containing the query point
            if (!current.Childs.TryFindFirst(child => Clipper.PointInPolygon(new IntVector2(query.X, query.Y), child.Contour) != PolygonContainmentResult.OutsidePolygon, out match)) {
               result = current;
               isHole = true;
               return;
            }

            // next off, current is land
            current = match;

            // If we fail to find a child hole border-excludingly containing the query point
            if (!current.Childs.TryFindFirst(child => Clipper.PointInPolygon(new IntVector2(query.X, query.Y), child.Contour) == PolygonContainmentResult.InPolygon, out match)) {
               result = current;
               isHole = false;
               return;
            }

            // next off, current is a hole
            current = match;
         }
      }


      /// <summary>
      /// Note: Containment definition varies by hole vs terrain: Containment for holes
      /// does not include the hole edge, containment for terrain includes the terrain edge.
      /// This is important, else e.g. knockback + terrain push placing an entity on an edge
      /// would potentially infinite loop.
      /// </summary>
      public static void PickDeepestPolynodeGivenHoleShapePolytree(this PolyTree polyTree, IntVector2 query, out PolyNode result, out bool isHole) {
         polyTree.AssertIsContourlessRootHolePunchResult();
         PolyNode current = polyTree;
         while (true) {
            // current is a hole of a hole's shape
            PolyNode match;

            // if we fail to find the first child land node border-inclusively containing the query point
            if (!current.Childs.TryFindFirst(child => Clipper.PointInPolygon(new IntVector2(query.X, query.Y), child.Contour) == PolygonContainmentResult.InPolygon, out match)) {
               result = current;
               isHole = true;
               return;
            }

            // next off, current is land of a hole's shape
            current = match;

            // If we fail to find a child hole border-excludingly containing the query point
            if (!current.Childs.TryFindFirst(child => Clipper.PointInPolygon(new IntVector2(query.X, query.Y), child.Contour) != PolygonContainmentResult.OutsidePolygon, out match)) {
               result = current;
               isHole = false;
               return;
            }

            // next off, current is a hole of a hole's shape
            current = match;
         }
      }

      public static bool SegmentInLandPolygonNonrecursive(this PolyNode landNode, IntVector2 first, IntVector2 second) {
         return SegmentInLandPolygonNonrecursive(landNode, new IntLineSegment2(first, second));
      }

      /// <summary>
      /// A line segment S is contained within a polynode P if it is contained in 
      /// P's contour (no points are outside contour) and none of P's child contours.
      /// 
      /// Note: Given a triangle-shaped land node. A line segment containing one of its edges
      /// (or a segment contained within that edge) would normally be counted as a part of the
      /// interior. For simplicity reasons, this is the case here; however, a sub-segment of
      /// that edge will not be counted as a part of the interior. We don't handle the sub-segment
      /// case because that'd be done imprecisely anyway (we're dealing with int line segments)
      /// </summary>
      public static bool SegmentInLandPolygonNonrecursive(this PolyNode landNode, IntLineSegment2 query) {
         var segmentContainment = SegmentInPolygon(landNode, query);

         switch (segmentContainment) {
            case PolygonContainmentResult.OutsidePolygon:
            case PolygonContainmentResult.IntersectsPolygon:
               return false;
            case PolygonContainmentResult.OnPolygon:
               return true;
            case PolygonContainmentResult.InPolygon:
               foreach (var child in landNode.Childs) {
                  var childContainment = SegmentInPolygon(child, query);
                  if (childContainment == PolygonContainmentResult.OutsidePolygon || childContainment == PolygonContainmentResult.OnPolygon) {
                     continue;
                  }
                  return false;
               }
               return true;
            default:
               throw new Exception("Invalid State");
         }
      }

      private static PolygonContainmentResult SegmentInPolygon(this PolyNode node, IntLineSegment2 query) {
         var bvh = node.visibilityGraphNodeData.ContourBvh;
         if (bvh != null) {
            if (bvh.Intersects(ref query)) {
               return PolygonContainmentResult.IntersectsPolygon;
            }
         } else {
            var last = node.Contour[node.Contour.Count - 1];
            var q1 = query.First;
            var q2 = query.Second;
            for (var i = 0; i < node.Contour.Count; i++) {
               var current = node.Contour[i];
               if ((current == q1 && last == q2) || (current == q2 && last == q1)) {
                  return PolygonContainmentResult.OnPolygon;
               }
               if (IntLineSegment2.Intersects(current.X, current.Y, last.X, last.Y, q1.X, q1.Y, q2.X, q2.Y)) {
                  return PolygonContainmentResult.IntersectsPolygon;
               }
               last = current;
            }
         }

         // Query segment isn't on-contour or intersecting the contour, so it's either fully in or out.
         var endpointContainment = Clipper.PointInPolygon(query.First, node.Contour);
         if (endpointContainment == PolygonContainmentResult.OnPolygon) return PolygonContainmentResult.IntersectsPolygon;
         return endpointContainment;
      }

      public static bool PointInLandPolygonNonrecursive(this PolyNode landNode, IntVector2 query) {
         var outerPipResult = Clipper.PointInPolygon(query, landNode.Contour);
         if (outerPipResult == PolygonContainmentResult.OutsidePolygon) return false;
         if (outerPipResult == PolygonContainmentResult.OnPolygon) return true;
         Trace.Assert(outerPipResult == PolygonContainmentResult.InPolygon);
     
         foreach (var child in landNode.Childs) {
            var childContainment = Clipper.PointInPolygon(query, child.Contour);
            if (childContainment == PolygonContainmentResult.OutsidePolygon || childContainment == PolygonContainmentResult.OnPolygon) {
               continue;
            }
            return false;
         }
         return true;
      }

      public static IEnumerable<PolyNode> EnumerateLandNodes(this PolyNode node) {
         var s = new Stack<PolyNode>();
         if (!node.IsHole) {
            s.Push(node);
         } else {
            foreach (var child in node.Childs) {
               s.Push(child);
            }
         }
         while (s.Count != 0) {
            var landNode = s.Pop();
            yield return landNode;
            foreach (var childHoleNode in landNode.Childs) {
               foreach (var childChildLandNode in childHoleNode.Childs) {
                  s.Push(childChildLandNode);
               }
            }
         }
      }

      public static List<PolyNode> GetLandNodes(this PolyNode node) {
         var q = new List<PolyNode>();
         if (!node.IsHole) {
            q.Add(node);
         } else {
            q.AddRange(node.Childs);
         }
         for (var i = 0; i < q.Count; i++) {
            var childHoles = q[i].Childs;
            for (var j = 0; j < childHoles.Count; j++) {
               q.AddRange(childHoles[j].Childs);
            }
         }
         return q;
      }

      public static IEnumerable<PolyNode> EnumerateAllNonrootNodes(this PolyNode root) {
         if (root.Parent != null) throw new ArgumentException("Expected root node");
         var s = new Stack<PolyNode>();
         root.Childs.ForEach(s.Push);
         while (s.Any()) {
            var node = s.Pop();
            yield return node;
            node.Childs.ForEach(s.Push);
         }
      }
   }
}