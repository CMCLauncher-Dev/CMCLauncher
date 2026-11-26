using System.Net;
using CMCLauncher;

static class DeviceCodePollingTests
{
    public static async Task Run(string root)
    {
        var store = new Store(root);
        var lifetime = TimeSpan.FromSeconds(5);
        var interval = TimeSpan.FromMilliseconds(5);
        var handler = new PollingHandler(attempt =>
            attempt switch
            {
                1 => throw new HttpRequestException("Temporary connection error"),
                2 => throw new TaskCanceledException("HTTP client timeout"),
                3 => new(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("""{"error":"gateway"}"""),
                },
                4 => new(HttpStatusCode.OK) { Content = new StringContent("<html>") },
                5 => new(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"authorization_pending"}"""),
                },
                _ => new(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"test-token"}"""),
                },
            }
        );
        using var core = new LauncherCore(store, handler);
        var auth = new MicrosoftAuth(core);
        var token = await auth.PollDeviceToken("client", "device", interval, lifetime, default);
        if (token.S("access_token") != "test-token" || handler.Attempts != 6)
            throw new Exception("Device token polling did not recover from temporary failures.");

        var declined = new PollingHandler(_ =>
            new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"authorization_declined","error_description":"declined by user"}"""
                ),
            }
        );
        using var declinedCore = new LauncherCore(store, declined);
        try
        {
            await new MicrosoftAuth(declinedCore).PollDeviceToken(
                "client",
                "device",
                interval,
                lifetime,
                default
            );
            throw new Exception("Expected authorization denial.");
        }
        catch (Exception ex)
            when (ex.Message.EndsWith("declined by user", StringComparison.Ordinal)
                && declined.Attempts == 1
            ) { }

        try
        {
            await auth.PollDeviceToken(
                "client",
                "device",
                TimeSpan.FromSeconds(1),
                interval,
                default
            );
            throw new Exception("Expected expiry before the first poll.");
        }
        catch (TimeoutException) when (handler.Attempts == 6) { }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await auth.PollDeviceToken("client", "device", interval, lifetime, cancelled.Token);
            throw new Exception("Expected user cancellation.");
        }
        catch (OperationCanceledException) when (handler.Attempts == 6) { }

        Console.WriteLine(
            "PASS device-code polling retries temporary failures, preserves denial and respects expiry and cancellation"
        );
    }

    sealed class PollingHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(respond(++Attempts));
    }
}
