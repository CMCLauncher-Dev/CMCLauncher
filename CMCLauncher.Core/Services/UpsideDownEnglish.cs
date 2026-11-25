using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CMCLauncher;

internal static class UpsideDownEnglish
{
    const string Upright = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!?.,;()[]<>_";
    const string Rotated = "ɐqɔpǝɟƃɥᴉɾʞlɯuodbɹsʇnʌʍxʎz∀𐐒ƆᗡƎℲ⅁HIſꓘ˥WNOԀΌᴚS⊥∩ΛMX⅄Z0ƖᄅƐㄣϛ9ㄥ86¡¿˙'؛)(][><‾";
    static readonly Rune[] RotatedGlyphs = Rotated.EnumerateRunes().ToArray();

    // Keep formatting tokens, links, and line breaks usable in the translated text.
    static readonly Regex ProtectedTokens = new(
        @"(\{\{|\}\}|\{\d+(?:,\s*-?\d+)?(?::[^{}]*)?\}|https?://[^\s<>()]+|%(?:\d+\$)?[sdf%]|\r\n|[\r\n])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    public static string Transform(string text)
    {
        var parts = ProtectedTokens.Split(text);
        for (var i = 0; i < parts.Length; i += 2)
        {
            var part = parts[i];
            var start = part.Length - part.TrimStart().Length;
            if (start == part.Length)
                continue;
            var end = part.TrimEnd().Length;
            var elements = StringInfo.ParseCombiningCharacters(part[start..end]);
            var result = new StringBuilder(part.Length).Append(part.AsSpan(0, start));
            for (var j = elements.Length - 1; j >= 0; j--)
            {
                var offset = start + elements[j];
                var next = j + 1 < elements.Length ? start + elements[j + 1] : end;
                var glyph = Upright.IndexOf(part[offset]);
                if (glyph >= 0)
                {
                    result.Append(RotatedGlyphs[glyph].ToString());
                    offset++;
                }
                result.Append(part.AsSpan(offset, next - offset));
            }
            parts[i] = result.Append(part.AsSpan(end)).ToString();
        }
        return string.Concat(parts);
    }
}
