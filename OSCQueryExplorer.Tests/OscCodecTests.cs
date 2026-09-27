using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Protocol.Osc;

namespace OSCQueryExplorer.Tests;

public sealed class OscCodecTests
{
    [Fact]
    public void Message_RoundTrips_AllCommonTypes()
    {
        var original = new OscMessage("/avatar/parameters/Test", [
            new(OscValueKind.Int32, 42), new(OscValueKind.Float32, 0.5f), new(OscValueKind.String, "日本語"),
            new(OscValueKind.Blob, new byte[] { 1, 2, 3 }), new(OscValueKind.True, true),
            OscValue.Array([new(OscValueKind.Int64, 99L), new(OscValueKind.Float64, 1.25d)])]);

        var decoded = Assert.IsType<OscMessage>(OscCodec.Decode(OscCodec.Encode(original)));

        Assert.Equal(original.Address, decoded.Address);
        Assert.Equal(original.Arguments.Select(x => x.Kind), decoded.Arguments.Select(x => x.Kind));
        Assert.Equal("日本語", decoded.Arguments[2].Value);
    }

    [Fact]
    public void Bundle_RoundTrips()
    {
        var bundle = new OscBundle(1, [new OscMessage("/a", [new(OscValueKind.Int32, 1)]), new OscMessage("/b", [])]);
        var decoded = Assert.IsType<OscBundle>(OscCodec.Decode(OscCodec.Encode(bundle)));
        Assert.Equal(2, decoded.Packets.Count);
    }

    [Fact]
    public void TruncatedPacket_IsRejected() => Assert.Throws<OscProtocolException>(() => OscCodec.Decode([0x2f, 0x61]));

    [Fact]
    public void TypeTagString_IncludesCompleteNestedArrays()
    {
        OscValue[] values = [new(OscValueKind.Int32, 1), OscValue.Array([new(OscValueKind.Float32, 2f), OscValue.Array([new(OscValueKind.True, true)])])];

        Assert.Equal(",i[f[T]]", OscCodec.GetTypeTagString(values));
    }
}
