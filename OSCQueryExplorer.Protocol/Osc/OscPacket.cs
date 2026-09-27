using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.Osc;

public abstract record OscPacket;
public sealed record OscMessage(string Address, IReadOnlyList<OscValue> Arguments) : OscPacket;
public sealed record OscBundle(ulong Timetag, IReadOnlyList<OscPacket> Packets) : OscPacket;

public sealed class OscProtocolException(string message) : Exception(message);
