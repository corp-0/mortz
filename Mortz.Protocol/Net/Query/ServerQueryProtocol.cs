using System.Buffers.Binary;
using System.Text;
using Mortz.Protocol.Net.Names;

namespace Mortz.Protocol.Net.Query;

public static class ServerQueryProtocol
{
    public const int VERSION = 1;
    public const int PORT_OFFSET = 1;
    public const int MAX_DATAGRAM_BYTES = 1_400;
    public const int MAX_RESPONSE_BYTES = 65_536;
    public const int MAX_FRAGMENTS = 64;
    public const int MAX_RULES = 128;
    public const int MAX_RULE_VALUE_BYTES = 256;
    public const int MAX_TEXT_LENGTH = 48;
    public const int MAX_CHALLENGE_RETRIES = 2;
    public const byte INFO_REQUEST = 0x54;
    public const byte RULES_REQUEST = 0x56;
    public const byte INFO_RESPONSE = 0x49;
    public const byte RULES_RESPONSE = 0x45;
    public const byte CHALLENGE_RESPONSE = 0x41;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly byte[] _infoQuery = [.. "Source Engine Query\0"u8];

    public static int QueryPort(int gamePort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gamePort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(gamePort, ushort.MaxValue);
        return gamePort + PORT_OFFSET;
    }

    public static byte[] EncodeInfoRequest(int? challenge = null)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        WriteHeader(writer, INFO_REQUEST);
        writer.Write(_infoQuery);
        if (challenge.HasValue)
            writer.Write(challenge.Value);
        return stream.ToArray();
    }

    public static byte[] EncodeRulesRequest(int challenge = -1) => EncodeChallengePacket(RULES_REQUEST, challenge);
    public static byte[] EncodeChallenge(int challenge) => EncodeChallengePacket(CHALLENGE_RESPONSE, challenge);

    public static bool TryDecodeRequest(ReadOnlySpan<byte> packet, out byte kind, out int? challenge)
    {
        kind = 0;
        challenge = null;
        if (packet.Length < 5 || BinaryPrimitives.ReadInt32LittleEndian(packet) != -1)
            return false;
        kind = packet[4];
        if (kind == RULES_REQUEST && packet.Length == 9)
        {
            challenge = BinaryPrimitives.ReadInt32LittleEndian(packet[5..]);
            return true;
        }
        if (kind != INFO_REQUEST || packet.Length != 25 && packet.Length != 29 ||
            !packet.Slice(5, _infoQuery.Length).SequenceEqual(_infoQuery))
            return false;
        if (packet.Length == 29)
            challenge = BinaryPrimitives.ReadInt32LittleEndian(packet[25..]);
        return true;
    }

    public static bool TryDecodeChallenge(ReadOnlySpan<byte> packet, out int challenge)
    {
        challenge = 0;
        if (!HasHeader(packet, CHALLENGE_RESPONSE) || packet.Length != 9)
            return false;
        challenge = BinaryPrimitives.ReadInt32LittleEndian(packet[5..]);
        return true;
    }

    public static byte[] EncodeInfoResponse(ServerInfo info)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        WriteHeader(writer, INFO_RESPONSE);
        writer.Write((byte)17);
        WriteString(writer, SafeName.Sanitize(info.Name, MAX_TEXT_LENGTH));
        WriteString(writer, SafeName.Sanitize(info.Map, MAX_TEXT_LENGTH));
        WriteString(writer, "mortz");
        WriteString(writer, "Mortz");
        writer.Write((ushort)(info.AppId & ushort.MaxValue));
        writer.Write((byte)Math.Clamp(info.Players, 0, byte.MaxValue));
        writer.Write((byte)Math.Clamp(info.MaxPlayers, 0, byte.MaxValue));
        writer.Write((byte)0);
        writer.Write((byte)'d');
        writer.Write((byte)(OperatingSystem.IsWindows() ? 'w' : 'l'));
        writer.Write((byte)0);
        writer.Write((byte)0);
        WriteString(writer, info.Version);
        writer.Write((byte)0x81); // EDF: game port and full game ID.
        writer.Write(checked((ushort)info.GamePort));
        writer.Write((ulong)info.AppId);
        return stream.ToArray();
    }

    public static bool TryDecodeInfo(ReadOnlySpan<byte> packet, int fallbackGamePort, out ServerInfo info)
    {
        info = null!;
        if (!HasHeader(packet, INFO_RESPONSE))
            return false;
        try
        {
            PacketReader reader = new(packet[5..]);
            reader.Byte();
            string name = SafeName.Sanitize(reader.String(), MAX_TEXT_LENGTH);
            string map = SafeName.Sanitize(reader.String(), MAX_TEXT_LENGTH);
            reader.String();
            reader.String();
            uint appId = reader.UInt16();
            int players = reader.Byte();
            int capacity = reader.Byte();
            reader.Byte();
            reader.Byte();
            reader.Byte();
            if (reader.Byte() > 1 || reader.Byte() > 1)
                return false;
            string version = reader.String();
            int port = fallbackGamePort;
            if (!reader.Finished)
            {
                byte extra = reader.Byte();
                if ((extra & ~0xF1) != 0)
                    return false;
                if ((extra & 0x80) != 0)
                    port = reader.UInt16();
                if ((extra & 0x10) != 0)
                    reader.UInt64();
                if ((extra & 0x40) != 0)
                {
                    reader.UInt16();
                    reader.String();
                }
                if ((extra & 0x20) != 0)
                    reader.String();
                if ((extra & 0x01) != 0)
                    appId = (uint)(reader.UInt64() & 0xFFFFFF);
            }
            if (!reader.Finished || port is < 1 or > ushort.MaxValue || players > capacity)
                return false;
            info = new ServerInfo(name, "", map, players, capacity, false, false, port, 0, 0,
                appId, version, MetadataValid: false);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            return false;
        }
    }

    public static byte[] EncodeRulesResponse(IReadOnlyDictionary<string, string> rules)
    {
        if (rules.Count > MAX_RULES)
            throw new ArgumentOutOfRangeException(nameof(rules));
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        WriteHeader(writer, RULES_RESPONSE);
        writer.Write((ushort)rules.Count);
        foreach ((string key, string value) in rules)
        {
            WriteString(writer, key);
            WriteString(writer, value);
        }
        if (stream.Length > MAX_RESPONSE_BYTES)
            throw new ArgumentOutOfRangeException(nameof(rules));
        return stream.ToArray();
    }

    public static byte[] EncodeRulesResponse(ServerInfo info) => EncodeRulesResponse(ServerQueryMetadata.ToRules(info));

    public static bool TryDecodeRules(ReadOnlySpan<byte> packet, out Dictionary<string, string> rules)
    {
        rules = new(StringComparer.Ordinal);
        if (!HasHeader(packet, RULES_RESPONSE))
            return false;
        try
        {
            PacketReader reader = new(packet[5..]);
            int count = reader.UInt16();
            if (count > MAX_RULES)
                return false;
            for (int i = 0; i < count; i++)
            {
                string key = reader.String();
                string value = reader.String();
                if (key.Length == 0 || !rules.TryAdd(key, value))
                    return false;
            }
            return reader.Finished;
        }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            return false;
        }
    }

    public static IReadOnlyList<byte[]> SplitResponse(byte[] response, uint requestId)
    {
        if (response.Length > MAX_RESPONSE_BYTES || response.Length < 5)
            throw new ArgumentOutOfRangeException(nameof(response));
        if (response.Length <= MAX_DATAGRAM_BYTES)
            return [response];
        const int PAYLOAD_SIZE = MAX_DATAGRAM_BYTES - 12;
        int count = (response.Length + PAYLOAD_SIZE - 1) / PAYLOAD_SIZE;
        List<byte[]> result = new(count);
        for (int i = 0; i < count; i++)
        {
            int size = Math.Min(PAYLOAD_SIZE, response.Length - i * PAYLOAD_SIZE);
            byte[] packet = new byte[size + 12];
            BinaryPrimitives.WriteInt32LittleEndian(packet, -2);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), requestId & 0x7FFFFFFF);
            packet[8] = (byte)count;
            packet[9] = (byte)i;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10), MAX_DATAGRAM_BYTES);
            response.AsSpan(i * PAYLOAD_SIZE, size).CopyTo(packet.AsSpan(12));
            result.Add(packet);
        }
        return result;
    }

    public static bool HasHeader(ReadOnlySpan<byte> packet, byte kind) =>
        packet.Length is >= 5 and <= MAX_RESPONSE_BYTES &&
        BinaryPrimitives.ReadInt32LittleEndian(packet) == -1 && packet[4] == kind;

    private static byte[] EncodeChallengePacket(byte kind, int challenge)
    {
        byte[] packet = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(packet, -1);
        packet[4] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5), challenge);
        return packet;
    }

    private static void WriteHeader(BinaryWriter writer, byte kind)
    {
        writer.Write(-1);
        writer.Write(kind);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (value.Contains('\0') || _utf8.GetByteCount(value) > MAX_RULE_VALUE_BYTES)
            throw new ArgumentOutOfRangeException(nameof(value));
        writer.Write(_utf8.GetBytes(value));
        writer.Write((byte)0);
    }

    private ref struct PacketReader(ReadOnlySpan<byte> remaining)
    {
        private ReadOnlySpan<byte> _remaining = remaining;
        public bool Finished => _remaining.IsEmpty;
        public byte Byte() => Take(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        public string String()
        {
            int length = _remaining.IndexOf((byte)0);
            if (length is < 0 or > MAX_RULE_VALUE_BYTES)
                throw new InvalidDataException("Invalid A2S string.");
            string result = _utf8.GetString(Take(length));
            Take(1);
            return result;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_remaining.Length < count)
                throw new InvalidDataException("Truncated A2S response.");
            ReadOnlySpan<byte> value = _remaining[..count];
            _remaining = _remaining[count..];
            return value;
        }
    }
}
