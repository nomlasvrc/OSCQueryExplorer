using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.Osc;

public static class OscCodec
{
    public static string GetTypeTagString(IEnumerable<OscValue> values)
    {
        var tags = new StringBuilder(",");
        foreach (var value in values) AppendTag(tags, value);
        return tags.ToString();
    }

    public static OscPacket Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new Reader(packet);
        return reader.PeekString() == "#bundle" ? DecodeBundle(ref reader) : DecodeMessage(ref reader);
    }

    public static byte[] Encode(OscPacket packet)
    {
        using var stream = new MemoryStream();
        WritePacket(stream, packet);
        return stream.ToArray();
    }

    private static OscMessage DecodeMessage(ref Reader reader)
    {
        var address = reader.ReadString();
        if (!address.StartsWith('/')) throw new OscProtocolException("OSC address must start with '/'.");
        var tags = reader.ReadString();
        if (!tags.StartsWith(',')) throw new OscProtocolException("OSC type tag string is missing.");
        var index = 1;
        return new OscMessage(address, ReadArguments(tags, ref index, ref reader, false));
    }

    private static ImmutableArray<OscValue> ReadArguments(string tags, ref int index, ref Reader reader, bool inArray)
    {
        var values = ImmutableArray.CreateBuilder<OscValue>();
        while (index < tags.Length)
        {
            var tag = tags[index++];
            if (tag == ']')
            {
                if (!inArray) throw new OscProtocolException("Unexpected array terminator.");
                return values.ToImmutable();
            }
            if (tag == '[') { values.Add(OscValue.Array(ReadArguments(tags, ref index, ref reader, true))); continue; }
            values.Add(tag switch
            {
                'i' => new(OscValueKind.Int32, reader.ReadInt32()),
                'f' => new(OscValueKind.Float32, reader.ReadSingle()),
                's' or 'S' => new(OscValueKind.String, reader.ReadString()),
                'b' => new(OscValueKind.Blob, reader.ReadBlob()),
                'h' => new(OscValueKind.Int64, reader.ReadInt64()),
                't' => new(OscValueKind.Timetag, reader.ReadUInt64()),
                'd' => new(OscValueKind.Float64, reader.ReadDouble()),
                'c' => new(OscValueKind.Char, char.ConvertFromUtf32(reader.ReadInt32())),
                'r' => new(OscValueKind.Rgba, reader.ReadUInt32()),
                'm' => new(OscValueKind.Midi, reader.ReadUInt32()),
                'T' => new(OscValueKind.True, true),
                'F' => new(OscValueKind.False, false),
                'N' => new(OscValueKind.Nil, null),
                'I' => new(OscValueKind.Impulse, null),
                _ => throw new OscProtocolException($"Unsupported OSC type tag '{tag}'.")
            });
        }
        if (inArray) throw new OscProtocolException("OSC array is not terminated.");
        return values.ToImmutable();
    }

    private static OscBundle DecodeBundle(ref Reader reader)
    {
        if (reader.ReadString() != "#bundle") throw new OscProtocolException("Invalid OSC bundle header.");
        var timetag = reader.ReadUInt64();
        var packets = new List<OscPacket>();
        while (reader.Remaining > 0)
        {
            var size = reader.ReadInt32();
            if (size <= 0 || size > reader.Remaining) throw new OscProtocolException("Invalid OSC bundle element size.");
            packets.Add(Decode(reader.ReadBytes(size)));
        }
        return new OscBundle(timetag, packets);
    }

    private static void WritePacket(Stream stream, OscPacket packet)
    {
        if (packet is OscMessage message) WriteMessage(stream, message);
        else if (packet is OscBundle bundle) WriteBundle(stream, bundle);
        else throw new OscProtocolException("Unknown OSC packet type.");
    }

    private static void WriteMessage(Stream stream, OscMessage message)
    {
        if (!message.Address.StartsWith('/')) throw new OscProtocolException("OSC address must start with '/'.");
        WriteString(stream, message.Address);
        WriteString(stream, GetTypeTagString(message.Arguments));
        foreach (var argument in message.Arguments) WriteValue(stream, argument);
    }

    private static void WriteBundle(Stream stream, OscBundle bundle)
    {
        WriteString(stream, "#bundle");
        WriteUInt64(stream, bundle.Timetag);
        foreach (var packet in bundle.Packets)
        {
            var bytes = Encode(packet);
            WriteInt32(stream, bytes.Length);
            stream.Write(bytes);
        }
    }

    private static void AppendTag(StringBuilder builder, OscValue value)
    {
        if (value.Kind == OscValueKind.Array)
        {
            builder.Append('[');
            foreach (var item in (IEnumerable<OscValue>)value.Value!) AppendTag(builder, item);
            builder.Append(']');
            return;
        }
        builder.Append(value.Kind switch { OscValueKind.Int32 => 'i', OscValueKind.Float32 => 'f', OscValueKind.String => 's', OscValueKind.Blob => 'b', OscValueKind.Int64 => 'h', OscValueKind.Timetag => 't', OscValueKind.Float64 => 'd', OscValueKind.Char => 'c', OscValueKind.Rgba => 'r', OscValueKind.Midi => 'm', OscValueKind.True => 'T', OscValueKind.False => 'F', OscValueKind.Nil => 'N', OscValueKind.Impulse => 'I', _ => throw new OscProtocolException("Unsupported value kind.") });
    }

    private static void WriteValue(Stream stream, OscValue value)
    {
        switch (value.Kind)
        {
            case OscValueKind.Int32: WriteInt32(stream, Convert.ToInt32(value.Value)); break;
            case OscValueKind.Float32: WriteUInt32(stream, BitConverter.SingleToUInt32Bits(Convert.ToSingle(value.Value))); break;
            case OscValueKind.String: WriteString(stream, Convert.ToString(value.Value) ?? string.Empty); break;
            case OscValueKind.Blob: WriteBlob(stream, (byte[])value.Value!); break;
            case OscValueKind.Int64: WriteUInt64(stream, unchecked((ulong)Convert.ToInt64(value.Value))); break;
            case OscValueKind.Timetag: WriteUInt64(stream, Convert.ToUInt64(value.Value)); break;
            case OscValueKind.Float64: WriteUInt64(stream, BitConverter.DoubleToUInt64Bits(Convert.ToDouble(value.Value))); break;
            case OscValueKind.Char: WriteInt32(stream, char.ConvertToUtf32(Convert.ToString(value.Value)!, 0)); break;
            case OscValueKind.Rgba or OscValueKind.Midi: WriteUInt32(stream, Convert.ToUInt32(value.Value)); break;
            case OscValueKind.Array:
                foreach (var item in (IEnumerable<OscValue>)value.Value!) WriteValue(stream, item);
                break;
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes); stream.WriteByte(0);
        while (stream.Position % 4 != 0) stream.WriteByte(0);
    }
    private static void WriteBlob(Stream stream, byte[] value) { WriteInt32(stream, value.Length); stream.Write(value); while (stream.Position % 4 != 0) stream.WriteByte(0); }
    private static void WriteInt32(Stream s, int v) => WriteUInt32(s, unchecked((uint)v));
    private static void WriteUInt32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); s.Write(b); }
    private static void WriteUInt64(Stream s, ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); s.Write(b); }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;
        public int Remaining => _data.Length - _position;
        public string PeekString() { var copy = this; return copy.ReadString(); }
        public string ReadString() { var end = _data[_position..].IndexOf((byte)0); if (end < 0) throw new OscProtocolException("Unterminated OSC string."); var result = Encoding.UTF8.GetString(_data.Slice(_position, end)); _position = Align4(_position + end + 1); Require(0); return result; }
        public int ReadInt32() => unchecked((int)ReadUInt32());
        public uint ReadUInt32() { Require(4); var value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]); _position += 4; return value; }
        public long ReadInt64() => unchecked((long)ReadUInt64());
        public ulong ReadUInt64() { Require(8); var value = BinaryPrimitives.ReadUInt64BigEndian(_data[_position..]); _position += 8; return value; }
        public float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadUInt32());
        public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadUInt64());
        public byte[] ReadBlob() { var count = ReadInt32(); if (count < 0) throw new OscProtocolException("Negative blob length."); var result = ReadBytes(count).ToArray(); _position = Align4(_position); Require(0); return result; }
        public ReadOnlySpan<byte> ReadBytes(int count) { Require(count); var result = _data.Slice(_position, count); _position += count; return result; }
        private void Require(int count) { if (count < 0 || _position + count > _data.Length) throw new OscProtocolException("OSC packet is truncated."); }
        private static int Align4(int value) => (value + 3) & ~3;
    }
}
