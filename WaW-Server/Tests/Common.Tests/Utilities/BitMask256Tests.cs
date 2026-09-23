using Common.Utilities;

namespace Common.Tests.Utilities;

public class BitMask256Tests {
    public class EmptyState {
        [Fact]
        public void DefaultInstance_IsEmpty() {
            var mask = new BitMask256();

            Assert.True(mask.IsEmpty);
        }

        [Fact]
        public void DefaultInstance_NoBitsAreSet() {
            var mask = new BitMask256();

            for (var i = 0; i < 256; i++)
                Assert.False(mask.IsSet(i));
        }

        [Fact]
        public void SettingAnyBit_MakesMaskNonEmpty() {
            var mask = new BitMask256();
            mask.Set(0);

            Assert.False(mask.IsEmpty);
        }

        [Fact]
        public void Clear_RestoresEmptyState() {
            var mask = new BitMask256();
            mask.Set(10);
            mask.Set(200);

            mask.Clear();

            Assert.True(mask.IsEmpty);
            Assert.False(mask.IsSet(10));
            Assert.False(mask.IsSet(200));
        }
    }

    public class SetAndIsSet {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(31)]   // last bit of word 0
        [InlineData(32)]   // first bit of word 1
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(128)]
        [InlineData(255)]  // last bit of word 7
        public void Set_MakesIsSetTrueForThatIndex(int index) {
            var mask = new BitMask256();
            mask.Set(index);

            Assert.True(mask.IsSet(index));
        }

        [Fact]
        public void Set_DoesNotAffectOtherBitsInSameWord() {
            var mask = new BitMask256();
            mask.Set(5);

            for (var i = 0; i < 32; i++) {
                if (i == 5) continue;
                Assert.False(mask.IsSet(i));
            }
        }

        [Fact]
        public void Set_AtWordBoundary_DoesNotBleedIntoAdjacentWord() {
            var mask = new BitMask256();
            mask.Set(31);

            Assert.True(mask.IsSet(31));
            Assert.False(mask.IsSet(32));
        }

        [Fact]
        public void Set_IsIdempotent() {
            var mask = new BitMask256();
            mask.Set(17);
            mask.Set(17);

            Assert.True(mask.IsSet(17));
        }

        [Fact]
        public void MultipleSets_AreAllIndependentlyObservable() {
            var mask = new BitMask256();
            int[] indices = [0, 1, 31, 32, 100, 200, 255];

            foreach (var i in indices)
                mask.Set(i);

            foreach (var i in indices)
                Assert.True(mask.IsSet(i));

            // A handful of indices that were never set should remain false.
            int[] untouched = [2, 50, 150, 254];
            foreach (var i in untouched)
                Assert.False(mask.IsSet(i));
        }
    }

    public class UnsetBit {
        [Fact]
        public void Unset_ClearsASetBit() {
            var mask = new BitMask256();
            mask.Set(42);

            mask.Unset(42);

            Assert.False(mask.IsSet(42));
        }

        [Fact]
        public void Unset_DoesNotAffectOtherSetBits() {
            var mask = new BitMask256();
            mask.Set(10);
            mask.Set(20);

            mask.Unset(10);

            Assert.False(mask.IsSet(10));
            Assert.True(mask.IsSet(20));
        }

        [Fact]
        public void Unset_LastRemainingBit_RestoresEmptyState() {
            var mask = new BitMask256();
            mask.Set(200);

            mask.Unset(200);

            Assert.True(mask.IsEmpty);
        }

        [Fact]
        public void Unset_WhenOtherBitsRemain_StaysNonEmpty() {
            var mask = new BitMask256();
            mask.Set(5);
            mask.Set(5 + 32); // different word

            mask.Unset(5);

            Assert.False(mask.IsEmpty);
        }

        [Fact]
        public void Unset_AtWordBoundary_DoesNotBleedIntoAdjacentWord() {
            var mask = new BitMask256();
            mask.Set(31);
            mask.Set(32);

            mask.Unset(31);

            Assert.False(mask.IsSet(31));
            Assert.True(mask.IsSet(32));
        }

        [Fact]
        public void Unset_NeverSetBit_IsNoOp() {
            var mask = new BitMask256();
            mask.Set(1);

            mask.Unset(2);

            Assert.True(mask.IsSet(1));
            Assert.False(mask.IsSet(2));
            Assert.False(mask.IsEmpty);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Unset_OutOfRangeIndex_IsIgnored(int index) {
            var mask = new BitMask256();
            mask.Set(0);

            mask.Unset(index);

            Assert.True(mask.IsSet(0));
        }
    }

    public class BoundsChecking {
        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Set_OutOfRangeIndex_IsIgnored(int index) {
            var mask = new BitMask256();
            mask.Set(index);

            Assert.True(mask.IsEmpty);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void IsSet_OutOfRangeIndex_ReturnsFalse(int index) {
            var mask = new BitMask256();

            Assert.False(mask.IsSet(index));
        }
    }
}
