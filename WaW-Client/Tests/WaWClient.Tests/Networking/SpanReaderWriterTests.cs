using System;
using System.IO;
using WaWClient.Networking;
using WaWClient.Networking.Structs.DataObjects;

namespace WaWClient.Tests.Networking;

public class SpanReaderWriterTests {
    public class Primitives {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Bool_RoundTrips(bool value) {
            Span<byte> buffer = stackalloc byte[1];
            var writer = new SpanWriter(buffer);
            writer.Write(value);

            var reader = new SpanReader(buffer);
            Assert.Equal(value, reader.ReadBoolean());
            Assert.Equal(1, reader.Position);
        }

        [Fact]
        public void Byte_RoundTrips() {
            Span<byte> buffer = stackalloc byte[1];
            var writer = new SpanWriter(buffer);
            writer.Write((byte)0xAB);

            var reader = new SpanReader(buffer);
            Assert.Equal((byte)0xAB, reader.ReadByte());
        }

        [Theory]
        [InlineData((short)0, true)]
        [InlineData(short.MaxValue, true)]
        [InlineData(short.MinValue, true)]
        [InlineData((short)-1234, false)]
        public void Int16_RoundTrips(short value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[2];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadInt16());
        }

        [Theory]
        [InlineData((ushort)0, true)]
        [InlineData(ushort.MaxValue, false)]
        public void UInt16_RoundTrips(ushort value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[2];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadUInt16());
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(int.MaxValue, true)]
        [InlineData(int.MinValue, false)]
        public void Int32_RoundTrips(int value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[4];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadInt32());
        }

        [Theory]
        [InlineData(0u, true)]
        [InlineData(uint.MaxValue, false)]
        public void UInt32_RoundTrips(uint value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[4];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadUInt32());
        }

        [Theory]
        [InlineData(0L, true)]
        [InlineData(long.MaxValue, true)]
        [InlineData(long.MinValue, false)]
        public void Int64_RoundTrips(long value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[8];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadInt64());
        }

        [Theory]
        [InlineData(0ul, true)]
        [InlineData(ulong.MaxValue, false)]
        public void UInt64_RoundTrips(ulong value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[8];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadUInt64());
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(3.14159f, true)]
        [InlineData(float.NaN, false)]
        [InlineData(float.PositiveInfinity, false)]
        public void Single_RoundTrips(float value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[4];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            var result = reader.ReadSingle();
            if (float.IsNaN(value))
                Assert.True(float.IsNaN(result));
            else
                Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0d, true)]
        [InlineData(2.718281828, true)]
        [InlineData(double.MinValue, false)]
        public void Double_RoundTrips(double value, bool littleEndian) {
            Span<byte> buffer = stackalloc byte[8];
            var writer = new SpanWriter(buffer, littleEndian);
            writer.Write(value);

            var reader = new SpanReader(buffer, littleEndian);
            Assert.Equal(value, reader.ReadDouble());
        }
    }

    public class PositionAdvancement {
        [Fact]
        public void SequentialWrites_AdvancePositionByExactByteCounts() {
            Span<byte> buffer = stackalloc byte[1 + 1 + 2 + 4 + 8 + 4];
            var writer = new SpanWriter(buffer);

            writer.Write(true);              // +1
            Assert.Equal(1, writer.Position);

            writer.Write((byte)7);           // +1
            Assert.Equal(2, writer.Position);

            writer.Write((short)123);        // +2
            Assert.Equal(4, writer.Position);

            writer.Write(456);               // +4
            Assert.Equal(8, writer.Position);

            writer.Write(789L);              // +8
            Assert.Equal(16, writer.Position);

            writer.Write(1.5f);              // +4
            Assert.Equal(20, writer.Position);
        }

        [Fact]
        public void SequentialReads_ReturnValuesInWriteOrder() {
            Span<byte> buffer = stackalloc byte[1 + 4 + 2];
            var writer = new SpanWriter(buffer);
            writer.Write(true);
            writer.Write(42);
            writer.Write((ushort)9);

            var reader = new SpanReader(buffer);
            Assert.True(reader.ReadBoolean());
            Assert.Equal(42, reader.ReadInt32());
            Assert.Equal((ushort)9, reader.ReadUInt16());
        }

        [Fact]
        public void ReadBytes_ReturnsExactSliceAndAdvancesPosition() {
            byte[] buffer = [1, 2, 3, 4, 5];
            var reader = new SpanReader(buffer);

            reader.ReadByte(); // skip first byte
            var slice = reader.ReadBytes(3);

            Assert.Equal(new byte[] { 2, 3, 4 }, slice.ToArray());
            Assert.Equal(4, reader.Position);
        }
    }

    public class Strings {
        [Theory]
        [InlineData("hello")]
        [InlineData("")]
        [InlineData("unicode: éè中")]
        public void NullTerminatedString_RoundTrips(string value) {
            Span<byte> buffer = stackalloc byte[128];
            var writer = new SpanWriter(buffer);
            writer.WriteNullTerminatedString(value);

            var reader = new SpanReader(buffer);
            Assert.Equal(value, reader.ReadNullTerminatedString());
        }

        [Fact]
        public void NullTerminatedString_MissingTerminator_Throws() {
            byte[] buffer = "no-terminator"u8.ToArray();
            var reader = new SpanReader(buffer);

            // Assert.Throws takes a delegate, which can't capture a ref struct local - use try/catch instead.
            Exception? caught = null;
            try {
                reader.ReadNullTerminatedString();
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.IsType<InvalidDataException>(caught);
        }

        [Theory]
        [InlineData("a short message")]
        [InlineData("")]
        public void UTF_RoundTrips(string value) {
            Span<byte> buffer = stackalloc byte[128];
            var writer = new SpanWriter(buffer);
            writer.WriteUTF(value);

            var reader = new SpanReader(buffer);
            Assert.Equal(value, reader.ReadUTF());
        }

        [Fact]
        public void WriteUTF_TooLargeForBuffer_Throws() {
            Span<byte> buffer = stackalloc byte[4];
            var writer = new SpanWriter(buffer);

            // Assert.Throws takes a delegate, which can't capture a ref struct local - use try/catch instead.
            Exception? caught = null;
            try {
                writer.WriteUTF("this does not fit");
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.IsType<InternalBufferOverflowException>(caught);
        }

        [Theory]
        [InlineData("a longer 32-bit-length-prefixed message")]
        [InlineData("")]
        public void Utf32_RoundTrips(string value) {
            Span<byte> buffer = stackalloc byte[256];
            var writer = new SpanWriter(buffer);
            writer.Write32UTF(value);

            var reader = new SpanReader(buffer);
            Assert.Equal(value, reader.Read32UTF());
        }
    }

    public class GenericArrayWrite {
        [Fact]
        public void ByteArray_RoundTrips_ViaLengthPrefixAndReadBytes() {
            byte[] data = [10, 20, 30, 40, 250];
            Span<byte> buffer = stackalloc byte[2 + data.Length];
            var writer = new SpanWriter(buffer);
            writer.Write(data);

            var reader = new SpanReader(buffer);
            var length = reader.ReadUInt16();
            var result = reader.ReadBytes(length);

            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public void CharArray_RoundTrips_ViaReadUTF() {
            char[] chars = ['h', 'i', '!'];
            Span<byte> buffer = stackalloc byte[16];
            var writer = new SpanWriter(buffer);
            writer.Write(chars);

            var reader = new SpanReader(buffer);
            Assert.Equal(new string(chars), reader.ReadUTF());
        }

        [Fact]
        public void StringArray_RoundTrips_ViaCountThenReadUTFLoop() {
            string[] strings = ["alpha", "beta", ""];
            Span<byte> buffer = stackalloc byte[64];
            var writer = new SpanWriter(buffer);
            writer.Write(strings);

            var reader = new SpanReader(buffer);
            var count = reader.ReadUInt16();
            var result = new string[count];
            for (var i = 0; i < count; i++)
                result[i] = reader.ReadUTF();

            Assert.Equal(strings, result);
        }

        [Fact]
        public void IntArray_RoundTrips_ViaLengthPrefixThenReadInt32Loop() {
            int[] values = [1, -2, 3, int.MaxValue];
            Span<byte> buffer = stackalloc byte[2 + values.Length * 4];
            var writer = new SpanWriter(buffer);
            writer.Write(values);

            var reader = new SpanReader(buffer);
            var count = reader.ReadUInt16();
            var result = new int[count];
            for (var i = 0; i < count; i++)
                result[i] = reader.ReadInt32();

            Assert.Equal(values, result);
        }
    }

    public class PositionDataObject {
        [Fact]
        public void Write_Position_EncodesXThenYAsFloats() {
            var pos = new Position(12.5f, -3.25f);
            Span<byte> buffer = stackalloc byte[8];
            var writer = new SpanWriter(buffer);
            writer.Write(pos);

            var reader = new SpanReader(buffer);
            Assert.Equal(12.5f, reader.ReadSingle());
            Assert.Equal(-3.25f, reader.ReadSingle());
        }

        [Fact]
        public void Position_ReadThenWrite_RoundTripsThroughIDataObject() {
            var original = new Position(7f, 8f);
            Span<byte> buffer = stackalloc byte[8];
            var writer = new SpanWriter(buffer);
            original.Write(ref writer);

            var reader = new SpanReader(buffer);
            var roundTripped = new Position();
            roundTripped.Read(ref reader);

            Assert.Equal(original.X, roundTripped.X);
            Assert.Equal(original.Y, roundTripped.Y);
        }
    }
}
