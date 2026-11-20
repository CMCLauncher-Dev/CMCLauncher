using System.Text;

namespace CMCLauncher;

// 日志尾部读取：只读取日志文件末尾的有限内容，避免把超大日志整体载入内存。
public static class LogTail
{
    // 读取文件末尾最多 maxCharacters 个字符（按 4 字节/字符估算磁盘读取量）；
    // 游戏日志可能远超报告限制，因此限制磁盘读取后再解码。
    public static string Read(string path, int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);
        if (maxCharacters == 0)
            return "";
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        stream.Seek(Math.Max(0, stream.Length - (maxCharacters * 4L + 4)), SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = reader.ReadToEnd();
        if (text.Length <= maxCharacters)
            return text;
        var start = text.Length - maxCharacters;
        if (char.IsLowSurrogate(text[start]))
            start++;
        return text[start..];
    }
}
