using System.Net;
using CMCLauncher;

static class BmclTests
{
    sealed class Handler(
        List<string> urls,
        Func<bool> fail,
        Func<bool> officialFailure,
        Func<bool> slow
    ) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            urls.Add(request.RequestUri!.AbsoluteUri);
            if (slow() && request.RequestUri.Host == "piston-meta.mojang.com")
                await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(
                fail() && request.RequestUri.Host == "bmclapi2.bangbang93.com"
                || officialFailure() && request.RequestUri.Host == "piston-meta.mojang.com"
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.OK
            )
            {
                Content = new StringContent("{}"),
            };
        }
    }

    public static async Task Run(string root)
    {
        void Check(bool value)
        {
            if (!value)
                throw new Exception("BMCLAPI routing regression");
        }
        Check(
            LauncherCore
                .BmclUrl(new("https://resources.download.minecraft.net/ab/hash"))
                .AbsoluteUri == "https://bmclapi2.bangbang93.com/assets/ab/hash"
        );
        Check(
            LauncherCore.BmclUrl(new("https://libraries.minecraft.net/a.jar")).AbsolutePath
                == "/maven/a.jar"
        );
        foreach (
            var url in new[]
            {
                "https://api.minecraftservices.com/minecraft/profile",
                "https://api.curseforge.com/v1/mods",
                "https://api.modrinth.com/v2/search",
                "https://libraries.minecraft.net.evil.example/a.jar",
            }
        )
            Check(LauncherCore.BmclUrl(new(url)).AbsoluteUri == url);
        var store = new Store(root);
        Check(store.Config.Settings.UseBmclApi == MicrosoftAuth.IsMainlandMachine);
        var explicitOff = System.Text.Json.JsonSerializer.Deserialize<Settings>(
            "{\"useBmclApi\":false}",
            Store.JsonOptions
        )!;
        Check(!explicitOff.UseBmclApi);
        var missing = System.Text.Json.JsonSerializer.Deserialize<Settings>(
            "{}",
            Store.JsonOptions
        )!;
        Check(missing.UseBmclApi == MicrosoftAuth.IsMainlandMachine);
        store.Config.Settings.UseBmclApi = false;
        var urls = new List<string>();
        var fail = false;
        var failOfficial = false;
        var slow = false;
        using var core = new LauncherCore(
            store,
            new Handler(urls, () => fail, () => failOfficial, () => slow)
        );
        const string official = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        await core.Json(official, CancellationToken.None);
        Check(urls.SequenceEqual([official]));
        urls.Clear();
        store.Config.Settings.UseBmclApi = true;
        await core.Json(official, CancellationToken.None);
        Check(urls.Count == 1 && urls[0].Contains("bmclapi2.bangbang93.com"));
        urls.Clear();
        fail = true;
        await core.Json(official, CancellationToken.None);
        Check(urls.Count == 2 && urls[1] == official);
        await store.Save();
        Check(new Store(root).Config.Settings.UseBmclApi);
        store.Config.Settings.UseBmclApi = false;
        await store.Save();
        Check(!new Store(root).Config.Settings.UseBmclApi);
        fail = false;
        store.Config.Settings.GameDownloadSource = "official";
        failOfficial = true;
        urls.Clear();
        await core.Json(official, CancellationToken.None);
        Check(
            urls.Count == 2 && urls[0] == official && urls[1].Contains("bmclapi2.bangbang93.com")
        );
        failOfficial = false;
        urls.Clear();
        store.Config.Settings.GameDownloadSource = "auto";
        await core.Json(official, CancellationToken.None);
        Check(urls.SequenceEqual([official]));
        slow = true;
        urls.Clear();
        await core.Json(official, CancellationToken.None);
        Check(
            urls.Count == 2 && urls[0] == official && urls[1].Contains("bmclapi2.bangbang93.com")
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        urls.Clear();
        try
        {
            await core.Json(official, canceled.Token);
            throw new Exception("Canceled source request continued");
        }
        catch (OperationCanceledException)
        {
            Check(!urls.Any(u => u.Contains("bmclapi2.bangbang93.com")));
        }
        await store.Save();
        Check(new Store(root).Config.Settings.EffectiveGameDownloadSource == "auto");
    }
}
