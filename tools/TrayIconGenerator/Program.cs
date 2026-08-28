using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var projectDirectory = AppContext.BaseDirectory;
var repositoryDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "../../../../../"));

if (args is ["--extract", var destination])
{
    var iconPath = Path.Combine(repositoryDirectory, "MiHotKeyApp", "tray_keymap_icon.ico");
    using var icon = new Icon(iconPath);
    using var bitmap = icon.ToBitmap();
    bitmap.Save(Path.GetFullPath(destination), ImageFormat.Png);
    return;
}

var outputPath = args is [var path] ? Path.GetFullPath(path) : Path.Combine(repositoryDirectory, "MiHotKeyApp", "tray_keymap_icon.ico");
var sizes = new[] { 16, 32, 48 };
var frames = sizes.Select(CreateArrowPng).ToArray();
WriteIcon(outputPath, sizes, frames);
Console.WriteLine($"Wrote {outputPath} ({string.Join(", ", sizes)} px)");

static byte[] CreateArrowPng(int size)
{
    const int scale = 8;
    using var bitmap = new Bitmap(size * scale, size * scale, PixelFormat.Format32bppArgb);
    bitmap.SetResolution(96, 96);

    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        graphics.ScaleTransform(size * scale / 48f, size * scale / 48f);

        using var background = CreateRoundedRectangle(4, 4, 40, 40, 10);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(255, 31, 89, 130));
        graphics.FillPath(backgroundBrush, background);

        // The arrow points up and right, matching the original icon's direction.
        var arrow = new[]
        {
            new PointF(13, 30), new PointF(18, 35), new PointF(36, 17),
            new PointF(36, 25), new PointF(41, 25), new PointF(41, 8),
            new PointF(24, 8), new PointF(24, 13), new PointF(32, 13),
        };
        using var arrowBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
        graphics.FillPolygon(arrowBrush, arrow);
    }

    using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(resized))
    {
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(bitmap, new Rectangle(0, 0, size, size));
    }

    using var stream = new MemoryStream();
    resized.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static GraphicsPath CreateRoundedRectangle(float x, float y, float width, float height, float radius)
{
    var path = new GraphicsPath();
    var diameter = radius * 2;
    path.AddArc(x, y, diameter, diameter, 180, 90);
    path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
    path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
    path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();
    return path;
}

static void WriteIcon(string path, IReadOnlyList<int> sizes, IReadOnlyList<byte[]> frames)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)sizes.Count);

    var imageOffset = 6 + (16 * sizes.Count);
    for (var index = 0; index < sizes.Count; index++)
    {
        var size = sizes[index];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(frames[index].Length);
        writer.Write(imageOffset);
        imageOffset += frames[index].Length;
    }

    foreach (var frame in frames)
    {
        writer.Write(frame);
    }
}
