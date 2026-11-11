using CMCLauncher;

static class MinecraftPfpTests
{
    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("MinecraftPFP pixel regression");
    }

    public static void Run()
    {
        var skin = new byte[64 * 64 * 4];
        for (var y = 8; y < 16; y++)
        for (var x = 8; x < 16; x++)
        {
            var i = (y * 64 + x) * 4;
            skin[i] = 20;
            skin[i + 1] = 40;
            skin[i + 2] = 200;
            var hat = i + 32 * 4;
            skin[hat] = 220;
            skin[hat + 1] = 80;
            skin[hat + 3] = 128;
        }
        var flat = MinecraftPfp.Render(skin, 64, 64, 128, false, 0);
        Check(flat.Length == 128 * 128 * 4 && flat[0] == 20 && flat[2] == 200 && flat[3] == 255);
        var layered = MinecraftPfp.Render(skin, 64, 64, 128, true, 0);
        Check(layered[0] == 120 && layered[2] == 100 && layered[3] == 255);
        var transparent = MinecraftPfp.Render(skin, 64, 64, 256, true, 12);
        Check(transparent[3] == 0 && transparent[(128 * 256 + 128) * 4 + 3] == 255);
        var solid = MinecraftPfp.Render(skin, 64, 64, 512, false, 20, [1, 2, 3]);
        Check(solid[0] == 1 && solid[1] == 2 && solid[2] == 3 && solid[3] == 255);
        Check(
            MinecraftPfp.Render(skin[..(64 * 32 * 4)], 64, 32, 128, true, 0).SequenceEqual(layered)
        );
        var hd = new byte[128 * 128 * 4];
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 128; x++)
            Array.Copy(skin, ((y / 2) * 64 + x / 2) * 4, hd, (y * 128 + x) * 4, 4);
        Check(MinecraftPfp.Render(hd, 128, 128, 128, true, 0).SequenceEqual(layered));
        void Paint(int sx, int sy, int w, int h, byte color)
        {
            for (var y = sy; y < sy + h; y++)
            for (var x = sx; x < sx + w; x++)
                skin[(y * 64 + x) * 4] = color;
        }
        Paint(20, 20, 8, 12, 11);
        Paint(44, 20, 4, 12, 44);
        Paint(36, 52, 4, 12, 66);
        var bust = MinecraftPfp.Render(skin, 64, 64, 128, false, 12, portrait: true, shadow: false);
        Check(bust[(120 * 128 + 64) * 4] == 11);
        Check(bust[(110 * 128 + 35) * 4] == 44 && bust[(110 * 128 + 90) * 4] == 66);
        Check(bust[3] == 0);
        var shaded = MinecraftPfp.Render(
            skin,
            64,
            64,
            128,
            false,
            12,
            portrait: true,
            shadow: true
        );
        Check(Enumerable.Range(0, 128 * 128).Any(i => shaded[i * 4 + 3] is > 0 and < 255));
        var slim = MinecraftPfp.Render(
            skin,
            64,
            64,
            128,
            false,
            12,
            portrait: true,
            shadow: false,
            slim: true
        );
        Check(
            Enumerable.Range(0, 128).Count(x => slim[(110 * 128 + x) * 4 + 3] != 0)
                < Enumerable.Range(0, 128).Count(x => bust[(110 * 128 + x) * 4 + 3] != 0)
        );
        var oldBust = MinecraftPfp.Render(
            skin[..(64 * 32 * 4)],
            64,
            32,
            128,
            false,
            12,
            portrait: true,
            shadow: false
        );
        Check(oldBust[(110 * 128 + 90) * 4] == 44);
        var vertical = MinecraftPfp.Render(
            skin,
            64,
            64,
            128,
            false,
            12,
            [1, 2, 3],
            portrait: true,
            shadow: false,
            gradientEnd: [201, 202, 203],
            gradientAngle: 90
        );
        Check(vertical[0] == 1 && vertical[(127 * 128 + 127) * 4] == 201);
        var horizontal = MinecraftPfp.Render(
            skin,
            64,
            64,
            128,
            false,
            12,
            [1, 2, 3],
            portrait: true,
            shadow: false,
            gradientEnd: [201, 202, 203],
            gradientAngle: 0
        );
        Check(horizontal[127 * 4] == 201 && horizontal[(127 * 128) * 4] == 1);
        try
        {
            MinecraftPfp.Render([], 64, 64, 512, true, 0);
            throw new Exception("Malformed skin accepted");
        }
        catch (InvalidDataException) { }
        try
        {
            MinecraftPfp.Render(skin, 64, 64, 4000, true, 0);
            throw new Exception("Invalid output size accepted");
        }
        catch (ArgumentOutOfRangeException) { }
    }
}
