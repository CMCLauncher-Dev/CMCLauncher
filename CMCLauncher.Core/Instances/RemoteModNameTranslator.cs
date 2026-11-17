using System.Text.Json;

namespace CMCLauncher;

// 模组"列表显示名称"的本地翻译器，思路对齐 HMCL：
// 只翻译列表里展示的名称，不改动模组任何 Java 文件与内容。
// 名称是专有名词，优先查本地映射表，避免把 Sodium 机译成「钠」；
// 未命中的名称保留原文。映射表可扩展，并支持从外部 JSON 补充。
public static class RemoteModNameTranslator
{
    // 曲例：大小写不敏感；键用常见的项目显示名/英文名，值给中文名。
    static readonly Dictionary<string, string> KnownNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sodium"] = "钠（渲染优化）",
            ["Lithium"] = "锂（服务端优化）",
            ["Phosphor"] = "磷光（光照优化）",
            ["Iris Shaders"] = "Iris 光影",
            ["OptiFine"] = "高清修复（OptiFine）",
            ["JEI"] = "JEI 物品管理器",
            ["Just Enough Items"] = "JEI 物品管理器",
            ["REI"] = "REI 物品管理器",
            ["Roughly Enough Items"] = "REI 物品管理器",
            ["NEI"] = "NEI 物品管理器",
            ["Not Enough Items"] = "NEI 物品管理器",
            ["CraftTweaker"] = "CraftTweaker 魔改",
            ["KubeJS"] = "KubeJS 魔改脚本",
            ["Create"] = "机械动力",
            ["Mekanism"] = "通用机械",
            ["Thermal Expansion"] = "热力膨胀",
            ["Immersive Engineering"] = "沉浸工程",
            ["Applied Energistics"] = "应用能源",
            ["AE2"] = "应用能源 2",
            ["Botania"] = "植物魔法",
            ["Thaumcraft"] = "神秘时代",
            ["Blood Magic"] = "血魔法",
            ["Astral Sorcery"] = "星辉魔法",
            ["Tinkers Construct"] = "匠魂",
            ["Mantle"] = "匠魂前置（Mantle）",
            ["Twilight Forest"] = "暮色森林",
            ["The Aether"] = "以太",
            ["Alexs Caves"] = "Alex 的洞穴",
            ["Ice and Fire"] = "冰与火之歌",
            ["Deeper and Darker"] = "黑暗深处",
            ["Better Nether"] = "更好的下界",
            ["Better End"] = "更好的末地",
            ["Biomes O Plenty"] = "生物群系百态",
            ["Terralith"] = "Terralith 群系生成",
            ["Amplified Nether"] = "丰富下界",
            ["Wilder Wild"] = "狂野世界",
            ["TerraBlender"] = "TerraBlender 群系前置",
            ["JourneyMap"] = "旅行地图",
            ["Xaeros Minimap"] = "小地图（Xaero）",
            ["Xaero's Minimap"] = "小地图（Xaero）",
            ["Xaero's World Map"] = "世界地图（Xaero）",
            ["Mini HUD"] = "MiniHUD 辅助",
            ["Litematica"] = "蓝图投影（Litematica）",
            ["WorldEdit"] = "创世神（WorldEdit）",
            ["WorldEditCUI"] = "创世神 CUI",
            ["Fast Leaf Decay"] = "快速树叶凋落",
            ["Chunk Pregenerator"] = "区块预生成",
            ["Cave Dust"] = "洞穴灰尘",
            ["Sound Physics Remastered"] = "声音物理重置",
            ["3D Skin Layers"] = "3D 皮肤层",
            ["Skin Layers 3D"] = "3D 皮肤层",
            ["Continuity"] = "Continuity 连接纹理",
            ["Cull Leaves"] = "裁剪树叶",
            ["Cull Clouds"] = "裁剪云",
            ["Corse CDA"] = "着色器",
            ["Entity Culling"] = "实体剔除",
            ["Rubidium"] = "铷（渲染优化）",
            ["Embeddium"] = "Embeddium 渲染优化",
            ["Oculus"] = "Oculus 光影",
            ["Magnesium"] = "镁（渲染优化）",
            ["FerriteCore"] = "FerriteCore 内存优化",
            ["Canary"] = "金丝雀（服务端优化）",
            ["Krypton"] = "氪（网络优化）",
            ["Starlight"] = "星光（光照引擎）",
            ["Inventory Tweaks"] = "R 键整理",
            ["Mouse Tweaks"] = "鼠标手势",
            ["Controlling"] = "按键冲突控制",
            ["Catalogue"] = "模组目录",
            ["Mod Menu"] = "模组菜单",
            ["Custom Main Menu"] = "自定义主菜单",
            ["AppleSkin"] = "饱食度显示",
            ["Hwyla"] = "标识高亮（一睹为快）",
            ["Waila"] = "标识高亮（一睹为快）",
            ["Jade"] = "Jade 标识高亮",
            ["Better Advancements"] = "更好的进度界面",
            ["ProgressAPI"] = "进度 API",
            ["Toast Control"] = "Toast 控制",
            ["Nvidia World"] = "世界边角",
            ["Boring Default Configs"] = "默认配置",
            ["The One Probe"] = "探测棒（TOP）",
            ["TOP"] = "探测棒（The One Probe）",
            ["Gravestone"] = "墓碑",
            ["Simple Graves"] = "简易墓碑",
            ["Waystones"] = "传送石",
            ["Bedrock Miners"] = "基岩采掘",
            ["Sculk Hunter"] = "潜声猎手",
            ["Neat"] = "血条显示",
            ["Simple Sponge"] = "简易海绵",
            ["Drip Sounds"] = "滴落声音",
            ["Amplified Settings"] = "丰富设置",
            ["Item Model Fix"] = "物品模型修复",
            ["Fabric API"] = "Fabric API",
            ["Forge"] = "Forge",
            ["NeoForge"] = "NeoForge",
            ["Quilt API"] = "Quilt API",
            ["Quilted Fabric API"] = "Quilt 版 Fabric API",
            ["GeckoLib"] = "GeckoLib 动画库",
            ["Curios API"] = "Curios 饰品栏",
            ["Trinkets"] = "Trinkets 饰品栏",
            ["Rei"] = "REI 物品管理器",
            ["Cloth Config"] = "Cloth 配置库",
            ["Architectury"] = "Architectury API",
            ["YOEndi"] = "Endi",
            ["JitterLib"] = "JitterLib 前置",
            ["Iceberg"] = "Iceberg 前置库",
            ["Blueprint"] = "Blueprint 前置库",
        };

    // 外部扩展映射表（用户可自行补充）：加载成功则合并进 KnownNames 的优先子集。
    static readonly Dictionary<string, string> External =
        new(StringComparer.OrdinalIgnoreCase);

    // 解析外部 JSON 映射表（可选）。文件为 {"Sodium":"钠", ...} 扁平对象。
    public static void LoadExternal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        try
        {
            using var reader = new StreamReader(path);
            var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var prop in doc.RootElement.EnumerateObject())
                External[prop.Name] = prop.Value.GetString() ?? prop.Name;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 外部映射表损坏时静默忽略，不影响启动。
        }
    }

    // 返回给定模组名的中文显示名；未命中或等于原文时返回 null（由调用方保留原文）。
    // 大小写、前后空格不敏感；排序上外层映射优先于内置映射，方便用户覆盖内置结果。
    public static string? Translate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string Head(string s)
        {
            // 常见项目名常带版本号（如 "Sodium 0.5.8"）或前缀，这里取首个独立词尝试匹配，
            // 匹配到多词名时再回退整体匹配，避免过长判断。
            var trimmed = s.Trim();
            if (trimmed.Contains(' '))
            {
                var first = trimmed.Split(' ')[0];
                if (
                    External.TryGetValue(first, out var e) || KnownNames.TryGetValue(first, out e)
                )
                    return e;
            }
            return trimmed;
        }
        var head = Head(name);
        if (External.TryGetValue(head, out var translated))
            return translated;
        if (KnownNames.TryGetValue(head, out var known))
            return known;
        string? knownExact = null;
        string? exact = null;
        var hadExternal = External.TryGetValue(name.Trim(), out exact);
        var hadKnown = KnownNames.TryGetValue(name.Trim(), out knownExact);
        if (hadExternal)
            return exact;
        if (hadKnown)
            return knownExact;
        return null;
    }
}