namespace CMCLauncher;

// 社区项目的单条依赖：名称、关系（依赖/冲突等）与版本区间。
public sealed record CommunityDependency(string Name, string Relation, string Version);

// 社区内容统一门面：按项目 ID 前缀路由到 Modrinth 或 CurseForge，
// 搜索时支持双源合并（交错分页）与单源降级。
public sealed class CommunityService(
    ModrinthService modrinth,
    CurseForgeCommunityService curseforge
)
{
    // 获取项目详情（按 ID 路由到对应源）。
    public Task<CommunityProject> ProjectDetails(string id, CancellationToken ct) =>
        CurseForgeCommunityService.IsProject(id)
            ? curseforge.ProjectDetails(id, ct)
            : modrinth.ProjectDetails(id, ct);

    // 获取项目版本列表（按 ID 路由到对应源）。
    public Task<List<CommunityVersion>> ProjectVersions(string id, CancellationToken ct) =>
        CurseForgeCommunityService.IsProject(id)
            ? curseforge.ProjectVersions(id, ct)
            : modrinth.ProjectVersions(id, ct);

    // 获取指定版本的依赖列表（按 ID 路由到对应源）。
    public Task<List<CommunityDependency>> Dependencies(
        string project,
        string version,
        CancellationToken ct
    ) =>
        CurseForgeCommunityService.IsProject(project)
            ? curseforge.Dependencies(project, version, ct)
            : modrinth.Dependencies(project, version, ct);

    // 双源交错分页计数：前 2*min(a,b) 个结果两源交替，
    // 剩余部分归入较长列表。用于保证翻页跳转时两源偏移量稳定。
    internal static (int A, int B) Counts(int n, int a, int b)
    {
        var paired = (int)Math.Min(n, 2L * Math.Min(a, b));
        var extra = n - paired;
        return ((paired + 1) / 2 + (a > b ? extra : 0), paired / 2 + (b >= a ? extra : 0));
    }

    // 社区搜索（支持双源合并）：
    // 指定单一源时直接查询；"all" 时先探测两源可用性，
    // 单源不可用则降级并附警告，双源可用则按交错顺序合并结果。
    public async Task<CommunitySearchPage> SearchPage(
        string query,
        string type,
        CancellationToken ct,
        int offset,
        int limit,
        CommunitySearchFilters? filters = null
    )
    {
        filters ??= new();
        if (type == "world")
            filters = filters with { Source = "curseforge" };
        if (offset < 0 || limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(offset));
        // 按源选择查询：cf 为 true 走 CurseForge，否则走 Modrinth。
        Task<CommunitySearchPage> Fetch(bool cf, int start, int count) =>
            cf
                ? curseforge.SearchPage(query, type, ct, start, count, filters)
                : modrinth.SearchPage(query, type, ct, start, count, filters);
        if (filters.Source == "modrinth")
            return await Fetch(false, offset, limit);
        if (filters.Source == "curseforge")
            return await Fetch(true, offset, limit);
        if (filters.Source != "all")
            throw new ArgumentException("Unknown community source.");
        // 探测源可用性：取 1 条结果，失败（非取消）返回 null。
        async Task<CommunitySearchPage?> Probe(bool cf)
        {
            try
            {
                return await Fetch(cf, 0, 1);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
        var probes = await Task.WhenAll(Probe(false), Probe(true));
        if (probes.All(p => p == null))
            throw new InvalidOperationException("Neither community source is available.");
        if (probes.Any(p => p == null))
        {
            var cf = probes[0] == null;
            var result = await Fetch(cf, offset, limit);
            return result with
            {
                Warning = cf
                    ? "Modrinth is unavailable. Showing CurseForge results only."
                    : "CurseForge is unavailable. Showing Modrinth results only.",
            };
        }
        // 双源均可用：计算本页在两源中各自需要的偏移区间，
        // 并行取回后按交错顺序合并。
        var a = probes[0]!.TotalHits;
        var b = probes[1]!.TotalHits;
        var total = checked(a + b);
        var before = Counts(Math.Min(offset, total), a, b);
        var after = Counts((int)Math.Min((long)offset + limit, total), a, b);
        // 取回某源 [start, end) 区间的结果。
        async Task<IReadOnlyList<SearchResult>> Range(bool cf, int start, int end) =>
            end == start ? [] : (await Fetch(cf, start, end - start)).Hits;
        var pages = await Task.WhenAll(
            Range(false, before.A, after.A),
            Range(true, before.B, after.B)
        );
        var hits = new List<SearchResult>();
        int ai = 0,
            bi = 0;
        for (var n = offset; n < Math.Min((long)offset + limit, total); n++)
        {
            var current = Counts(n, a, b);
            var next = Counts(n + 1, a, b);
            if (next.A > current.A)
            {
                if (ai < pages[0].Count)
                    hits.Add(pages[0][ai++]);
            }
            else if (bi < pages[1].Count)
                hits.Add(pages[1][bi++]);
        }
        return new(hits, offset, limit, total);
    }
}
