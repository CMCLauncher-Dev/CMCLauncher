using System.Buffers.Binary;
using System.Text;

namespace CMCLauncher;

// 已保存的服务器条目：显示名称与地址（host:port）。
public sealed record SavedServer(string Name, string Address);

// 读取 Minecraft 的 servers.dat（已保存服务器列表）。
public static class SavedServers
{
    // servers.dat 为未压缩的 Java NBT 格式。
    // 仅解析 name / ip 字段，未知字段一律跳过，绝不回写文件。
    public static IReadOnlyList<SavedServer> Read(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, "servers.dat");
        if (!File.Exists(path)) return [];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > 16 * 1024 * 1024) throw new IOException("Server list is too large.");
        using var reader = new BinaryReader(file, Encoding.UTF8);
        // 读取 NBT 长度前缀（4 字节大端有符号整数），超出上限则视为非法。
        int Count(int maximum)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException();
            var value = BinaryPrimitives.ReadInt32BigEndian(bytes);
            if (value < 0 || value > maximum) throw new IOException("Invalid NBT length.");
            return value;
        }
        // 读取 NBT 字符串（2 字节大端长度 + UTF-8 字节）。
        string Text()
        {
            var bytes = reader.ReadBytes(2);
            if (bytes.Length != 2) throw new EndOfStreamException();
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            var value = reader.ReadBytes(length);
            if (value.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(value);
        }
        var servers = new List<SavedServer>();
        // 跳过指定字节数（用于不关心的 NBT 字段）。
        void Skip(long size)
        {
            if (size > file.Length - file.Position) throw new EndOfStreamException();
            file.Position += size;
        }
        // 递归解析 NBT 标签负载：
        // type 为标签类型（1~12），depth 防止无限嵌套，
        // server 表示当前处于服务器条目内（提取 name/ip），
        // root 表示处于根复合标签内（定位 "servers" 列表）。
        void Payload(int type, int depth, bool server = false, bool root = false)
        {
            if (depth > 32) throw new IOException("NBT nesting is too deep.");
            switch (type)
            {
                case 1: Skip(1); break;
                case 2: Skip(2); break;
                case 3: case 5: Skip(4); break;
                case 4: case 6: Skip(8); break;
                case 7: Skip(Count(16 * 1024 * 1024)); break;
                case 8: Text(); break;
                case 9:
                    var element = reader.ReadByte();
                    var count = Count(100000);
                    for (var i = 0; i < count; i++) Payload(element, depth + 1);
                    break;
                case 10:
                    string name = "", address = "";
                    for (int tag; (tag = reader.ReadByte()) != 0;)
                    {
                        var key = Text();
                        if (root && key == "servers" && tag == 9)
                        {
                            var itemType = reader.ReadByte();
                            var length = Count(10000);
                            if (itemType != 10) throw new IOException("Invalid server list.");
                            for (var i = 0; i < length; i++) Payload(10, depth + 1, true);
                        }
                        else if (server && tag == 8 && key is "name" or "ip")
                        {
                            var value = Text();
                            if (key == "name") name = value; else address = value;
                        }
                        else Payload(tag, depth + 1);
                    }
                    if (server && !string.IsNullOrWhiteSpace(address)) servers.Add(new(name, address));
                    break;
                case 11: Skip((long)Count(4000000) * 4); break;
                case 12: Skip((long)Count(2000000) * 8); break;
                default: throw new IOException("Unsupported NBT tag.");
            }
        }
        // 根标签必须为复合类型（10），随后读取根名称并解析。
        if (reader.ReadByte() != 10) throw new IOException("Invalid server list root.");
        Text();
        Payload(10, 0, root: true);
        return servers;
    }
}
