using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyJsonThemes(Action<bool, string> check)
    {
        var savedPage = page;
        var savedTheme = store.Config.Settings.Theme;
        var savedPreset = store.Config.Settings.ColorPreset;
        var savedFont = store.Config.Settings.UiFontSize;
        var directory = Path.Combine(Program.UserDirectory, "themes");
        // 自带主题在 Default/；验证时把覆盖写到外层同名文件，模拟用户修改。
        var craft = Path.Combine(directory, "craft.json");
        var bundledCraft = Path.Combine(BundledResources.DefaultDirectory(directory), "craft.json");
        var external = Path.Combine(directory, "verification-theme.json");
        var native = Path.Combine(directory, "native.json");
        var original = await File.ReadAllBytesAsync(bundledCraft);
        var originalCraft = File.Exists(craft) ? await File.ReadAllBytesAsync(craft) : null;
        var originalExternal = File.Exists(external)
            ? await File.ReadAllBytesAsync(external)
            : null;
        var originalNative = File.Exists(native) ? await File.ReadAllBytesAsync(native) : null;
        try
        {
            check(
                themeCatalog.Presets[0].Id == "craft"
                    && themeCatalog.Presets.Where(p => p.Id is "craft" or "lakecyan" or "sakura")
                        .All(p => p.IsBuiltIn),
                "Fresh green is listed first; Default/ themes such as Lake Cyan and Sakura are built in"
            );
            foreach (
                var (id, light, dark) in new[]
                {
                    ("craft", "#F6F8F7", "#1B1C1B"),
                    ("lakecyan", "#F1FAFC", "#171D1F"),
                    ("sakura", "#FFF5F8", "#1F1A1D"),
                }
            )
            foreach (var mode in new[] { "light", "dark" })
            {
                store.Config.Settings.ColorPreset = id;
                store.Config.Settings.Theme = mode;
                ApplyTheme();
                check(
                    WindowBackground.Color == RawColor(mode == "light" ? light : dark),
                    "JSON preserves the window palette for " + id + " " + mode
                );
            }
            foreach (var mode in new[] { "light", "dark" })
            {
                store.Config.Settings.Theme = mode;
                store.Config.Settings.ColorPreset = "native";
                page = "personalization";
                await RenderAsync();
                check(
                    themeCards.Count > 0
                        && themeCards.All(card =>
                            card.Background is SolidColorBrush brush
                            && brush.Color == SoftSurfaceColor()
                        ),
                    "new native cards use their current surface color in " + mode
                );
                var picker = Descendants(PageBody)
                    .OfType<ComboBox>()
                    .Single(c => c.Header?.ToString() == Ui("Preset"));
                foreach (var id in new[] { "craft", "lakecyan", "sakura", "native" })
                {
                    var existingCards = themeCards.ToArray();
                    picker.SelectedIndex = ColorPresetIndex(id);
                    await Task.Delay(100);
                    check(
                        store.Config.Settings.Theme == mode
                            && existingCards.SequenceEqual(themeCards)
                            && themeCards.All(card =>
                                card.Background is SolidColorBrush brush
                                && brush.Color == SoftSurfaceColor()
                            ),
                        "preset selection immediately recolors existing cards without changing appearance: "
                            + id
                            + " "
                            + mode
                    );
                    if (id == "lakecyan" && mode == "light")
                        check(
                            themeCards.All(card =>
                                card.Background is SolidColorBrush brush
                                && brush.Color == RawColor("#E0F0F3")
                            ),
                            "Lake Cyan light cards immediately use the blended theme color rather than white"
                        );
                }
                store.Config.Settings.ColorPreset = "lakecyan";
                await RenderAsync();
                check(
                    themeCards.All(card =>
                        card.Background is SolidColorBrush brush
                        && brush.Color == SoftSurfaceColor()
                    ),
                    "reentering personalization creates correctly themed cards without toggling appearance: "
                        + mode
                );
                var compact = CompactCard(new TextBlock { Text = "Card palette fixture" });
                check(
                    compact.Background is SolidColorBrush compactBrush
                        && compactBrush.Color == SoftSurfaceColor(),
                    "compact cards use the same initial theme surface in " + mode
                );
                themeCards.Remove(compact);
            }
            var json = JsonNode.Parse(original)!;
            json["colors"]!["LightBg"] = "#123456";
            json["dark"]!["DarkBg"] = "#234567";
            await File.WriteAllTextAsync(craft, json.ToJsonString());
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "light";
            page = "personalization";
            await RenderAsync();
            check(
                WindowBackground.Color == RawColor("#123456"),
                "reopening personalization reloads external edits without recompilation"
            );
            store.Config.Settings.Theme = "dark";
            ApplyTheme();
            check(
                WindowBackground.Color == RawColor("#234567"),
                "external JSON dark overrides update the actual window"
            );
            await File.WriteAllTextAsync(
                external,
                """{"version":1,"id":"verification-theme","displayName":"Verification theme","colors":{"LightBg":"#345678"}}"""
            );
            store.Config.Settings.Theme = "light";
            await RenderAsync();
            var selector = Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Header?.ToString() == Ui("Preset"));
            check(
                selector.Items.Contains("Verification theme"),
                "additional JSON themes appear in the preset selector"
            );
            store.Config.Settings.UiFontSize = 18;
            selector.SelectedIndex = ColorPresetIndex("verification-theme");
            await Task.Delay(200);
            check(
                store.Config.Settings.ColorPreset == "verification-theme"
                    && WindowBackground.Color == RawColor("#345678")
                    && store.Config.Settings.UiFontSize == 18,
                "external theme selection updates and persists colors without resetting typography"
            );
            check(
                ImportAppearancePreset(AppearanceJson(), "JSON round trip").ColorPreset
                    == "verification-theme",
                "appearance import accepts dynamically discovered theme IDs"
            );
            File.Delete(external);
            await RenderAsync();
            selector = Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Header?.ToString() == Ui("Preset"));
            check(
                selector.SelectedIndex == ColorPresetIndex("craft")
                    && WindowBackground.Color == RawColor("#123456"),
                "removing an external theme shows Fresh green in both the selector and window"
            );
            store.Config.Settings.ColorPreset = "native";
            ApplyTheme();
            var nativeBackground = WindowBackground.Color;
            var nativeAccent = PaletteColor(ThemeTokens.LightAccent);
            await File.WriteAllTextAsync(
                native,
                """{"id":"native","displayName":"Override","colors":{"LightBg":"#FF0000","LightAccent":"#FF0000"}}"""
            );
            themeCatalog.Reload();
            ApplyTheme();
            check(
                WindowBackground.Color == nativeBackground
                    && PaletteColor(ThemeTokens.LightAccent) == nativeAccent
                    && !themeCatalog.Presets.Any(p => p.Id == "native"),
                "JSON cannot replace native WinUI theme colors or its preset"
            );
            await File.WriteAllTextAsync(craft, "{invalid");
            themeCatalog.Reload();
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "dark";
            ApplyTheme();
            check(
                WindowBackground.Color == RawColor("#1B1C1B"),
                "malformed external Fresh green JSON falls back to the embedded palette in the window"
            );
        }
        finally
        {
            if (originalCraft == null)
                File.Delete(craft);
            else
                await File.WriteAllBytesAsync(craft, originalCraft);
            if (originalExternal == null)
                File.Delete(external);
            else
                await File.WriteAllBytesAsync(external, originalExternal);
            if (originalNative == null)
                File.Delete(native);
            else
                await File.WriteAllBytesAsync(native, originalNative);
            themeCatalog.Reload();
            store.Config.Settings.Theme = savedTheme;
            store.Config.Settings.ColorPreset = savedPreset;
            store.Config.Settings.UiFontSize = savedFont;
            page = savedPage;
            ApplyTheme();
            await store.Save();
            await RenderAsync();
        }
    }
}
