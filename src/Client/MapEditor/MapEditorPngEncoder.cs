using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Mortz.Client.MapEditor;

public delegate void MapEditorRgbaRowRenderer(int row, Span<byte> destination);

public readonly record struct MapEditorPngEncodingMetrics(
    int ScanlineBufferBytes,
    int IdatBufferBytes);

public static class MapEditorPngEncoder
{
    private static ReadOnlySpan<byte> Signature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static MapEditorPngEncodingMetrics EncodeRgba(Stream output,
        int width, int height, MapEditorRgbaRowRenderer renderRow)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(renderRow);
        if (!output.CanWrite)
            throw new ArgumentException("PNG output stream must be writable.", nameof(output));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int rowLength = checked(width * 4);
        byte[] row = GC.AllocateUninitializedArray<byte>(rowLength);

        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
        using (PngIdatStream idat = new(output))
        using (ZLibStream zlib = new(idat, CompressionLevel.Optimal, leaveOpen: true))
        {
            ReadOnlySpan<byte> filter = [0];
            for (int y = 0; y < height; y++)
            {
                renderRow(y, row);
                zlib.Write(filter);
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IEND", []);
        return new MapEditorPngEncodingMetrics(row.Length, PngIdatStream.CHUNK_SIZE);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, data.Length);
        output.Write(integer);
        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = 0xffffffff;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xffffffff;
        BinaryPrimitives.WriteUInt32BigEndian(integer, crc);
        output.Write(integer);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xedb88320 & mask);
            }
        }
        return crc;
    }

    private sealed class PngIdatStream : Stream
    {
        internal const int CHUNK_SIZE = 64 * 1024;
        private readonly Stream _output;
        private readonly byte[] _buffer = new byte[CHUNK_SIZE];
        private int _length;

        public PngIdatStream(Stream output) => _output = output;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => FlushChunk();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(_buffer.Length - _length, buffer.Length);
                buffer[..count].CopyTo(_buffer.AsSpan(_length));
                _length += count;
                buffer = buffer[count..];
                if (_length == _buffer.Length)
                    FlushChunk();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                FlushChunk();
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void FlushChunk()
        {
            if (_length == 0)
                return;
            WriteChunk(_output, "IDAT", _buffer.AsSpan(0, _length));
            _length = 0;
        }
    }
}
