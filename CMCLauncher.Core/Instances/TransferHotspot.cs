using System.Security.Cryptography;
using QRCoder;

namespace CMCLauncher;

public sealed class TransferHotspot(string name, string password, Func<Task> stop) : IAsyncDisposable
{
    Func<Task>? release = stop;

    public string Name { get; } = name;
    public string Password { get; } = password;

    public static (string Name, string Password) CreateCredentials() =>
        ("CMCLauncher-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));

    public byte[] ConnectionQrCode()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            $"WIFI:T:WPA;S:{Escape(Name)};P:{Escape(Password)};;", QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(5);
    }

    static string Escape(string value) =>
        string.Concat(value.Select(character =>
            character is '\\' or ';' or ',' or ':' or '"' ? "\\" + character : character.ToString()));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref release, null) is { } close)
            await close();
    }
}
