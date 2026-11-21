using System.Numerics;

namespace CMCLauncher;

static class SkinPreviewRenderer
{
    const int AtlasWidth = 64;
    const int BytesPerPixel = 4;

    readonly record struct Face(
        Vector3 Origin, Vector3 Across, Vector3 Down,
        int U, int V, int Width, int Height, float Light, bool Overlay, bool Mirror
    );

    public static byte[] Render(byte[] texture, int height, bool slim, int size, float yaw, float pitch, SkinPose pose)
    {
        if (height is not (32 or 64) || texture.Length != AtlasWidth * height * BytesPerPixel)
            throw new ArgumentException("Invalid skin texture.", nameof(texture));

        var modern = height == 64;
        var arm = modern && slim ? 3 : 4;
        var faces = new List<Face>();
        AddBox(0, 28, 0, 8, 8, 8, 0, 0);
        AddBox(0, 28, 0, 8, 8, 8, 32, 0, overlay: .5f);
        AddBox(0, 18, 0, 8, 12, 4, 16, 16);
        AddBox(-4 - arm / 2f, 18, 0, arm, 12, 4, 40, 16);
        AddBox(4 + arm / 2f, 18, 0, arm, 12, 4, modern ? 32 : 40, modern ? 48 : 16, mirror: !modern);
        AddBox(-2, 6, 0, 4, 12, 4, 0, 16);
        AddBox(2, 6, 0, 4, 12, 4, modern ? 16 : 0, modern ? 48 : 16, mirror: !modern);
        if (modern)
        {
            AddBox(0, 18, 0, 8, 12, 4, 16, 32, overlay: .25f);
            AddBox(-4 - arm / 2f, 18, 0, arm, 12, 4, 40, 32, overlay: .25f);
            AddBox(4 + arm / 2f, 18, 0, arm, 12, 4, 48, 48, overlay: .25f);
            AddBox(-2, 6, 0, 4, 12, 4, 0, 32, overlay: .25f);
            AddBox(2, 6, 0, 4, 12, 4, 0, 48, overlay: .25f);
        }

        var view = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, view));
        var up = Vector3.Cross(view, right);
        var scale = size / 41f;
        var pixels = new byte[size * size * BytesPerPixel];
        var depths = new float[size * size];
        Array.Fill(depths, float.NegativeInfinity);
        foreach (var face in faces.OrderBy(face => face.Overlay)
                     .ThenBy(face => Vector3.Dot(face.Origin + (face.Across + face.Down) / 2, view)))
            if (Vector3.Dot(Vector3.Cross(face.Down, face.Across), view) > .001f)
                Paint(face);
        return pixels;

        void AddBox(float x, float y, float z, int w, int h, int d, int u, int v, float overlay = 0, bool mirror = false)
        {
            var left = x - w / 2f - overlay;
            var top = y + h / 2f + overlay;
            var front = z + d / 2f + overlay;
            var back = z - d / 2f - overlay;
            var width = w + 2 * overlay;
            var vertical = new Vector3(0, -h - 2 * overlay, 0);
            var horizontal = new Vector3(width, 0, 0);
            var depth = new Vector3(0, 0, d + 2 * overlay);
            var stride = pose == SkinPose.Running ? .96f : pose == SkinPose.Walking ? .52f : 0;
            var side = MathF.Sign(x);
            var limbPitch = y == 6 ? side * stride : x != 0 && y == 18 ? -side * stride : 0;
            var shoulderRoll = pose == SkinPose.Waving && x < -4 && y == 18 ? -2.36f : 0;
            var rotation = Quaternion.CreateFromYawPitchRoll(0, limbPitch, shoulderRoll);
            var pivot = new Vector3(x, y + h / 2f - 1, z);
            void AddFace(Vector3 origin, Vector3 across, Vector3 down, int atlasX, int atlasY, int sw, int sh,
                float light, bool flip)
            {
                faces.Add(new(pivot + Vector3.Transform(origin - pivot, rotation),
                    Vector3.Transform(across, rotation), Vector3.Transform(down, rotation),
                    atlasX, atlasY, sw, sh, light, overlay > 0, flip));
            }
            var right = left + width;
            var bottom = top + vertical.Y;
            AddFace(new(left, top, front), horizontal, vertical, u + d, v + d, w, h, 1, mirror);
            AddFace(new(right, top, front), -depth, vertical, mirror ? u : u + d + w, v + d, d, h, .76f, false);
            AddFace(new(left, top, back), depth, vertical, mirror ? u + d + w : u, v + d, d, h, .76f, false);
            AddFace(new(right, top, back), -horizontal, vertical, u + d * 2 + w, v + d, w, h, .83f, mirror);
            AddFace(new(left, top, back), horizontal, depth, u + d, v, w, d, .91f, mirror);
            AddFace(new(left, bottom, front), horizontal, -depth, u + d + w, v, w, d, .63f, mirror);
        }

        Vector2 Project(Vector3 point)
        {
            var centered = point - new Vector3(0, 16, 0);
            return new Vector2(size / 2f + Vector3.Dot(centered, right) * scale,
                size / 2f - Vector3.Dot(centered, up) * scale);
        }

        void Paint(Face face)
        {
            var a = Project(face.Origin);
            var across = Project(face.Origin + face.Across) - a;
            var down = Project(face.Origin + face.Down) - a;
            var det = across.X * down.Y - across.Y * down.X;
            var min = a + Vector2.Min(across, Vector2.Zero) + Vector2.Min(down, Vector2.Zero);
            var max = a + Vector2.Max(across, Vector2.Zero) + Vector2.Max(down, Vector2.Zero);
            var startDepth = Vector3.Dot(face.Origin, view);
            var acrossDepth = Vector3.Dot(face.Across, view);
            var downDepth = Vector3.Dot(face.Down, view);
            for (var y = Math.Max(0, (int)MathF.Floor(min.Y)); y < Math.Min(size, (int)MathF.Ceiling(max.Y)); y++)
            for (var x = Math.Max(0, (int)MathF.Floor(min.X)); x < Math.Min(size, (int)MathF.Ceiling(max.X)); x++)
            {
                var offset = new Vector2(x + .5f, y + .5f) - a;
                var u = (offset.X * down.Y - offset.Y * down.X) / det;
                var v = (across.X * offset.Y - across.Y * offset.X) / det;
                if (u < 0 || u >= 1 || v < 0 || v >= 1)
                    continue;
                var index = y * size + x;
                var z = startDepth + acrossDepth * u + downDepth * v;
                if (z < depths[index])
                    continue;
                var tx = (int)(u * face.Width);
                if (face.Mirror)
                    tx = face.Width - 1 - tx;
                var source = ((face.V + (int)(v * face.Height)) * AtlasWidth + face.U + tx) * BytesPerPixel;
                var alpha = face.Overlay ? texture[source + 3] / 255f : 1f;
                if (alpha == 0)
                    continue;
                var dest = index * BytesPerPixel;
                var behind = pixels[dest + 3] / 255f * (1 - alpha);
                var combined = alpha + behind;
                for (var channel = 0; channel < 3; channel++)
                    pixels[dest + channel] = (byte)MathF.Round(
                        (texture[source + channel] * face.Light * alpha + pixels[dest + channel] * behind) / combined);
                pixels[dest + 3] = (byte)MathF.Round(combined * 255);
                depths[index] = z;
            }
        }
    }
}
