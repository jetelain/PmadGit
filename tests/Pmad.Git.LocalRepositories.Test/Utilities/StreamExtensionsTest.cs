using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test.Utilities;

public sealed class StreamExtensionsTest
{
    [Fact]
    public async Task CopyToExactlyAsync_CopiesExactLength()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await source.CopyToExactlyAsync(destination, 5, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, destination.ToArray());
    }

    [Fact]
    public async Task CopyToExactlyAsync_CopiesFullStream_WhenLengthEqualsStreamLength()
    {
        var data = new byte[] { 10, 20, 30 };
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await source.CopyToExactlyAsync(destination, data.Length, CancellationToken.None);

        Assert.Equal(data, destination.ToArray());
    }

    [Fact]
    public async Task CopyToExactlyAsync_DoesNothing_WhenLengthIsZero()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await source.CopyToExactlyAsync(destination, 0, CancellationToken.None);

        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task CopyToExactlyAsync_ThrowsEndOfStreamException_WhenStreamEndsEarly()
    {
        var data = new byte[] { 1, 2, 3 };
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => source.CopyToExactlyAsync(destination, 5, CancellationToken.None));
    }

    [Fact]
    public async Task CopyToExactlyAsync_ThrowsEndOfStreamException_WhenStreamIsEmpty()
    {
        using var source = new MemoryStream();
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => source.CopyToExactlyAsync(destination, 1, CancellationToken.None));
    }

    [Fact]
    public async Task CopyToExactlyAsync_DoesNotCopyBeyondLength_WhenSourceHasMoreData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await source.CopyToExactlyAsync(destination, 3, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());
        Assert.Equal(3, source.Position);
    }

    [Fact]
    public async Task CopyToExactlyAsync_ThrowsOperationCanceledException_WhenCancelled()
    {
        var data = new byte[1024 * 1024];
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.CopyToExactlyAsync(destination, data.Length, cts.Token));
    }

    [Fact]
    public async Task CopyToExactlyAsync_CopiesLargeStream_SpanningMultipleBuffers()
    {
        var data = new byte[200_000];
        new Random(42).NextBytes(data);
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        await source.CopyToExactlyAsync(destination, data.Length, CancellationToken.None);

        Assert.Equal(data, destination.ToArray());
    }
}
