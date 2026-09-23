using Common.Network;
using Common.Utilities.Collections;

namespace Common.Tests.Utilities.Collections;

public class EntityIdTests {
    public class Construction {
        [Fact]
        public void RawValueConstructor_StoresValueVerbatim() {
            var id = new EntityId(0x12345678);

            Assert.Equal(0x12345678, id.Value);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        [InlineData(500, 7)]
        public void IndexGenerationConstructor_PacksIntoExpectedValue(int index, int generation) {
            var id = new EntityId(index, generation);

            Assert.Equal((generation << 20) | index, id.Value);
        }

        [Fact]
        public void Null_IsZeroIndexAndZeroGeneration() {
            Assert.Equal(0, EntityId.Null.Value);
            Assert.Equal(0, EntityId.Null.Index);
            Assert.Equal(0, EntityId.Null.Generation);
        }
    }

    public class BitPacking {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0xFFFFF, 0)]        // max 20-bit index, no generation
        [InlineData(0, 0xFFF)]          // no index, max 12-bit generation
        [InlineData(0xFFFFF, 0xFFF)]    // both maxed - fills all 32 bits
        [InlineData(12345, 42)]
        public void IndexAndGeneration_RoundTripThroughValue(int index, int generation) {
            var id = new EntityId(index, generation);

            Assert.Equal(index, id.Index);
            Assert.Equal(generation, id.Generation);
        }

        [Fact]
        public void MaxIndexAndGeneration_ProducesAllBitsSet() {
            var id = new EntityId(0xFFFFF, 0xFFF);

            Assert.Equal(-1, id.Value); // 0xFFFFFFFF as a signed int32
        }

        [Fact]
        public void Generation_OccupiesBitsAboveIndex() {
            // A generation of 1 with index 0 should land exactly at bit 20.
            var id = new EntityId(0, 1);

            Assert.Equal(1 << 20, id.Value);
        }

        [Fact]
        public void Index_DoesNotReadIntoGenerationBits() {
            // Index accessor must mask to the low 20 bits even when generation bits are set.
            var id = new EntityId(0xFFFFF, 0xABC);

            Assert.Equal(0xFFFFF, id.Index);
        }

        [Fact]
        public void Generation_DoesNotReadIndexBits() {
            var id = new EntityId(0xFFFFF, 0);

            Assert.Equal(0, id.Generation);
        }
    }

    public class EqualityAndHashing {
        [Fact]
        public void Equals_TrueForSameValue() {
            var a = new EntityId(10, 2);
            var b = new EntityId(10, 2);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Theory]
        [InlineData(10, 2, 11, 2)]
        [InlineData(10, 2, 10, 3)]
        public void Equals_FalseForDifferentIndexOrGeneration(int index1, int gen1, int index2, int gen2) {
            var a = new EntityId(index1, gen1);
            var b = new EntityId(index2, gen2);

            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Equals_FalseForNonEntityIdObject() {
            var a = new EntityId(1, 1);

            Assert.False(a.Equals("not an EntityId"));
        }

        [Fact]
        public void GetHashCode_MatchesForEqualValues() {
            var a = new EntityId(99, 4);
            var b = new EntityId(99, 4);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_EqualsRawValue() {
            var a = new EntityId(99, 4);

            Assert.Equal(a.Value, a.GetHashCode());
        }
    }

    public class Serialization {
        [Fact]
        public void Read_ParsesRawInt32FromReader() {
            var original = new EntityId(321, 9);
            Span<byte> buffer = stackalloc byte[4];
            var writer = new SpanWriter(buffer);
            writer.Write(original.Value);

            var reader = new SpanReader(buffer);
            var result = EntityId.Read(ref reader);

            Assert.Equal(original, result);
            Assert.Equal(original.Index, result.Index);
            Assert.Equal(original.Generation, result.Generation);
        }
    }
}
