using Windows.Devices.WiFiDirect;

namespace CMCLauncher.WinUI;

static class WindowsTransferHotspot
{
    public static async Task<TransferHotspot> Start(CancellationToken ct)
    {
        var (name, password) = TransferHotspot.CreateCredentials();
        var publisher = new WiFiDirectAdvertisementPublisher();
        var listener = new WiFiDirectConnectionListener();
        var clients = new List<WiFiDirectDevice>();
        var stopping = false;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
        var settings = publisher.Advertisement.LegacySettings;
        settings.IsEnabled = true;
        settings.Ssid = name;
        settings.Passphrase.Password = password;
        listener.ConnectionRequested += OnConnectionRequested;
        publisher.StatusChanged += OnStatusChanged;
        try
        {
            ct.ThrowIfCancellationRequested();
            publisher.Start();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            return new TransferHotspot(name, password, Stop);
        }
        catch
        {
            await Stop();
            throw;
        }
        finally
        {
            publisher.StatusChanged -= OnStatusChanged;
        }

        Task Stop()
        {
            listener.ConnectionRequested -= OnConnectionRequested;
            lock (clients)
            {
                stopping = true;
                foreach (var client in clients)
                    client.Dispose();
                clients.Clear();
            }
            if (publisher.Status is WiFiDirectAdvertisementPublisherStatus.Started
                or WiFiDirectAdvertisementPublisherStatus.Created)
                publisher.Stop();
            return Task.CompletedTask;
        }

        async void OnConnectionRequested(WiFiDirectConnectionListener sender,
            WiFiDirectConnectionRequestedEventArgs args)
        {
            try
            {
                using var request = args.GetConnectionRequest();
                var device = await WiFiDirectDevice.FromIdAsync(request.DeviceInformation.Id);
                if (device == null)
                    return;
                lock (clients)
                {
                    if (stopping)
                        device.Dispose();
                    else
                        clients.Add(device);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                or UnauthorizedAccessException or OperationCanceledException)
            {
                // The device may disconnect before Windows completes association.
            }
        }

        void OnStatusChanged(WiFiDirectAdvertisementPublisher sender,
            WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
        {
            if (args.Status == WiFiDirectAdvertisementPublisherStatus.Started)
                ready.TrySetResult();
            else if (args.Status is WiFiDirectAdvertisementPublisherStatus.Aborted
                or WiFiDirectAdvertisementPublisherStatus.Stopped)
                ready.TrySetException(new IOException(
                    "Could not create a Wi-Fi hotspot. Check Windows Wi-Fi Direct support and close any active hotspot."));
        }
    }
}
