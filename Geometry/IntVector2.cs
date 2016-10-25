﻿using System;

namespace OpenMOBA.Geometry {
   public struct IntVector2 {
      public int X { get; set; }
      public int Y { get; set; }

      public IntVector2(int x, int y) {
         X = x;
         Y = y;
      }

      public float Norm2F() => (float)Math.Sqrt(X * X + Y * Y);

      public static IntVector2 operator +(IntVector2 a, IntVector2 b) => new IntVector2(a.X + b.X, a.Y + b.Y);
      public static IntVector2 operator -(IntVector2 a, IntVector2 b) => new IntVector2(a.X - b.X, a.Y - b.Y);
      public static bool operator ==(IntVector2 a, IntVector2 b) => a.X == b.X && a.Y == b.Y;
      public static bool operator !=(IntVector2 a, IntVector2 b) => a.X != b.X || a.Y != b.Y;
      public override bool Equals(object other) => other is IntVector2 && Equals((IntVector2)other);
      public bool Equals(IntVector2 other) => X == other.X && Y == other.Y;

      public override int GetHashCode() {
         unchecked {
            return (X * 397) ^ Y;
         }
      }
   }
}