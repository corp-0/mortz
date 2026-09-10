using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Mortz.Protocol.Hosting;

public enum HostControlKind : byte
{
    READY = 1,
    FAILED = 2,
    STOP = 3,
}

public record HostControlMessage(HostControlKind Kind, string Token,
    string Address = "", int GamePort = 0, int QueryPort = -1, string Reason = "");

public static class HostControl
{
    public const int MAX_FRAME_BYTES = 4096;
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding _encoding = new(false, true);

    public static bool MatchesToken(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(_encoding.GetBytes(supplied), _encoding.GetBytes(expected));

    public static async Task WriteAsync(Stream stream, HostControlMessage message, CancellationToken cancellation = default)
    {
        Validate(message);
        using MemoryStream payload = new();
        await using (BinaryWriter writer = new(payload, _encoding, leaveOpen: true))
        {
            writer.Write((byte)message.Kind);
            writer.Write(message.Token);
            switch (message.Kind)
            {
                case HostControlKind.READY:
                    writer.Write(message.Address);
                    writer.Write(message.GamePort);
                    writer.Write(message.QueryPort);
                    break;
                case HostControlKind.FAILED:
                    writer.Write(message.Reason);
                    break;
            }
        }
        if (payload.Length > MAX_FRAME_BYTES)
            throw new InvalidDataException("Host control message exceeds 4 KiB.");
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, (int)payload.Length);
        payload.GetBuffer().AsSpan(0, (int)payload.Length).CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    public static async Task<HostControlMessage> ReadAsync(Stream stream, CancellationToken cancellation = default)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellation).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is < 1 or > MAX_FRAME_BYTES)
        {
            throw new InvalidDataException("Invalid host control frame length.");
        }
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        using MemoryStream buffer = new(payload);
        using BinaryReader reader = new(buffer, _encoding);
        HostControlKind kind = (HostControlKind)reader.ReadByte();
        string token = reader.ReadString();
        HostControlMessage message = kind switch
        {
            HostControlKind.READY => new(kind, token, reader.ReadString(), reader.ReadInt32(), reader.ReadInt32()),
            HostControlKind.FAILED => new(kind, token, Reason: reader.ReadString()),
            HostControlKind.STOP => new(kind, token),
            _ => throw new InvalidDataException("Unknown host control message."),
        };
        Validate(message);
        if (buffer.Position != buffer.Length)
        {
            throw new InvalidDataException("Trailing host control data.");
        }
        return message;
    }

    private static void Validate(HostControlMessage message)
    {
        if (message.Token.Length != 64 || !message.Token.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("Invalid host control token.");
        }
        if (message.Kind == HostControlKind.READY &&
            (!IPAddress.TryParse(message.Address, out IPAddress? address) || !IPAddress.IsLoopback(address) ||
             message.GamePort is < 1 or > 65535 ||
             (message.QueryPort != -1 && message.QueryPort is < 1 or > 65535) || message.QueryPort == message.GamePort))
        {
            throw new InvalidDataException("Invalid local server endpoint.");
        }
        if (message.Kind == HostControlKind.FAILED &&
            (string.IsNullOrWhiteSpace(message.Reason) || _encoding.GetByteCount(message.Reason) > 3000))
        {
            throw new InvalidDataException("Invalid local server failure reason.");
        }
        if (!Enum.IsDefined(message.Kind))
        {
            throw new InvalidDataException("Unknown host control message.");
        }
    }
}
