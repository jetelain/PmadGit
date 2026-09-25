using System;
using System.IO;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Protocol.Pack;

/// <summary>
/// Applies Git binary deltas to base objects.
/// </summary>
public static class GitDeltaApplier
{
    /// <summary>
    /// Applies a delta byte sequence to the specified base object.
    /// </summary>
    /// <param name="baseObject">The base Git object.</param>
    /// <param name="delta">The delta byte payload.</param>
    /// <returns>A new <see cref="GitObjectData"/> representing the patched object.</returns>
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
                long copyOffset = 0;
                var copySize = 0;

                if ((opcode & 0x01) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copyOffset |= delta[cursor++];
                }
                if ((opcode & 0x02) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copyOffset |= (long)delta[cursor++] << 8;
                }
                if ((opcode & 0x04) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copyOffset |= (long)delta[cursor++] << 16;
                }
                if ((opcode & 0x08) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copyOffset |= (long)delta[cursor++] << 24;
                }

                if ((opcode & 0x10) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copySize |= delta[cursor++];
                }
                if ((opcode & 0x20) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copySize |= delta[cursor++] << 8;
                }
                if ((opcode & 0x40) != 0)
                {
                    if (cursor >= delta.Length) throw new InvalidDataException("Truncated delta payload");
                    copySize |= delta[cursor++] << 16;
                }
                if (copySize == 0) copySize = 0x10000;

                if (copyOffset < 0 || copyOffset > int.MaxValue || copyOffset + copySize > source.Length)
                {
                    throw new InvalidDataException("Delta copy instruction exceeds base size");
                }

                if ((long)resultIndex + copySize > result.Length)
                {
                    throw new InvalidDataException("Delta copy instruction exceeds result buffer size");
                }

                source.Slice((int)copyOffset, copySize).CopyTo(result.AsSpan(resultIndex));
                resultIndex += copySize;
            }
            else if (opcode != 0)
            {
                if (cursor + opcode > delta.Length)
                {
                    throw new InvalidDataException("Delta insert instruction exceeds payload");
                }

                if ((long)resultIndex + opcode > result.Length)
                {
                    throw new InvalidDataException("Delta insert instruction exceeds result buffer size");
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

