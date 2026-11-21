using System.Security.Cryptography;

namespace CMCLauncher;

internal static class StreamHash
{
    public static async Task<byte[]> ComputeAsync(HashAlgorithm algorithm, Stream stream, CancellationToken ct = default)
    {
        using (algorithm)
            return await algorithm.ComputeHashAsync(stream, ct).ConfigureAwait(false);
    }
}

#if NET6_0
internal static class LegacyStreamExtensions
{
    public static async Task<int> ReadAtLeastAsync(
        this Stream stream, Memory<byte> buffer, int minimum, bool throwOnEndOfStream, CancellationToken ct)
    {
        var total = 0;
        while (total < minimum)
        {
            var count = await stream.ReadAsync(buffer[total..], ct).ConfigureAwait(false);
            if (count == 0)
            {
                if (throwOnEndOfStream)
                    throw new EndOfStreamException();

                break;
            }

            total += count;
        }

        return total;
    }

    public static int ReadAtLeast(this Stream stream, Span<byte> buffer, int minimum, bool throwOnEndOfStream)
    {
        var total = 0;
        while (total < minimum)
        {
            var count = stream.Read(buffer[total..]);
            if (count == 0)
            {
                if (throwOnEndOfStream)
                    throw new EndOfStreamException();

                break;
            }

            total += count;
        }

        return total;
    }

    public static Task ReadExactlyAsync(this Stream stream, Memory<byte> buffer, CancellationToken ct) =>
        stream.ReadAtLeastAsync(buffer, buffer.Length, true, ct);

    public static Task<string> ReadToEndAsync(this StreamReader reader, CancellationToken ct) =>
        reader.ReadToEndAsync().WaitAsync(ct);

    public static Task<string?> ReadLineAsync(this StreamReader reader, CancellationToken ct) =>
        reader.ReadLineAsync().WaitAsync(ct);
}
#endif
