using System.Buffers.Binary;

namespace Mortz.Protocol.Net;

public ref struct PacketWriter
{
    private readonly Span<byte> _buffer;
    private readonly bool _writing;

    public int BytesWritten { get; private set; }

    private PacketWriter(Span<byte> buffer, bool writing)
    {
        _buffer = buffer;
        _writing = writing;
    }

    public static PacketWriter ForMeasurement() => new(Span<byte>.Empty, writing: false);

    public static PacketWriter ForBuffer(Span<byte> buffer) => new(buffer, writing: true);

    public void Write(byte value)
    {
        if (_writing)
            _buffer[BytesWritten] = value;
        BytesWritten = checked(BytesWritten + sizeof(byte));
    }

    public void Write(bool value) => Write(value ? (byte)1 : (byte)0);

    public void Write(short value)
    {
        if (_writing)
            BinaryPrimitives.WriteInt16LittleEndian(_buffer[BytesWritten..], value);
        BytesWritten = checked(BytesWritten + sizeof(short));
    }

    public void Write(ushort value)
    {
        if (_writing)
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer[BytesWritten..], value);
        BytesWritten = checked(BytesWritten + sizeof(ushort));
    }

    public void Write(int value)
    {
        if (_writing)
            BinaryPrimitives.WriteInt32LittleEndian(_buffer[BytesWritten..], value);
        BytesWritten = checked(BytesWritten + sizeof(int));
    }
}
