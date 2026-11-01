namespace CMCLauncher;

// Skin coordinates are defined on a 64-pixel atlas. Scale whole texels rather
// than interpolating so HD skins and exported avatars retain their pixel edges.
public static class MinecraftPfp
{
    public static byte[] Render(
        byte[] bgra,
        int width,
        int height,
        int size,
        bool overlay,
        int padding,
        byte[]? background = null,
        bool portrait = false,
        bool shadow = true,
        bool slim = false,
        byte[]? gradientEnd = null,
        double gradientAngle = 90
    )
    {
        if (
            width < 64
            || width > 1024
            || width % 64 != 0
            || height != width && height != width / 2
            || bgra.Length != width * height * 4
        )
            throw new InvalidDataException(
                "Use a 64×64 or 64×32 Minecraft skin, or an HD multiple up to 1024 pixels wide."
            );
        if (
            size is not (128 or 256 or 512 or 1024)
            || padding is < 0 or > 40
            || background != null && background.Length != 3
            || gradientEnd != null && (gradientEnd.Length != 3 || background == null)
            || !double.IsFinite(gradientAngle)
        )
            throw new ArgumentOutOfRangeException(nameof(size));
        if (portrait)
            return Portrait(
                bgra,
                width,
                height,
                size,
                overlay,
                padding,
                background,
                shadow,
                slim,
                gradientEnd,
                gradientAngle
            );
        var result = new byte[size * size * 4];
        FillBackground(result, size, background, gradientEnd, gradientAngle);
        var inset = size * padding / 100;
        var side = size - inset * 2;
        var scale = width / 64;
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            var sx = x * 8 * scale / side;
            var sy = y * 8 * scale / side;
            var face = ((8 * scale + sy) * width + 8 * scale + sx) * 4;
            var hat = ((8 * scale + sy) * width + 40 * scale + sx) * 4;
            var target = ((y + inset) * size + x + inset) * 4;
            var alpha = overlay ? bgra[hat + 3] : 0;
            for (var c = 0; c < 3; c++)
                result[target + c] = (byte)(
                    (bgra[hat + c] * alpha + bgra[face + c] * (255 - alpha) + 127) / 255
                );
            // Minecraft's base head layer is opaque, including older skin files
            // whose PNG transparency did not represent in-game transparency.
            result[target + 3] = 255;
        }
        return result;
    }

    static void FillBackground(byte[] pixels, int size, byte[]? start, byte[]? end, double angle)
    {
        if (start == null)
            return;
        var dx = Math.Cos(angle * Math.PI / 180);
        var dy = Math.Sin(angle * Math.PI / 180);
        var extent = Math.Abs(dx) + Math.Abs(dy);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var t =
                end == null
                    ? 0
                    : Math.Clamp(
                        ((x / (double)(size - 1) - .5) * dx + (y / (double)(size - 1) - .5) * dy)
                            / extent
                            + .5,
                        0,
                        1
                    );
            var p = (y * size + x) * 4;
            for (var c = 0; c < 3; c++)
                pixels[p + c] = (byte)Math.Round(start[c] * (1 - t) + (end?[c] ?? start[c]) * t);
            pixels[p + 3] = 255;
        }
    }

    static byte[] Portrait(
        byte[] skin,
        int width,
        int height,
        int size,
        bool overlay,
        int padding,
        byte[]? background,
        bool shadow,
        bool slim,
        byte[]? gradientEnd,
        double gradientAngle
    )
    {
        var person = new byte[size * size * 4];
        var unit = size * .6 * (1 - padding / 100.0) / 8;
        var head = (int)Math.Round(unit * 8);
        var top = (int)(size * .14);
        var bodyTop = top + head + (int)(unit * .7);
        var arm = slim ? 3 : 4;
        var torsoWidth = (int)Math.Round(unit * 4);
        var armWidth = (int)Math.Round(unit * arm / 2);
        var torsoX = (size - torsoWidth) / 2;
        var bodyHeight = (int)Math.Round(unit * 6);
        var atlas = width / 64;

        void Draw(
            int sx,
            int sy,
            int sw,
            int sh,
            int dx,
            int dy,
            int dw,
            int dh,
            bool outer = false,
            bool flip = false
        )
        {
            for (var y = Math.Max(0, dy); y < Math.Min(size, dy + dh); y++)
            for (var x = Math.Max(0, dx); x < Math.Min(size, dx + dw); x++)
            {
                var tx = (x - dx) * sw * atlas / dw;
                if (flip)
                    tx = sw * atlas - tx - 1;
                var source =
                    ((sy * atlas + (y - dy) * sh * atlas / dh) * width + sx * atlas + tx) * 4;
                var dest = (y * size + x) * 4;
                var alpha = outer ? skin[source + 3] : 255;
                if (alpha == 0)
                    continue;
                for (var c = 0; c < 3; c++)
                    person[dest + c] = (byte)(
                        (skin[source + c] * alpha + person[dest + c] * (255 - alpha) + 127) / 255
                    );
                person[dest + 3] = 255;
            }
        }

        Draw(20, 20, 8, 12, torsoX, bodyTop, torsoWidth, bodyHeight);
        Draw(44, 20, arm, 12, torsoX - armWidth, bodyTop, armWidth, bodyHeight);
        if (height == width)
            Draw(36, 52, arm, 12, torsoX + torsoWidth, bodyTop, armWidth, bodyHeight);
        else
            Draw(44, 20, arm, 12, torsoX + torsoWidth, bodyTop, armWidth, bodyHeight, flip: true);
        if (overlay && height == width)
        {
            Draw(20, 36, 8, 12, torsoX, bodyTop, torsoWidth, bodyHeight, true);
            Draw(44, 36, arm, 12, torsoX - armWidth, bodyTop, armWidth, bodyHeight, true);
            Draw(52, 52, arm, 12, torsoX + torsoWidth, bodyTop, armWidth, bodyHeight, true);
        }
        Draw(
            10,
            15,
            4,
            1,
            (int)(size / 2.0 - unit),
            top + head - 1,
            (int)(unit * 2),
            (int)(unit * .8) + 2
        );
        Draw(8, 8, 8, 8, (size - head) / 2, top, head, head);
        if (overlay)
            Draw(40, 8, 8, 8, (size - head) / 2, top, head, head, true);

        var result = new byte[person.Length];
        FillBackground(result, size, background, gradientEnd, gradientAngle);
        if (shadow)
        {
            var mask = new int[size * size];
            var offset = Math.Max(1, size / 70);
            for (var y = offset; y < size; y++)
            for (var x = 0; x < size; x++)
                mask[y * size + x] = person[((y - offset) * size + x) * 4 + 3];
            // Three separable box passes approximate a soft shadow in linear time.
            var radius = Math.Max(1, size / 65);
            for (var pass = 0; pass < 3; pass++)
            {
                var horizontal = new int[mask.Length];
                var blurred = new int[mask.Length];
                for (var y = 0; y < size; y++)
                {
                    var sum = 0;
                    for (var x = -radius; x < size; x++)
                    {
                        if (x + radius < size)
                            sum += mask[y * size + x + radius];
                        if (x - radius - 1 >= 0)
                            sum -= mask[y * size + x - radius - 1];
                        if (x >= 0)
                            horizontal[y * size + x] = sum / (radius * 2 + 1);
                    }
                }
                for (var x = 0; x < size; x++)
                {
                    var sum = 0;
                    for (var y = -radius; y < size; y++)
                    {
                        if (y + radius < size)
                            sum += horizontal[(y + radius) * size + x];
                        if (y - radius - 1 >= 0)
                            sum -= horizontal[(y - radius - 1) * size + x];
                        if (y >= 0)
                            blurred[y * size + x] = sum / (radius * 2 + 1);
                    }
                }
                mask = blurred;
            }
            for (var i = 0; i < mask.Length; i++)
            {
                var alpha = mask[i] * 150 / 255;
                var p = i * 4;
                if (background != null)
                    for (var c = 0; c < 3; c++)
                        result[p + c] = (byte)(result[p + c] * (255 - alpha) / 255);
                else
                    result[p + 3] = (byte)alpha;
            }
        }
        for (var i = 0; i < result.Length; i += 4)
            if (person[i + 3] != 0)
                Array.Copy(person, i, result, i, 4);
        return result;
    }
}
