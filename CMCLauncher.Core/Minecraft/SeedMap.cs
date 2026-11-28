using System.Globalization;
using System.Runtime.InteropServices;

namespace CMCLauncher;

public readonly record struct SeedMapPoint(int X, int Z);

public readonly record struct SeedMapStructure(int X, int Z, int Type);

public enum SeedMapDimension
{
    Overworld,
    Nether = -1,
    End = 1,
}

public sealed class SeedMap
{
    public const int AllStructures = 16;
    public const int Size = 192;
    public const int DisplayDetail = 2;
    public const int WorldLimit = 29_999_984;
    public const int MinScale = 2;
    public const int MaxScale = 32;
    public const int DefaultScale = 16;

    public static IReadOnlyList<string> Versions { get; } =
        Array.AsReadOnly(
            new[]
            {
                "1.13.2",
                "1.14.4",
                "1.15.2",
                "1.16.1",
                "1.16.5",
                "1.17.1",
                "1.18.2",
                "1.19.2",
                "1.19.4",
                "1.20.6",
                "1.21.1",
                "1.21.3",
                "1.21.4",
            }
        );

    public static IReadOnlyList<string> StructureNames { get; } =
        Array.AsReadOnly(
            new[]
            {
                "Hide structures",
                "Stronghold",
                "Village",
                "Pillager outpost",
                "Desert pyramid",
                "Jungle temple",
                "Swamp hut",
                "Igloo",
                "Ocean monument",
                "Woodland mansion",
                "Ancient city",
                "Shipwreck",
                "Ocean ruins",
                "Ruined portal",
                "Trail ruins",
                "Trial chambers",
                "All structures",
                "Nether fortress",
                "Bastion remnant",
                "Ruined portal",
                "End city",
                "End gateway",
            }
        );

    readonly byte[] biomes;
    readonly int version;
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int RasterWidth { get; }
    public int RasterHeight { get; }
    public int BlocksPerSample { get; }
    readonly int detail;
    public IReadOnlyList<SeedMapStructure> Structures { get; }
    public SeedMapPoint Center { get; }
    public SeedMapPoint Origin { get; }
    public int Scale { get; }
    public SeedMapDimension Dimension { get; }

    public static IEnumerable<int> StructureOptions(int version, SeedMapDimension dimension) =>
        Enumerable
            .Range(0, StructureNames.Count)
            .Where(index => SupportsStructure(version, index, dimension));

    public static bool SupportsStructure(
        int version,
        int structureIndex,
        SeedMapDimension dimension = SeedMapDimension.Overworld,
        bool highDetail = false
    )
    {
        if (structureIndex is 0 or AllStructures)
            return true;
        return dimension switch
        {
            SeedMapDimension.Overworld => structureIndex switch
            {
                3 => version >= 1,
                10 => version >= 7,
                13 => version >= 3,
                14 => version >= 9,
                15 => version >= 10,
                >= 1 and <= 15 => true,
                _ => false,
            },
            SeedMapDimension.Nether => structureIndex == 17
                || (version >= 3 && structureIndex is 18 or 19),
            SeedMapDimension.End => structureIndex is 20 or 21,
            _ => false,
        };
    }

    public static long ParseSeed(string seed)
    {
        var text = seed.Trim();
        if (text.Length == 0)
            throw new ArgumentException("A world seed is required.", nameof(seed));
        if (
            long.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
            return value;
        var hash = 0;
        foreach (var character in text)
            hash = unchecked(hash * 31 + character);
        return hash;
    }

    public SeedMap(
        long seed,
        int version,
        int structureIndex,
        SeedMapPoint center,
        int scale,
        int width = Size,
        int height = Size,
        SeedMapDimension dimension = SeedMapDimension.Overworld,
        bool highDetail = false
    )
    {
        if (version < 0 || version >= Versions.Count)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (
            structureIndex < 0
            || structureIndex >= StructureNames.Count
            || !SupportsStructure(version, structureIndex, dimension)
        )
            throw new ArgumentOutOfRangeException(nameof(structureIndex));
        if (scale < MinScale || scale > MaxScale)
            throw new ArgumentOutOfRangeException(nameof(scale));

        if (!Enum.IsDefined(typeof(SeedMapDimension), dimension))
            throw new ArgumentOutOfRangeException(nameof(dimension));
        this.version = version;
        Dimension = dimension;
        Scale = scale;
        if (width is < 1 or > 640 || height is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid seed map dimensions.");
        Width = width;
        Height = height;
        detail = highDetail && scale % DisplayDetail == 0 ? DisplayDetail : 1;
        RasterWidth = width * detail;
        RasterHeight = height * detail;
        BlocksPerSample = scale / detail;
        biomes = new byte[RasterWidth * RasterHeight];
        Pixels = new byte[RasterWidth * RasterHeight * 4];
        var radiusX = width * scale / 2;
        var radiusZ = height * scale / 2;
        Center = new(
            Math.Clamp(center.X, -WorldLimit + radiusX, WorldLimit - (width * scale - radiusX)),
            Math.Clamp(center.Z, -WorldLimit + radiusZ, WorldLimit - (height * scale - radiusZ))
        );
        Origin = new(Center.X - radiusX, Center.Z - radiusZ);
        var positions = new SeedMapStructure[4096];
        var nativeType = structureIndex switch
        {
            AllStructures => -3,
            0 => -1,
            1 => -2,
            _ => structureIndex - 2,
        };
        var count = Render(
            version,
            unchecked((ulong)seed),
            Origin.X,
            Origin.Z,
            BlocksPerSample,
            RasterWidth,
            RasterHeight,
            (int)dimension,
            nativeType,
            Pixels,
            biomes,
            positions,
            positions.Length
        );
        if (count < 0)
            throw new InvalidOperationException("Biome generation failed.");

        Structures = Array.AsReadOnly(
            positions
                .Take(count)
                .OrderBy(position => DistanceSquared(new(position.X, position.Z), Center))
                .ToArray()
        );
        foreach (var position in Structures)
            DrawMarker(position);
    }

    public SeedMapPoint PanBy(double horizontal, double vertical) =>
        new(
            Center.X - (int)Math.Round(horizontal * Width * Scale),
            Center.Z - (int)Math.Round(vertical * Height * Scale)
        );

    public SeedMapPoint ZoomAt(double horizontal, double vertical, int newScale) =>
        new(
            Center.X + (int)Math.Round((horizontal - .5) * Width * (Scale - newScale)),
            Center.Z + (int)Math.Round((vertical - .5) * Height * (Scale - newScale))
        );

    public SeedMapPoint PositionAt(double x, double y) =>
        new(Origin.X + MapOffset(x, Width), Origin.Z + MapOffset(y, Height));

    public static string TeleportCommand(SeedMapStructure structure, SeedMapDimension dimension)
    {
        var target = dimension switch
        {
            SeedMapDimension.Nether => "the_nether",
            SeedMapDimension.End => "the_end",
            _ => "overworld",
        };
        return FormattableString.Invariant(
            $"/execute in minecraft:{target} run tp @s {structure.X} ~ {structure.Z}"
        );
    }

    int MapOffset(double fraction, int samples) =>
        Math.Clamp((int)(fraction * samples * Scale), 0, samples * Scale - 1);

    public string BiomeAt(double x, double y)
    {
        var column = Math.Clamp((int)(x * RasterWidth), 0, RasterWidth - 1);
        var row = Math.Clamp((int)(y * RasterHeight), 0, RasterHeight - 1);
        var name = Marshal.PtrToStringAnsi(BiomeName(version, biomes[row * RasterWidth + column]));
        return (name ?? "Unknown biome").Replace('_', ' ');
    }

    public SeedMapStructure? StructureAt(double x, double y)
    {
        var column = x * Width;
        var row = y * Height;
        SeedMapStructure? best = null;
        var minDistance = 36d;
        foreach (var position in Structures)
        {
            var dx = (position.X - Origin.X) / (double)Scale - column;
            var dy = (position.Z - Origin.Z) / (double)Scale - row;
            var distance = dx * dx + dy * dy;
            if (distance <= minDistance)
            {
                best = position;
                minDistance = distance;
            }
        }
        return best;
    }

    // Five-by-five glyphs drawn over a dark badge to stay readable on bright biomes.
    // Keys are the structure IDs returned by the native engine.
    static readonly (string Glyph, int Color)[] markerStyles =
    [
        ("", 0), // hidden
        ("1111101010011100101011111", 0xeab5ff),
        ("0010001110111111010111111", 0xffc66e),
        ("0111001010111110010001110", 0xff998f),
        ("0010001110011101111111011", 0xffd96a),
        ("0010001110001000111011111", 0x93e889),
        ("0001100111011101110001100", 0x9ceea3),
        ("0000001110111111101111111", 0xd9fbff),
        ("0010010101111110111000100", 0x86ffff),
        ("1010111111101011111111011", 0xeda572),
        ("0111010001101011000101110", 0x97cdfd),
        ("0010000110001111111101110", 0x7ce2ff),
        ("0000010100111011011111111", 0x9bc9fc),
        ("0111011011100011101101110", 0xef9aff),
        ("1100001100001100110011000", 0xf3a95d),
        ("1000101010001000101010001", 0x8bf6cf),
        ("", 0), // all
        ("1010111111011100111011111", 0xff8079),
        ("1110010111111011010111111", 0xf5c171),
        ("0111011011100011101101110", 0xf295ff),
        ("0111000100011100010011111", 0xf7ec9a),
        ("0010001010101010101000100", 0xd4abff),
    ];

    void DrawMarker(SeedMapStructure position)
    {
        var x = (position.X - Origin.X) / BlocksPerSample;
        var z = (position.Z - Origin.Z) / BlocksPerSample;
        var radius = 4 * detail;
        var (glyph, color) = markerStyles[position.Type];
        for (
            var row = Math.Max(0, z - radius);
            row <= Math.Min(RasterHeight - 1, z + radius);
            row++
        )
        for (
            var column = Math.Max(0, x - radius);
            column <= Math.Min(RasterWidth - 1, x + radius);
            column++
        )
        {
            var dx = (column - x) / (double)detail;
            var dz = (row - z) / (double)detail;
            if (Math.Abs(dx) + Math.Abs(dz) > 6)
                continue;
            var pixel = (row * RasterWidth + column) * 4;
            var glyphX = (int)Math.Floor(dx + 2.5);
            var glyphZ = (int)Math.Floor(dz + 2.5);
            var icon =
                glyphX is >= 0 and < 5
                && glyphZ is >= 0 and < 5
                && glyph[glyphZ * 5 + glyphX] == '1';
            var rgb =
                icon ? color
                : dx * dx + dz * dz >= 16 ? 0xeeeeee
                : 0x20232c;
            Pixels[pixel] = (byte)rgb;
            Pixels[pixel + 1] = (byte)(rgb >> 8);
            Pixels[pixel + 2] = (byte)(rgb >> 16);
        }
    }

    static long DistanceSquared(SeedMapPoint a, SeedMapPoint b)
    {
        var x = (long)a.X - b.X;
        var z = (long)a.Z - b.Z;
        return x * x + z * z;
    }

    [DllImport(
        "cmc_seed_map",
        EntryPoint = "cmc_seed_render",
        CallingConvention = CallingConvention.Cdecl
    )]
    static extern int Render(
        int version,
        ulong seed,
        int x,
        int z,
        int scale,
        int width,
        int height,
        int dimension,
        int structureType,
        [Out] byte[] pixels,
        [Out] byte[] biomes,
        [Out] SeedMapStructure[] positions,
        int capacity
    );

    [DllImport(
        "cmc_seed_map",
        EntryPoint = "cmc_seed_biome_name",
        CallingConvention = CallingConvention.Cdecl
    )]
    static extern IntPtr BiomeName(int version, int biome);
}
