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
    static readonly TimeSpan TransferIdleTimeout = TimeSpan.FromSeconds(30);
    const int DiscoveryPort = 45837;
    const long MaxArchiveBytes = 64L * 1024 * 1024 * 1024;
    static readonly byte[] DiscoveryRequest = "CMCLauncher.InstanceTransfer.1?"u8.ToArray();
    static readonly byte[] LegacyConnectionRequest = "CMC.PairRequest1"u8.ToArray();
    static readonly byte[] ConnectionRequest = "CMC.PairRequest2"u8.ToArray();
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
        CancellationToken ct,
        Func<string, string, CancellationToken, Task<bool>>? confirm = null,
        Action<long, long>? progress = null
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
                    var awaitingReceipt = false;
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
                        var supportsReceipt = supplied.AsSpan().SequenceEqual(ConnectionRequest);
                        if (
                            confirm != null
                            && (
                                supportsReceipt
                                || supplied.AsSpan().SequenceEqual(LegacyConnectionRequest)
                            )
                        )
                        {
                            var nonce = new byte[32];
                            await ReadExactly(stream, nonce, handshake.Token);
                            using var approval = CancellationTokenSource.CreateLinkedTokenSource(
                                ct
                            );
                            approval.CancelAfter(TimeSpan.FromSeconds(60));
                            var verification = ConnectionVerification(
                                certificate.GetCertHash(HashAlgorithmName.SHA256),
                                nonce
                            );
                            var approved = await confirm(
                                    client.Client.RemoteEndPoint!.ToString()!,
                                    verification,
                                    approval.Token
                                )
                                .WaitAsync(approval.Token);
                            await stream.WriteAsync(
                                new byte[] { approved ? (byte)1 : (byte)0 },
                                approval.Token
                            );
                            if (!approved)
                                continue;
                        }
                        else if (!CryptographicOperations.FixedTimeEquals(supplied, secret))
                            continue;
                        var size = new byte[sizeof(long)];
                        BinaryPrimitives.WriteInt64BigEndian(size, payload.Length);
                        await stream.WriteAsync(size, ct);
                        payload.Position = 0;
                        await CopyExactly(
                            payload,
                            stream,
                            payload.Length,
                            ct,
                            progress,
                            TransferIdleTimeout
                        );
                        if (supportsReceipt)
                        {
                            awaitingReceipt = true;
                            using var receiptTimeout =
                                CancellationTokenSource.CreateLinkedTokenSource(ct);
                            receiptTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                            var receipt = new byte[1];
                            try
                            {
                                await ReadExactly(stream, receipt, receiptTimeout.Token);
                            }
                            catch (Exception ex)
                                when (!ct.IsCancellationRequested
                                    && ex is IOException or OperationCanceledException
                                )
                            {
                                throw new IOException(
                                    "The receiving device did not confirm the import.",
                                    ex
                                );
                            }
                            if (receipt[0] != 1)
                                throw new IOException(
                                    "The receiving device could not import the archive."
                                );
                        }
                        return;
                    }
                    catch (Exception ex)
                        when (!ct.IsCancellationRequested
                            && !awaitingReceipt
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

    static bool TryParseEndpoint(string address, out IPEndPoint? endpoint) =>
        IPEndPoint.TryParse(address, out endpoint)
        && endpoint.AddressFamily == AddressFamily.InterNetwork
        && endpoint.Port != 0
        && address.Contains(':');

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

    public static Task<IReadOnlyList<Instance>> Receive(
        Store store,
        string address,
        string code,
        Action installing,
        CancellationToken ct
    ) => ReceiveCore(store, address, code, null, installing, ct, null);

    public static Task<IReadOnlyList<Instance>> RequestReceive(
        Store store,
        string address,
        Action<string> awaitingApproval,
        Action installing,
        CancellationToken ct,
        Action<long, long>? progress = null
    ) => ReceiveCore(store, address, null, awaitingApproval, installing, ct, progress);

    static async Task<IReadOnlyList<Instance>> ReceiveCore(
        Store store,
        string address,
        string? code,
        Action<string>? awaitingApproval,
        Action installing,
        CancellationToken ct,
        Action<long, long>? progress
    )
    {
        if (!TryParseEndpoint(address.Trim(), out var endpoint))
            throw new FormatException("Enter an IPv4 address and port.");
        byte[]? fingerprint = null;
        byte[]? secret = null;
        if (awaitingApproval == null)
        {
            if (!TryNormalizePairingCode(code, out var normalized))
                throw new FormatException(
                    "Copy the full pairing code from the sending device and try again."
                );
            fingerprint = Convert.FromHexString(normalized[..32]);
            secret = Convert.FromHexString(normalized[32..]);
        }
        using var client = new TcpClient();
        using var stream = await Connect(client, endpoint!, fingerprint, ct);
        var size = new byte[sizeof(long)];
        using (var pairing = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            pairing.CancelAfter(TimeSpan.FromSeconds(awaitingApproval == null ? 10 : 65));
            try
            {
                if (awaitingApproval != null)
                {
                    var nonce = RandomNumberGenerator.GetBytes(32);
                    var certificateHash = stream.RemoteCertificate!.GetCertHash(
                        HashAlgorithmName.SHA256
                    );
                    await stream.WriteAsync(ConnectionRequest, pairing.Token);
                    await stream.WriteAsync(nonce, pairing.Token);
                    awaitingApproval(ConnectionVerification(certificateHash, nonce));
                    var decision = new byte[1];
                    await ReadExactly(stream, decision, pairing.Token);
                    if (decision[0] != 1)
                        throw new IOException(
                            "The sending device declined the connection request."
                        );
                }
                else
                    await stream.WriteAsync(secret!, pairing.Token);
                await ReadExactly(stream, size, pairing.Token);
            }
            catch (EndOfStreamException ex)
            {
                throw new IOException(
                    "The sender rejected pairing or disconnected. Make sure both devices use the same launcher version.",
                    ex
                );
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new IOException(
                    "The sender did not respond to pairing. Keep the sending page open and try again.",
                    ex
                );
            }
        }
        var length = BinaryPrimitives.ReadInt64BigEndian(size);
        if (length <= 0 || length > MaxArchiveBytes)
            throw new InvalidDataException("Invalid transfer archive size.");

        var archiveReceived = false;
        var file = Path.GetTempFileName();
        try
        {
            using var payload = File.Open(
                file,
                FileMode.Truncate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            try
            {
                await CopyExactly(stream, payload, length, ct, progress, TransferIdleTimeout);
            }
            catch (IOException ex) when (!ct.IsCancellationRequested)
            {
                throw new IOException(
                    "Could not receive the complete archive. Check the connection and free disk space, then retry.",
                    ex
                );
            }
            archiveReceived = true;
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
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (var index = 0; index < profiles.Length; index++)
                {
                    var prefix = batch == null ? "" : EntryPrefix(index);
                    imported.Add(
                        await Import(
                            store,
                            profiles[index],
                            batchNames,
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
            if (awaitingApproval != null)
                await SendImportReceipt(stream, true);
            return imported;
        }
        catch
        {
            if (archiveReceived && awaitingApproval != null)
                await SendImportReceipt(stream, false);
            throw;
        }
        finally
        {
            File.Delete(file);
        }
    }

    static async Task SendImportReceipt(SslStream stream, bool imported)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await stream.WriteAsync(new byte[] { imported ? (byte)1 : (byte)0 }, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // A lost receipt cannot change the receiver's import outcome.
        }
    }

    static string EntryPrefix(int index) =>
        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";

    static async Task<Instance> Import(
        Store store,
        Profile profile,
        HashSet<string> batchNames,
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
            PendingTransferredLoaderSetup = profile.Loader is "forge" or "neoforge",
            TransferredFromDevice = true,
            StorageRoot = store.MinecraftRoot,
        };
        var versions = Store.SafePath(instance.StorageRoot, "versions");
        var stage = Store.SafePath(versions, ".transfer-" + instance.Id);
        Directory.CreateDirectory(stage);
        try
        {
            installing();
            await Unpack(entries, prefix, stage, ct);
            for (var suffix = 2; ; suffix++)
            {
                try
                {
                    if (!batchNames.Contains(instance.Name))
                    {
                        store.EnsureUniqueInstanceName(instance);
                        break;
                    }
                    instance.Name = $"{profile.Name} ({suffix})";
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
            instance.Directory = Store.SafePath(versions, instance.Id);
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, store.GameDirectory(instance));
            batchNames.Add(instance.Name);
            return instance;
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
        }
    }

    // The sender and receiver compare a value tied to both the certificate and the request.
    static string ConnectionVerification(byte[] certificateHash, byte[] nonce)
    {
        var input = new byte[certificateHash.Length + nonce.Length];
        certificateHash.CopyTo(input, 0);
        nonce.CopyTo(input, certificateHash.Length);
        return Convert.ToHexString(SHA256.HashData(input).AsSpan(0, 6));
    }

    static async Task<SslStream> Connect(
        TcpClient client,
        IPEndPoint endpoint,
        byte[]? fingerprint,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await client.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException(
                "Connection to the sender timed out. Check the address and network.",
                ex
            );
        }
        catch (SocketException ex)
        {
            throw new IOException(
                "Cannot connect to the sender. Check the address, network, and firewall.",
                ex
            );
        }
        var fingerprintMismatch = false;
        var stream = new SslStream(
            client.GetStream(),
            false,
            (_, cert, _, _) =>
            {
                if (cert == null)
                    return false;
                fingerprintMismatch =
                    fingerprint != null
                    && !CryptographicOperations.FixedTimeEquals(
                        fingerprint,
                        cert.GetCertHash(HashAlgorithmName.SHA256).AsSpan(0, fingerprint.Length)
                    );
                return !fingerprintMismatch;
            }
        );
        try
        {
            await stream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = endpoint.Address.ToString() },
                timeout.Token
            );
            return stream;
        }
        catch (System.Security.Authentication.AuthenticationException ex) when (fingerprintMismatch)
        {
            stream.Dispose();
            throw new IOException(
                "The pairing code does not match this sender. Copy its current pairing code.",
                ex
            );
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stream.Dispose();
            throw new IOException(
                "Connection to the sender timed out. Check the address and network.",
                ex
            );
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
        CancellationToken ct,
        Action<long, long>? progress = null,
        TimeSpan? idleTimeout = null
    )
    {
        using var idle = idleTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        var transferToken = idle?.Token ?? ct;
        try
        {
            var buffer = new byte[81920];
            long transferred = 0;
            var update = progress == null ? null : System.Diagnostics.Stopwatch.StartNew();
            progress?.Invoke(0, length);
            while (transferred < length)
            {
                if (idleTimeout is { } timeout)
                    idle!.CancelAfter(timeout);
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - transferred)),
                    transferToken
                );
                if (read == 0)
                    throw new EndOfStreamException();
                await destination.WriteAsync(buffer.AsMemory(0, read), transferToken);
                transferred += read;
                if (update != null && (transferred == length || update.ElapsedMilliseconds >= 150))
                {
                    progress!(transferred, length);
                    update.Restart();
                }
            }
        }
        catch (OperationCanceledException ex)
            when (idle?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                "No transfer progress for 30 seconds. Check the connection and retry.",
                ex
            );
        }
    }
}
