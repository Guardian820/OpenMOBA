﻿using System.Drawing;
using System.Linq;
using OpenMOBA.Foundation.Visibility;
using OpenMOBA.Geometry;

namespace OpenMOBA.DevTool.Debugging {
   public static class VisibilityGraphDebugDisplay {
      private static readonly StrokeStyle BarrierStrokeStyle = new StrokeStyle(Color.DimGray);
      private static readonly StrokeStyle VisibilityEdgeStrokeStyle = new StrokeStyle(Color.Cyan);
      private static readonly StrokeStyle WaypointStrokeStyle = new StrokeStyle(Color.Red, 5.0f);

      public static void DrawVisibilityGraph(this IDebugCanvas canvas, VisibilityGraph visibilityGraph) {
         canvas.DrawLineList(
            visibilityGraph.Barriers.SelectMany(barrier => barrier.Points).ToList(),
            BarrierStrokeStyle);

         if (visibilityGraph.Waypoints.Any()) {
            canvas.DrawLineList(
               (from i in Enumerable.Range(0, visibilityGraph.Waypoints.Length - 1)
                  from j in Enumerable.Range(i + 1, visibilityGraph.Waypoints.Length - i - 1)
                  where !double.IsNaN(visibilityGraph.Distances[i, j])
                  select new IntLineSegment3(visibilityGraph.Waypoints[i], visibilityGraph.Waypoints[j])).ToList(),
               VisibilityEdgeStrokeStyle);
            canvas.DrawPoints(
               visibilityGraph.Waypoints,
               WaypointStrokeStyle);
         }
      }
   }
}