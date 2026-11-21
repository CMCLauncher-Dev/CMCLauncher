namespace CMCLauncher;

// Skin coordinates are defined on a 64-pixel atlas. Scale whole texels rather
// than interpolating so HD skins and exported avatars retain their pixel edges.
public static class MinecraftPfp
{
    public const int DefaultShadowDepth = 30;

    // 从皮肤像素数据渲染头像（BGRA 格式）。
    // 支持方形头像（仅头部）与肖像模式（头+身体+手臂），
    // 可选叠加层、背景色/渐变、阴影与细手臂（slim）模型。
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
        double gradientAngle = 90,
        int shadowDepth = DefaultShadowDepth
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
            || background != null && background.Length is not (3 or 4)
            || gradientEnd != null && (gradientEnd.Length is not (3 or 4) || background == null)
            || !double.IsFinite(gradientAngle)
            || shadowDepth is < 0 or > 100
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
                gradientAngle,
                shadowDepth
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

    // Ordered dithering spreads 8-bit rounding across neighboring pixels so
    // slow gradients do not produce full-width bands. Share the threshold across RGB.
    static ReadOnlySpan<byte> GradientThresholds =>
        [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    // 填充背景：纯色（end 为 null）或沿指定角度的双色渐变。
    static void FillBackground(byte[] pixels, int size, byte[]? start, byte[]? end, double angle)
    {
        if (start == null)
            return;
        var dx = Math.Cos(angle * Math.PI / 180);
        var dy = Math.Sin(angle * Math.PI / 180);
        var extent = Math.Abs(dx) + Math.Abs(dy);
        var startAlpha = start.Length == 4 ? start[3] : 255;
        var endAlpha = end == null ? startAlpha : end.Length == 4 ? end[3] : 255;
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
            var threshold = (GradientThresholds[(y & 3) * 4 + (x & 3)] + .5) / 16;
            for (var c = 0; c < 3; c++)
            {
                var value = start[c] + ((end?[c] ?? start[c]) - start[c]) * t;
                pixels[p + c] = (byte)Math.Floor(value + threshold);
            }
            pixels[p + 3] = (byte)Math.Round(startAlpha + (endAlpha - startAlpha) * t);
        }
    }

    // 渲染肖像模式头像：按皮肤图集坐标绘制头部、躯干与手臂，
    // 支持叠加层、细手臂模型、背景与软阴影（三次可分离盒式模糊近似）。
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
        double gradientAngle,
        int shadowDepth
    )
    {
        var person = new byte[size * size * 4];
        // Use a logical pixel grid for the angled portrait. Scaling the grid
        // preserves the silhouette and keeps the bust anchored to the bottom.
        var unit = size / 20.0 * (1 + (12 - padding) / 100.0);
        int X(int column) => (int)Math.Round(size / 2.0 + (column - 10) * unit);
        int Y(int row) => (int)Math.Round(size + (row - 20) * unit);
        var atlas = width / 64;

        // 从皮肤图集的源矩形（sx,sy,sw,sh）绘制到目标矩形（dx,dy,dw,dh），
        // 按整像素缩放（不插值）以保持像素边缘；outer 为 true 时使用叠加层透明度。
        void Draw(
            int sx,
            int sy,
            int sw,
            int sh,
            int x0,
            int y0,
            int logicalWidth,
            int logicalHeight,
            bool outer = false,
            bool flip = false,
            int shade = 100
        )
        {
            var dx = X(x0);
            var dy = Y(y0);
            var dw = X(x0 + logicalWidth) - dx;
            var dh = Y(y0 + logicalHeight) - dy;
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
                        (skin[source + c] * shade / 100 * alpha + person[dest + c] * (255 - alpha) + 127) / 255
                    );
                person[dest + 3] = 255;
            }
        }

        var modern = height == width;
        var sleeveOverlay = overlay && modern;
        // The far sleeve sits behind the chest, while the near sleeve overlaps
        // it. Composite each outer layer with its own base before drawing the next part.
        Draw(modern ? 36 : 44, modern ? 52 : 20, 3, 7, 12, 13, 3, 7, flip: !modern, shade: 90);
        if (sleeveOverlay)
            Draw(52, 52, 3, 7, 12, 13, 3, 7, true, shade: 90);

        Draw(21, 20, 6, 1, 7, 11, 6, 1, shade: 90);
        Draw(20, 21, 8, 8, 6, 12, 8, 8);
        if (sleeveOverlay)
        {
            Draw(21, 36, 6, 1, 7, 11, 6, 1, true, shade: 90);
            Draw(20, 37, 8, 8, 6, 12, 8, 8, true);
        }

        var sleeveWidth = slim ? 2 : 3;
        Draw(44, 20, 3, 7, 8 - sleeveWidth, 13, sleeveWidth, 7, shade: 94);
        if (sleeveOverlay)
            Draw(44, 36, 3, 7, 8 - sleeveWidth, 13, sleeveWidth, 7, true, shade: 94);

        Draw(5, 9, 3, 7, 5, 4, 3, 7, shade: 82);
        Draw(8, 9, 7, 7, 8, 4, 7, 7);
        if (overlay)
        {
            Draw(37, 9, 3, 7, 5, 4, 3, 7, true, shade: 82);
            Draw(40, 9, 7, 7, 8, 4, 7, 7, true);
        }

        var result = new byte[person.Length];
        FillBackground(result, size, background, gradientEnd, gradientAngle);
        if (shadow && shadowDepth > 0)
        {
            var mask = new int[size * size];
            var offset = Math.Max(1, size / 48);
            for (var y = offset; y < size; y++)
            for (var x = 0; x < size; x++)
                mask[y * size + x] = person[((y - offset) * size + x) * 4 + 3];
            // Three separable box passes approximate a soft shadow in linear time.
            var radius = Math.Max(1, size / 52);
            var horizontal = new int[mask.Length];
            for (var pass = 0; pass < 3; pass++)
            {
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
                            mask[y * size + x] = sum / (radius * 2 + 1);
                    }
                }
            }
            for (var i = 0; i < mask.Length; i++)
            {
                var alpha = mask[i] * shadowDepth / 100;
                var p = i * 4;
                if (alpha == 0)
                    continue;
                var backgroundWeight = result[p + 3] * (255 - alpha);
                var totalWeight = alpha * 255 + backgroundWeight;
                for (var c = 0; c < 3; c++)
                    result[p + c] = (byte)(result[p + c] * backgroundWeight / totalWeight);
                result[p + 3] = (byte)((totalWeight + 127) / 255);
            }
        }
        for (var i = 0; i < result.Length; i += 4)
            if (person[i + 3] != 0)
                Array.Copy(person, i, result, i, 4);
        return result;
    }
}
