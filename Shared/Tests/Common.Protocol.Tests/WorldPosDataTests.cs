using System.Numerics;
using Common.Structs;

namespace Common.Protocol.Tests;

public class WorldPosDataTests {
    public class Construction {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(3.5f, -2.25f)]
        [InlineData(float.MaxValue, float.MinValue)]
        public void Ctor_SetsXAndY(float x, float y) {
            var pos = new WorldPosData(x, y);

            Assert.Equal(x, pos.X);
            Assert.Equal(y, pos.Y);
        }
    }

    public class Arithmetic {
        [Fact]
        public void OperatorPlus_AddsComponentwise() {
            var a = new WorldPosData(1f, 2f);
            var b = new WorldPosData(10f, -5f);

            var result = a + b;

            Assert.Equal(11f, result.X);
            Assert.Equal(-3f, result.Y);
        }

        [Fact]
        public void OperatorPlus_DoesNotMutateOperands() {
            var a = new WorldPosData(1f, 2f);
            var b = new WorldPosData(10f, -5f);

            _ = a + b;

            Assert.Equal(1f, a.X);
            Assert.Equal(2f, a.Y);
            Assert.Equal(10f, b.X);
            Assert.Equal(-5f, b.Y);
        }

        [Fact]
        public void CompoundPlusEquals_MutatesInPlace() {
            var a = new WorldPosData(1f, 2f);
            var b = new WorldPosData(10f, -5f);

            a += b;

            Assert.Equal(11f, a.X);
            Assert.Equal(-3f, a.Y);
        }
    }

    public class EqualityAndHashing {
        [Fact]
        public void Equals_TrueForSameCoordinates() {
            var a = new WorldPosData(4f, 7f);
            var b = new WorldPosData(4f, 7f);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Theory]
        [InlineData(4f, 7f, 4f, 8f)]
        [InlineData(4f, 7f, 5f, 7f)]
        public void Equals_FalseForDifferentCoordinates(float x1, float y1, float x2, float y2) {
            var a = new WorldPosData(x1, y1);
            var b = new WorldPosData(x2, y2);

            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Equals_FalseForNonWorldPosDataObject() {
            var a = new WorldPosData(1f, 1f);

            Assert.False(a.Equals("not a WorldPosData"));
        }

        [Fact]
        public void GetHashCode_MatchesForEqualValues() {
            var a = new WorldPosData(4f, 7f);
            var b = new WorldPosData(4f, 7f);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }

    public class Conversions {
        [Fact]
        public void ImplicitConversion_ToVector2_PreservesComponents() {
            var pos = new WorldPosData(3f, -9f);

            Vector2 vec = pos;

            Assert.Equal(3f, vec.X);
            Assert.Equal(-9f, vec.Y);
        }

        [Fact]
        public void ToVec2_PreservesComponents() {
            var pos = new WorldPosData(3f, -9f);

            var vec = pos.ToVec2();

            Assert.Equal(new Vector2(3f, -9f), vec);
        }

        [Fact]
        public void ToWorldPos_RoundTripsFromVector2() {
            var vec = new Vector2(6f, 12f);

            var pos = vec.ToWorldPos();

            Assert.Equal(new WorldPosData(6f, 12f), pos);
        }

        [Fact]
        public void ToString_ReportsXAndY() {
            var pos = new WorldPosData(2f, 5f);

            Assert.Equal("X:2, Y:5", pos.ToString());
        }
    }

    public class DistanceAndAngle {
        [Fact]
        public void DistSqr_WorldPosDataOverload_MatchesSquaredEuclideanDistance() {
            var a = new WorldPosData(0f, 0f);
            var b = new WorldPosData(3f, 4f);

            Assert.Equal(25f, a.DistSqr(b));
        }

        [Fact]
        public void DistSqr_Vector2Overload_MatchesSquaredEuclideanDistance() {
            var a = new Vector2(0f, 0f);
            var b = new Vector2(3f, 4f);

            Assert.Equal(25f, a.DistSqr(b));
        }

        [Fact]
        public void DistSqr_IsZero_ForIdenticalPoints() {
            var a = new WorldPosData(5f, -2f);

            Assert.Equal(0f, a.DistSqr(a));
        }

        [Theory]
        [InlineData(0f, 0f, 1f, 0f, 0f)]      // straight right -> 0 degrees
        [InlineData(0f, 0f, 0f, 1f, 90f)]     // straight up -> 90 degrees
        [InlineData(0f, 0f, -1f, 0f, 180f)]   // straight left -> 180 degrees
        [InlineData(0f, 0f, 0f, -1f, -90f)]   // straight down -> -90 degrees
        public void AngleDegrees_MatchesExpectedDirection(float x1, float y1, float x2, float y2, float expectedDegrees) {
            var from = new WorldPosData(x1, y1);
            var to = new WorldPosData(x2, y2);

            Assert.Equal(expectedDegrees, from.AngleDegrees(to), precision: 3);
        }

        [Fact]
        public void AngleRadians_MatchesAtan2OfDelta() {
            var from = new WorldPosData(1f, 1f);
            var to = new WorldPosData(4f, 5f);

            var expected = MathF.Atan2(5f - 1f, 4f - 1f);

            Assert.Equal(expected, from.AngleRadians(to), precision: 6);
        }
    }
}
