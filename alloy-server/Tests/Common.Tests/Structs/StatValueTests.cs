using Common.Structs;

namespace Common.Tests.Structs;

public class StatValueTests {
    public class Factories {
        [Fact]
        public void FromInt_SetsTypeAndIntVal() {
            var value = StatValue.FromInt(42);

            Assert.Equal(StatValueType.Int, value.Type);
            Assert.Equal(42, value.IntVal);
            Assert.True(value.HasValue);
        }

        [Fact]
        public void FromFloat_SetsTypeAndFloatVal() {
            var value = StatValue.FromFloat(3.5f);

            Assert.Equal(StatValueType.Float, value.Type);
            Assert.Equal(3.5f, value.FloatVal);
            Assert.True(value.HasValue);
        }

        [Fact]
        public void FromString_SetsTypeAndStrVal() {
            var value = StatValue.FromString("abc");

            Assert.Equal(StatValueType.Str, value.Type);
            Assert.Equal("abc", value.StrVal);
            Assert.True(value.HasValue);
        }

        [Fact]
        public void Default_HasNoValue() {
            var value = default(StatValue);

            Assert.Equal(StatValueType.None, value.Type);
            Assert.False(value.HasValue);
        }
    }

    public class UnionLayout {
        [Fact]
        public void IntValAndFloatVal_ShareTheSameFourBytes() {
            var value = StatValue.FromInt(BitConverter.SingleToInt32Bits(9.75f));

            Assert.Equal(9.75f, value.FloatVal);
        }

        [Fact]
        public void FloatValAndIntVal_ShareTheSameFourBytes() {
            var value = StatValue.FromFloat(9.75f);

            Assert.Equal(BitConverter.SingleToInt32Bits(9.75f), value.IntVal);
        }

        [Fact]
        public void StrVal_IsIndependentOfIntFloatSlot() {
            var value = StatValue.FromString("hello");

            // Setting a string shouldn't be affected by whatever garbage sits in the overlapped int/float slot.
            Assert.Equal("hello", value.StrVal);
        }
    }

    public class EqualityAndHashing {
        [Fact]
        public void Equals_TrueForSameTypeAndIntValue() {
            var a = StatValue.FromInt(7);
            var b = StatValue.FromInt(7);

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Equals_TrueForSameTypeAndFloatValue() {
            var a = StatValue.FromFloat(1.25f);
            var b = StatValue.FromFloat(1.25f);

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_TrueForSameTypeAndStringValue() {
            var a = StatValue.FromString("match");
            var b = StatValue.FromString("match");

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_FalseForDifferentIntValues() {
            var a = StatValue.FromInt(1);
            var b = StatValue.FromInt(2);

            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_FalseForDifferentStringValues() {
            var a = StatValue.FromString("a");
            var b = StatValue.FromString("b");

            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_FalseAcrossDifferentTypes_EvenWithOverlappingRawBits() {
            // Int and Float share the same 4-byte slot; Equals must key off Type first
            // so an Int and a Float that happen to share a bit pattern are never equal.
            var asInt = StatValue.FromInt(BitConverter.SingleToInt32Bits(2f));
            var asFloat = StatValue.FromFloat(2f);

            Assert.False(asInt.Equals(asFloat));
            Assert.True(asInt != asFloat);
        }

        [Fact]
        public void Equals_TwoNoneValues_AreEqual() {
            var a = default(StatValue);
            var b = default(StatValue);

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_FalseForNonStatValueObject() {
            var a = StatValue.FromInt(1);

            Assert.False(a.Equals("not a StatValue"));
        }

        [Fact]
        public void Equals_FloatNaN_IsEqualToItself() {
            // float.Equals (unlike ==) treats NaN as equal to NaN - StatValue delegates to FloatVal.Equals.
            var a = StatValue.FromFloat(float.NaN);
            var b = StatValue.FromFloat(float.NaN);

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void GetHashCode_MatchesForEqualIntValues() {
            var a = StatValue.FromInt(55);
            var b = StatValue.FromInt(55);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_MatchesForEqualStringValues() {
            var a = StatValue.FromString("same");
            var b = StatValue.FromString("same");

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_IsZeroForNone() {
            var value = default(StatValue);

            Assert.Equal(0, value.GetHashCode());
        }
    }
}
