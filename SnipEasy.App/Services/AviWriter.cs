using System.IO;

namespace SnipEasy.App.Services;

internal sealed class AviWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _framesPerSecond;
    private readonly List<IndexEntry> _index = [];
    private long _riffSizeOffset;
    private long _avihFramesOffset;
    private long _strhLengthOffset;
    private long _moviListSizeOffset;
    private long _moviDataStart;
    private int _maxFrameSize;
    private bool _disposed;

    public AviWriter(string path, int width, int height, int framesPerSecond)
    {
        _width = width;
        _height = height;
        _framesPerSecond = Math.Clamp(framesPerSecond, 1, 30);
        _stream = File.Create(path);
        _writer = new BinaryWriter(_stream);
        WriteHeaders();
    }

    public int FrameCount => _index.Count;

    public void AddFrame(byte[] jpegBytes)
    {
        ThrowIfDisposed();

        var chunkStart = _stream.Position;
        WriteFourCc("00dc");
        _writer.Write(jpegBytes.Length);
        _writer.Write(jpegBytes);
        if ((jpegBytes.Length & 1) == 1)
        {
            _writer.Write((byte)0);
        }

        _index.Add(new IndexEntry((int)(chunkStart - _moviDataStart), jpegBytes.Length));
        _maxFrameSize = Math.Max(_maxFrameSize, jpegBytes.Length);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        FinalizeFile();
        _writer.Dispose();
        _stream.Dispose();
        _disposed = true;
    }

    private void WriteHeaders()
    {
        WriteFourCc("RIFF");
        _riffSizeOffset = _stream.Position;
        _writer.Write(0);
        WriteFourCc("AVI ");

        WriteList("hdrl", () =>
        {
            WriteChunk("avih", () =>
            {
                _writer.Write(1_000_000 / _framesPerSecond);
                _writer.Write(_width * _height * 3 * _framesPerSecond);
                _writer.Write(0);
                _writer.Write(0x10);
                _avihFramesOffset = _stream.Position;
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(1);
                _writer.Write(_width * _height * 3);
                _writer.Write(_width);
                _writer.Write(_height);
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(0);
            });

            WriteList("strl", () =>
            {
                WriteChunk("strh", () =>
                {
                    WriteFourCc("vids");
                    WriteFourCc("MJPG");
                    _writer.Write(0);
                    _writer.Write((short)0);
                    _writer.Write((short)0);
                    _writer.Write(0);
                    _writer.Write(1);
                    _writer.Write(_framesPerSecond);
                    _writer.Write(0);
                    _strhLengthOffset = _stream.Position;
                    _writer.Write(0);
                    _writer.Write(_width * _height * 3);
                    _writer.Write(-1);
                    _writer.Write(0);
                    _writer.Write((short)0);
                    _writer.Write((short)0);
                    _writer.Write((short)_width);
                    _writer.Write((short)_height);
                });

                WriteChunk("strf", () =>
                {
                    _writer.Write(40);
                    _writer.Write(_width);
                    _writer.Write(_height);
                    _writer.Write((short)1);
                    _writer.Write((short)24);
                    WriteFourCc("MJPG");
                    _writer.Write(_width * _height * 3);
                    _writer.Write(0);
                    _writer.Write(0);
                    _writer.Write(0);
                    _writer.Write(0);
                });
            });
        });

        WriteFourCc("LIST");
        _moviListSizeOffset = _stream.Position;
        _writer.Write(0);
        WriteFourCc("movi");
        _moviDataStart = _stream.Position;
    }

    private void FinalizeFile()
    {
        var idxStart = _stream.Position;
        WriteFourCc("idx1");
        _writer.Write(_index.Count * 16);
        foreach (var entry in _index)
        {
            WriteFourCc("00dc");
            _writer.Write(0x10);
            _writer.Write(entry.Offset);
            _writer.Write(entry.Size);
        }

        var fileEnd = _stream.Position;
        PatchInt32(_riffSizeOffset, (int)(fileEnd - 8));
        PatchInt32(_avihFramesOffset, _index.Count);
        PatchInt32(_strhLengthOffset, _index.Count);
        PatchInt32(_moviListSizeOffset, (int)(idxStart - _moviListSizeOffset - 4));
    }

    private void WriteList(string listType, Action writeContent)
    {
        WriteFourCc("LIST");
        var sizeOffset = _stream.Position;
        _writer.Write(0);
        WriteFourCc(listType);
        var contentStart = _stream.Position;
        writeContent();
        var end = _stream.Position;
        PatchInt32(sizeOffset, (int)(end - contentStart + 4));
        _stream.Position = end;
    }

    private void WriteChunk(string fourCc, Action writeContent)
    {
        WriteFourCc(fourCc);
        var sizeOffset = _stream.Position;
        _writer.Write(0);
        var start = _stream.Position;
        writeContent();
        var end = _stream.Position;
        var size = (int)(end - start);
        if ((size & 1) == 1)
        {
            _writer.Write((byte)0);
            end++;
        }

        PatchInt32(sizeOffset, size);
        _stream.Position = end;
    }

    private void PatchInt32(long offset, int value)
    {
        var current = _stream.Position;
        _stream.Position = offset;
        _writer.Write(value);
        _stream.Position = current;
    }

    private void WriteFourCc(string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        if (bytes.Length != 4)
        {
            throw new ArgumentException("FOURCC must be four characters.", nameof(value));
        }

        _writer.Write(bytes);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AviWriter));
        }
    }

    private sealed record IndexEntry(int Offset, int Size);
}
