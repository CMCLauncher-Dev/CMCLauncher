using System.Net;
using CMCLauncher;

static class BmclTests
{
    sealed class Handler(List<string> urls, Func<bool> fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(
                new HttpResponseMessage(
                    fail() && request.RequestUri.Host == "bmclapi2.bangbang93.com"
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.OK
                )
                {
                    Content = new StringContent("{}"),
                }
            );
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
        var urls = new List<string>();
        var fail = false;
        using var core = new LauncherCore(store, new Handler(urls, () => fail));
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
    }
}
