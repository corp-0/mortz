namespace Mortz.Protocol.Net;

/// <summary>Called once to measure and once to write; both calls must emit the same fields.</summary>
public delegate void WritePacket<TValue, TOptions>(ref PacketWriter writer, TValue value, TOptions options)
    where TValue : allows ref struct
    where TOptions : allows ref struct;

public static class PacketEncoder
{
    public static byte[] Encode<TValue, TOptions>(TValue value, TOptions options, WritePacket<TValue, TOptions> write)
        where TValue : allows ref struct
        where TOptions : allows ref struct
    {
        PacketWriter measure = PacketWriter.ForMeasurement();
        write(ref measure, value, options);

        byte[] data = new byte[measure.BytesWritten];
        PacketWriter writer = PacketWriter.ForBuffer(data);
        write(ref writer, value, options);
        if (writer.BytesWritten != data.Length)
            throw new InvalidOperationException("Packet size changed between measuring and writing.");
        return data;
    }
}
