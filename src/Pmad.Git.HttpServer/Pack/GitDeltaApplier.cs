using System;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Pack;

internal static class GitDeltaApplier
{
    public static GitObjectData Apply(GitObjectData baseObject, ReadOnlySpan<byte> delta)
    {
        var patched = ApplyDeltaCore(baseObject.Content, delta);
        return new GitObjectData(baseObject.Type, patched);
    }

    private static byte[] ApplyDeltaCore(ReadOnlySpan<byte> source, ReadOnlySpan<byte> delta)
    {
        var cursor = 0;
        var baseSize = ReadVariableLength(delta, ref cursor);
        var resultSize = ReadVariableLength(delta, ref cursor);

        if (baseSize != source.Length)
        {
            throw new InvalidDataException("Delta base size mismatch");
        }

        if (resultSize > int.MaxValue)
        {
            throw new InvalidDataException("Delta result size is too large");
        }

        var result = new byte[(int)resultSize];
        var resultIndex = 0;

        while (cursor < delta.Length)
        {
            var opcode = delta[cursor++];
            if ((opcode & 0x80) != 0)
            {
                var copyOffset = 0;
                var copySize = 0;

                if ((opcode & 0x01) != 0) copyOffset |= delta[cursor++];
                if ((opcode & 0x02) != 0) copyOffset |= delta[cursor++] << 8;
                if ((opcode & 0x04) != 0) copyOffset |= delta[cursor++] << 16;
                if ((opcode & 0x08) != 0) copyOffset |= delta[cursor++] << 24;

                if ((opcode & 0x10) != 0) copySize |= delta[cursor++];
                if ((opcode & 0x20) != 0) copySize |= delta[cursor++] << 8;
                if ((opcode & 0x40) != 0) copySize |= delta[cursor++] << 16;
                if (copySize == 0) copySize = 0x10000;

                if (copyOffset < 0 || copyOffset + copySize > source.Length)
                {
                    throw new InvalidDataException("Delta copy instruction exceeds base size");
                }

                source.Slice(copyOffset, copySize).CopyTo(result.AsSpan(resultIndex));
                resultIndex += copySize;
            }
            else if (opcode != 0)
            {
                if (cursor + opcode > delta.Length)
                {
                    throw new InvalidDataException("Delta insert instruction exceeds payload");
                }

                delta.Slice(cursor, opcode).CopyTo(result.AsSpan(resultIndex));
                cursor += opcode;
                resultIndex += opcode;
            }
            else
            {
                throw new InvalidDataException("Invalid delta opcode");
            }
        }

        if (resultIndex != result.Length)
        {
            throw new InvalidDataException("Delta application produced incorrect length");
        }

        return result;
    }

    private static long ReadVariableLength(ReadOnlySpan<byte> data, ref int cursor)
    {
        long result = 0;
        var shift = 0;
        while (cursor < data.Length)
        {
            var b = data[cursor++];
            result |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result;
    }
}
