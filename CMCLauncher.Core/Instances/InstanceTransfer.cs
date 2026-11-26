using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public static class InstanceTransfer
{
    const int DiscoveryPort = 45837;
    const long MaxArchiveBytes = 64L * 1024 * 1024 * 1024;
    static readonly byte[] DiscoveryRequest = "CMCLauncher.InstanceTransfer.1?"u8.ToArray();
    const string Manifest = "instance.json";
    const string BatchManifest = "instances.json";
    const int MaxManifestBytes = 1024 * 1024;

    public sealed record Selection(Instance Instance, IReadOnlyCollection<string> Paths);

    public static IReadOnlyList<(string Label, string[] Paths)> Content { get; } =
    [
        ("Worlds", ["saves"]),
        ("Mods and mod configuration", ["mods", "config", "defaultconfigs", "scripts", "kubejs"]),
        ("Resource packs and shaders", ["resourcepacks", "shaderpacks"]),
        (
            "Game settings and multiplayer servers",
            ["options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat"]
        ),
        ("Screenshots and schematics", ["screenshots", "schematics"]),
    ];

    sealed record Profile(
        string Name,
        string Description,
        string GameVersion,
        string Loader,
        string? LoaderVersion
    );

    public static async Task Send(
        Store store,
        IReadOnlyList<Selection> selections,
        bool discoverable,
        Action<string, string> ready,
        CancellationToken ct
    )
    {
        if (selections.Count == 0)
            throw new ArgumentException("Select at least one instance.", nameof(selections));
        var profiles = selections
            .Select(selection =>
            {
                var profile = CreateProfile(selection.Instance);
                if (SelectionError(selection.Instance) is { } error)
                    throw new InvalidDataException(profile.Name + ": " + error);
                return profile;
            })
            .ToArray();
        var batch = profiles.Length > 1;
        var manifestBytes = batch
            ? JsonSerializer.SerializeToUtf8Bytes(profiles)
            : JsonSerializer.SerializeToUtf8Bytes(profiles[0]);
        if (manifestBytes.Length > (batch ? MaxManifestBytes : 64 * 1024))
            throw new InvalidDataException("Instance metadata is too large.");
        var allowed = Content.SelectMany(x => x.Paths).ToHashSet(StringComparer.Ordinal);
        var file = Path.GetTempFileName();
        try
        {
            using var payload = File.Open(
                file,
                FileMode.Truncate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var entry = zip.CreateEntry(batch ? BatchManifest : Manifest).Open())
                    await entry.WriteAsync(manifestBytes, ct);
                for (var index = 0; index < selections.Count; index++)
                {
                    var selection = selections[index];
                    var root = store.GameDirectory(selection.Instance);
                    var prefix = batch ? EntryPrefix(index) : "";
                    foreach (var path in selection.Paths.Distinct(StringComparer.Ordinal))
                    {
                        if (!allowed.Contains(path))
                            throw new InvalidDataException("Invalid transfer content.");
                        await Pack(zip, root, prefix, path, ct);
                    }
                }
            }
            if (payload.Length > MaxArchiveBytes)
                throw new IOException("Transfer archive is too large.");

            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=CMCLauncher Instance Transfer",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using var generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1)
            );
            using var windowsCertificate = OperatingSystem.IsWindows()
                ? ImportServerCertificate(generated)
                : null;
            var certificate = windowsCertificate ?? generated;
            var secret = RandomNumberGenerator.GetBytes(16);
            var code =
                Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256).AsSpan(0, 16))
                + "-"
                + Convert.ToHexString(secret);
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task discovery = Task.CompletedTask;
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (discoverable)
                    discovery = Advertise(port, lifetime.Token);
                ready(
                    string.Join(
                        "  ",
                        Addresses()
                            .Select(x => x.Address)
                            .DefaultIfEmpty(IPAddress.Loopback)
                            .Select(x => new IPEndPoint(x, port))
                    ),
                    code
                );
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync().WaitAsync(ct);
                    using var stream = new SslStream(client.GetStream());
                    try
                    {
                        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        handshake.CancelAfter(TimeSpan.FromSeconds(5));
                        await stream.AuthenticateAsServerAsync(
                            new SslServerAuthenticationOptions { ServerCertificate = certificate },
                            handshake.Token
                        );
                        var supplied = new byte[secret.Length];
                        await ReadExactly(stream, supplied, handshake.Token);
                        if (!CryptographicOperations.FixedTimeEquals(supplied, secret))
                            continue;
                        var size = new byte[sizeof(long)];
                        BinaryPrimitives.WriteInt64BigEndian(size, payload.Length);
                        await stream.WriteAsync(size, ct);
                        payload.Position = 0;
                        await payload.CopyToAsync(stream, ct);
                        return;
                    }
                    catch (Exception ex)
                        when (!ct.IsCancellationRequested
                            && ex
                                is IOException
                                    or System.Security.Authentication.AuthenticationException
                                    or OperationCanceledException
                        )
                    {
                        // A failed connection leaves the offer available.
                    }
                }
            }
            finally
            {
                lifetime.Cancel();
                listener.Stop();
                await discovery;
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    // Schannel needs a key in the user key store. The imported certificate owns its lifetime.
    static X509Certificate2 ImportServerCertificate(X509Certificate2 source)
    {
        var pfx = source.Export(X509ContentType.Pfx);
        try
        {
            return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    static IEnumerable<UnicastIPAddressInformation> Addresses() =>
        NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(x =>
                x.OperationalStatus == OperationalStatus.Up
                && x.NetworkInterfaceType != NetworkInterfaceType.Loopback
            )
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

    static async Task Advertise(int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(DiscoveryPort);
            var response = new byte[DiscoveryRequest.Length + sizeof(ushort)];
            DiscoveryRequest.CopyTo(response, 0);
            BinaryPrimitives.WriteUInt16BigEndian(
                response.AsSpan(DiscoveryRequest.Length),
                (ushort)port
            );
            while (true)
            {
                var message = await udp.ReceiveAsync(ct);
                if (message.Buffer.AsSpan().SequenceEqual(DiscoveryRequest))
                    await udp.SendAsync(response, message.RemoteEndPoint, ct);
            }
        }
        catch (SocketException)
        { /* Direct connection remains available. */
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public static async Task<string[]> Discover(CancellationToken ct)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var network in Addresses())
        {
            var ip = network.Address.GetAddressBytes();
            var mask = network.IPv4Mask.GetAddressBytes();
            for (var i = 0; i < ip.Length; i++)
                ip[i] |= (byte)~mask[i];
            addresses.Add(new IPAddress(ip));
        }
        foreach (var address in addresses)
        {
            try
            {
                await udp.SendAsync(DiscoveryRequest, new IPEndPoint(address, DiscoveryPort), ct);
            }
            catch (SocketException) { }
        }
        var devices = new HashSet<string>();
        try
        {
            while (true)
            {
                var message = await udp.ReceiveAsync(timeout.Token);
                if (
                    message.Buffer.Length != DiscoveryRequest.Length + sizeof(ushort)
                    || !message
                        .Buffer.AsSpan(0, DiscoveryRequest.Length)
                        .SequenceEqual(DiscoveryRequest)
                )
                    continue;
                var port = BinaryPrimitives.ReadUInt16BigEndian(
                    message.Buffer.AsSpan(DiscoveryRequest.Length)
                );
                if (port != 0)
                    devices.Add(new IPEndPoint(message.RemoteEndPoint.Address, port).ToString());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return devices.OrderBy(x => x).ToArray();
    }

    public static bool TryNormalizePairingCode(string? code, out string normalized)
    {
        normalized = string.Concat(
            (code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '\u200B' && c != '\uFEFF')
        );
        if (
            normalized.Length == 65
            && normalized[32]
                is '-'
                    or '\u2010'
                    or '\u2011'
                    or '\u2012'
                    or '\u2013'
                    or '\u2014'
                    or '\u2212'
                    or '\uFF0D'
        )
            normalized = normalized.Remove(32, 1);
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
    }

    public static async Task<IReadOnlyList<Instance>> Receive(
        Store store,
        LauncherCore core,
        string address,
        string code,
        Action installing,
        CancellationToken ct
    )
    {
        if (
            !IPEndPoint.TryParse(address.Trim(), out var endpoint)
            || endpoint.AddressFamily != AddressFamily.InterNetwork
            || endpoint.Port == 0
            || !address.Contains(':')
        )
            throw new FormatException("Enter an IPv4 address and port.");
        if (!TryNormalizePairingCode(code, out var normalized))
            throw new FormatException(
                "Copy the full pairing code from the sending device and try again."
            );
        var fingerprint = Convert.FromHexString(normalized[..32]);
        var secret = Convert.FromHexString(normalized[32..]);
        using var client = new TcpClient();
        using var stream = await Connect(client, endpoint, fingerprint, ct);
        await stream.WriteAsync(secret, ct);
        var size = new byte[sizeof(long)];
        await ReadExactly(stream, size, ct);
        var length = BinaryPrimitives.ReadInt64BigEndian(size);
        if (length <= 0 || length > MaxArchiveBytes)
            throw new InvalidDataException("Invalid transfer archive size.");

        var file = Path.GetTempFileName();
        try
        {
            using var payload = File.Open(
                file,
                FileMode.Truncate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            await CopyExactly(stream, payload, length, ct);
            payload.Position = 0;
            using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
            var batch = zip.GetEntry(BatchManifest);
            var manifest = batch ?? zip.GetEntry(Manifest);
            if (
                manifest == null
                || manifest.Length > (batch == null ? 64 * 1024 : MaxManifestBytes)
            )
                throw new InvalidDataException("Invalid instance metadata.");
            Profile[] profiles;
            using (var metadata = manifest.Open())
            {
                profiles =
                    batch == null
                        ?
                        [
                            await JsonSerializer.DeserializeAsync<Profile>(
                                metadata,
                                cancellationToken: ct
                            ) ?? throw new InvalidDataException("Missing instance metadata."),
                        ]
                        : await JsonSerializer.DeserializeAsync<Profile[]>(
                            metadata,
                            cancellationToken: ct
                        ) ?? throw new InvalidDataException("Missing instance metadata.");
            }
            if (profiles.Length == 0)
                throw new InvalidDataException("Missing instance metadata.");
            foreach (var profile in profiles)
                Validate(profile);

            var entries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.Ordinal);
            for (var index = 0; index < profiles.Length; index++)
                entries.Add(batch == null ? "" : EntryPrefix(index), []);
            long remaining = MaxArchiveBytes;
            foreach (var entry in zip.Entries)
            {
                if (entry == manifest)
                    continue;
                var prefix =
                    batch == null ? "" : entry.FullName[..(entry.FullName.IndexOf('/') + 1)];
                if (!entries.TryGetValue(prefix, out var files) || entry.Length > remaining)
                    throw new InvalidDataException("Invalid transfer content.");
                remaining -= entry.Length;
                files.Add(entry);
            }

            var imported = new List<Instance>();
            try
            {
                for (var index = 0; index < profiles.Length; index++)
                {
                    var prefix = batch == null ? "" : EntryPrefix(index);
                    imported.Add(
                        await Install(
                            store,
                            core,
                            profiles[index],
                            entries[prefix],
                            prefix,
                            installing,
                            ct
                        )
                    );
                }
                ct.ThrowIfCancellationRequested();
                store.Config.Instances.AddRange(imported);
                await store.Save();
                return imported;
            }
            catch
            {
                foreach (var instance in imported)
                {
                    store.Config.Instances.Remove(instance);
                    Directory.Delete(store.GameDirectory(instance), recursive: true);
                }
                throw;
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    static string EntryPrefix(int index) =>
        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";

    static async Task<Instance> Install(
        Store store,
        LauncherCore core,
        Profile profile,
        IReadOnlyList<ZipArchiveEntry> entries,
        string prefix,
        Action installing,
        CancellationToken ct
    )
    {
        var instance = new Instance
        {
            Name = profile.Name,
            Description = profile.Description,
            GameVersion = profile.GameVersion,
            Loader = profile.Loader,
            LoaderVersion = profile.LoaderVersion,
            StorageRoot = store.MinecraftRoot,
        };
        var versions = Store.SafePath(instance.StorageRoot, "versions");
        var stage = Store.SafePath(versions, ".transfer-" + instance.Id);
        Directory.CreateDirectory(stage);
        string? installed = null;
        try
        {
            await Unpack(entries, prefix, stage, ct);
            for (var suffix = 2; ; suffix++)
            {
                try
                {
                    store.EnsureUniqueInstanceName(instance);
                    break;
                }
                catch
                    when (Directory.Exists(Store.SafePath(versions, instance.Name))
                        || store.Config.Instances.Any(x =>
                            x.Name.Trim().Equals(instance.Name, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                {
                    instance.Name = $"{profile.Name} ({suffix})";
                }
            }
            installing();
            var build =
                instance.Loader == "forge"
                    ? LauncherCore.LoaderBuildPrefix(instance.GameVersion, instance.Loader)
                        + instance.LoaderVersion
                    : instance.LoaderVersion ?? "";
            await core.InstallNewGame(instance, build, ct);
            installed = store.GameDirectory(instance);
            ct.ThrowIfCancellationRequested();
            foreach (var item in Directory.EnumerateFileSystemEntries(stage))
            {
                var destination = Store.SafePath(installed, Path.GetFileName(item));
                if (Directory.Exists(item))
                {
                    if (Directory.Exists(destination))
                        Directory.Delete(destination);
                    Directory.Move(item, destination);
                }
                else
                    File.Move(item, destination, overwrite: true);
            }
            installed = null;
            return instance;
        }
        finally
        {
            if (installed != null && Directory.Exists(installed))
                Directory.Delete(installed, recursive: true);
            Directory.Delete(stage, recursive: true);
        }
    }

    static async Task<SslStream> Connect(
        TcpClient client,
        IPEndPoint endpoint,
        byte[] fingerprint,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token);
        var stream = new SslStream(
            client.GetStream(),
            false,
            (_, cert, _, _) =>
                cert != null
                && CryptographicOperations.FixedTimeEquals(
                    fingerprint,
                    cert.GetCertHash(HashAlgorithmName.SHA256).AsSpan(0, fingerprint.Length)
                )
        );
        try
        {
            await stream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = endpoint.Address.ToString() },
                timeout.Token
            );
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    static async Task Pack(
        ZipArchive zip,
        string root,
        string prefix,
        string relative,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(root, relative);
        if (!File.Exists(path) && !Directory.Exists(path))
            return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            return;
        Store.SafePath(root, relative);
        if (Directory.Exists(path))
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                await Pack(zip, root, prefix, relative + "/" + Path.GetFileName(child), ct);
            return;
        }
        using var source = File.OpenRead(path);
        using var destination = zip.CreateEntry(prefix + relative, CompressionLevel.Fastest).Open();
        await source.CopyToAsync(destination, ct);
    }

    static async Task Unpack(
        IReadOnlyList<ZipArchiveEntry> entries,
        string prefix,
        string stage,
        CancellationToken ct
    )
    {
        var allowed = Content.SelectMany(x => x.Paths).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName[prefix.Length..];
            if (relative.Contains('\\') || !allowed.Contains(relative.Split('/')[0]))
                throw new InvalidDataException("Invalid transfer content.");
            var target = Store.SafePath(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var destination = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            using var source = entry.Open();
            await CopyExactly(source, destination, entry.Length, ct);
            if (source.ReadByte() != -1)
                throw new InvalidDataException("Invalid archive entry size.");
        }
    }

    static Profile CreateProfile(Instance instance) =>
        new(
            instance.Name?.Trim() ?? "",
            instance.Description,
            instance.GameVersion?.Trim() ?? "",
            instance.Loader,
            string.IsNullOrWhiteSpace(instance.LoaderVersion) || instance.Loader == "vanilla"
                ? null
                : instance.LoaderVersion.Trim()
        );

    public static string? SelectionError(Instance instance)
    {
        var error = ProfileError(CreateProfile(instance));
        if (error != null && instance.ImportRoot is { } root && instance.VersionId is { } id)
        {
            try
            {
                if (!File.Exists(Store.SafePath(root, $"versions/{id}/{id}.json")))
                    return "The imported version file is missing. Repair or reimport this instance.";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return "The imported version file cannot be accessed. Repair or reimport this instance.";
            }
        }
        return error;
    }

    static void Validate(Profile profile)
    {
        if (ProfileError(profile) is { } error)
            throw new InvalidDataException(error);
    }

    static string? ProfileError(Profile profile)
    {
        if (
            string.IsNullOrWhiteSpace(profile.Name)
            || profile.Name.Length > 150
            || profile.Name != profile.Name.Trim()
            || profile.Name.IndexOfAny("/\\:*?\"<>|".ToCharArray()) >= 0
            || profile.Name.EndsWith('.')
            || profile.Name.Any(char.IsControl)
            || Regex.IsMatch(
                profile.Name,
                @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|\z)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            )
        )
            return "The instance name cannot be used on the receiving device.";
        if (!IsVersion(profile.GameVersion))
            return "This instance has no valid Minecraft version. Repair or reimport it.";
        if (profile.Loader is not ("vanilla" or "fabric" or "forge" or "neoforge"))
            return "This instance uses a loader that instance transfer does not support.";
        if (
            (profile.LoaderVersion != null && !IsVersion(profile.LoaderVersion))
            || (profile.Loader is "forge" or "neoforge" && profile.LoaderVersion == null)
        )
            return "This instance has no valid loader version. Repair or reimport it.";
        return null;
    }

    static bool IsVersion(string? version) =>
        version is { Length: > 0 and <= 100 }
        && Regex.IsMatch(version, @"\A[A-Za-z0-9][A-Za-z0-9._+-]*\z");

    static async Task ReadExactly(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                throw new EndOfStreamException();
            buffer = buffer[read..];
        }
    }

    static async Task CopyExactly(
        Stream source,
        Stream destination,
        long length,
        CancellationToken ct
    )
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)),
                ct
            );
            if (read == 0)
                throw new EndOfStreamException();
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            length -= read;
        }
    }
}
