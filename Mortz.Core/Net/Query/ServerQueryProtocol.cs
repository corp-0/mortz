using System.Buffers.Binary;
using System.Text;
using Mortz.Core.Net.Names;

namespace Mortz.Core.Net.Query;

/// <summary>The ask/answer datagrams behind the server browser. Not a
/// [NetMessage]: those are gated on SCHEMA_HASH, and a query has to answer
/// across incompatible builds.</summary>
public static class ServerQueryProtocol
{
    /// <summary>Bumped only for query datagram changes, separate from
    /// NetConfig.PROTOCOL_VERSION.</summary>
    public const byte VERSION = 2;

    /// <summary>Direct connect relies on this: players type the port they
    /// would join, not the query port.</summary>
    public const int PORT_OFFSET = 1;

    /// <summary>Requests are padded to this and shorter datagrams ignored.
    /// The padding only caps the amplification ratio, a response is still ~7x
    /// this; what keeps the responder useless as a reflector is the global cap
    /// in ServerQueryRateLimiter. Relaxing that cap reopens amplification.</summary>
    public const int REQUEST_BYTES = 64;

    /// <summary>Bounds what a client will parse; the responder truncates to fit.</summary>
    public const int MAX_RESPONSE_BYTES = 512;

    /// <summary>Mode and map labels have no bound of their own, so clipping
    /// them here is fine. Server names are already under this.</summary>
    public const int MAX_TEXT_LENGTH = 48;

    private static readonly byte[] _magic = "MZQ1"u8.ToArray();
    private const byte KIND_REQUEST = 1;
    private const byte KIND_RESPONSE = 2;

    public static int QueryPort(int gamePort) => gamePort + PORT_OFFSET;

    public static byte[] EncodeRequest(uint nonce)
    {
        byte[] datagram = new byte[REQUEST_BYTES];
        using MemoryStream stream = new MemoryStream(datagram);
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        WriteHeader(writer, KIND_REQUEST, nonce);
        return datagram;
    }

    public static bool TryDecodeRequest(byte[] datagram, out uint nonce)
    {
        nonce = 0;
        if (datagram.Length != REQUEST_BYTES)
            return false;
        return TryReadHeader(datagram, KIND_REQUEST, out nonce, out _);
    }

    public static byte[] EncodeResponse(uint nonce, ServerInfo info)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        WriteHeader(writer, KIND_RESPONSE, nonce);
        writer.Write(info.ProtocolVersion);
        writer.Write(info.SchemaHash);
        writer.Write((ushort)Math.Clamp(info.GamePort, 0, ushort.MaxValue));
        writer.Write((byte)Math.Clamp(info.Players, 0, byte.MaxValue));
        writer.Write((byte)Math.Clamp(info.MaxPlayers, 0, byte.MaxValue));
        writer.Write(info.InLobby);
        writer.Write(info.AllowJoinInProgress);
        writer.Write(SafeName.Sanitize(info.Name, MAX_TEXT_LENGTH));
        writer.Write(SafeName.Sanitize(info.Mode, MAX_TEXT_LENGTH));
        writer.Write(SafeName.Sanitize(info.Map, MAX_TEXT_LENGTH));
        return stream.ToArray();
    }

    public static bool TryDecodeResponse(byte[] datagram, out ServerQueryReply reply)
    {
        reply = default;
        if (datagram.Length > MAX_RESPONSE_BYTES)
            return false;
        if (!TryReadHeader(datagram, KIND_RESPONSE, out uint nonce, out int offset))
            return false;
        try
        {
            using MemoryStream stream = new MemoryStream(datagram, offset, datagram.Length - offset);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8);
            int protocolVersion = reader.ReadInt32();
            ulong schemaHash = reader.ReadUInt64();
            int gamePort = reader.ReadUInt16();
            int players = reader.ReadByte();
            int maxPlayers = reader.ReadByte();
            bool inLobby = reader.ReadBoolean();
            bool allowJoinInProgress = reader.ReadBoolean();
            string name = ReadText(reader);
            string mode = ReadText(reader);
            string map = ReadText(reader);
            reply = new ServerQueryReply(nonce, new ServerInfo(name, mode, map, players,
                maxPlayers, inLobby, allowJoinInProgress, gamePort, protocolVersion, schemaHash));
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException)
        {
            return false;
        }
    }

    private static void WriteHeader(BinaryWriter writer, byte kind, uint nonce)
    {
        writer.Write(_magic);
        writer.Write(VERSION);
        writer.Write(kind);
        writer.Write(nonce);
    }

    private static bool TryReadHeader(byte[] datagram, byte kind, out uint nonce, out int offset)
    {
        nonce = 0;
        offset = _magic.Length + 2 + sizeof(uint);
        if (datagram.Length < offset)
            return false;
        for (int i = 0; i < _magic.Length; i++)
        {
            if (datagram[i] != _magic[i])
                return false;
        }
        if (datagram[_magic.Length] != VERSION || datagram[_magic.Length + 1] != kind)
            return false;
        nonce = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(_magic.Length + 2));
        return true;
    }

    // Sanitized on read too: the sender is any box on the internet, not
    // necessarily our responder. Homoglyphs pass through untouched, this stops
    // layout breakage, not lookalike names.
    private static string ReadText(BinaryReader reader) =>
        SafeName.Sanitize(reader.ReadString(), MAX_TEXT_LENGTH);
}
