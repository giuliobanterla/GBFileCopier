#!/usr/bin/env dotnet
// Draws the GBFileCopier icon and writes it in every format the apps need:
//   dotnet run tools/MakeIcons.cs
// Output: assets/icon/ (PNG sizes, icon.ico, GBFileCopier.iconset); on macOS also icon.icns (iconutil).
#:package SkiaSharp@3.119.4
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

using System.Diagnostics;
using SkiaSharp;

var root = FindRepositoryRoot();
var output = Path.Combine(root, "assets", "icon");
var iconset = Path.Combine(output, "GBFileCopier.iconset");
Directory.CreateDirectory(iconset);

// Master drawing on a 1024 × 1024 canvas, following the macOS grid: an 824 × 824 tile with a margin of 100
static void Draw(SKCanvas canvas)
{
    canvas.Clear(SKColors.Transparent);

    // Tile with a vertical blue gradient and a soft shadow
    var tile = new SKRoundRect(SKRect.Create(100, 100, 824, 824), 185, 185);
    using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70), ImageFilter = SKImageFilter.CreateBlur(14, 14) })
    {
        canvas.Save();
        canvas.Translate(0, 12);
        canvas.DrawRoundRect(tile, shadow);
        canvas.Restore();
    }

    using (var background = new SKPaint { IsAntialias = true })
    {
        background.Shader = SKShader.CreateLinearGradient(
            new SKPoint(512, 100), new SKPoint(512, 924),
            [new SKColor(0x3B, 0x8B, 0xF6), new SKColor(0x1D, 0x4E, 0xD8)], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(tile, background);
    }

    // Two sheets: the original behind, the copy in front
    DrawSheet(canvas, SKRect.Create(268, 236, 360, 460), new SKColor(255, 255, 255, 120), lines: false);
    DrawSheet(canvas, SKRect.Create(380, 330, 360, 460), SKColors.White, lines: true);

    // Green badge with a check mark: the copy is verified
    using (var badge = new SKPaint { IsAntialias = true, Color = new SKColor(0x16, 0xA3, 0x4A) })
    using (var ring = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 22 })
    using (var check = new SKPaint
    {
        IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke,
        StrokeWidth = 40, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    })
    {
        canvas.DrawCircle(724, 738, 134, badge);
        canvas.DrawCircle(724, 738, 134, ring);
        using var path = new SKPath();
        path.MoveTo(662, 740);
        path.LineTo(706, 784);
        path.LineTo(790, 692);
        canvas.DrawPath(path, check);
    }
}

static void DrawSheet(SKCanvas canvas, SKRect rect, SKColor color, bool lines)
{
    const float fold = 90;
    using var paint = new SKPaint { IsAntialias = true, Color = color };
    using var path = new SKPath();
    path.MoveTo(rect.Left + 36, rect.Top);
    path.LineTo(rect.Right - fold, rect.Top);
    path.LineTo(rect.Right, rect.Top + fold);
    path.LineTo(rect.Right, rect.Bottom - 36);
    path.QuadTo(rect.Right, rect.Bottom, rect.Right - 36, rect.Bottom);
    path.LineTo(rect.Left + 36, rect.Bottom);
    path.QuadTo(rect.Left, rect.Bottom, rect.Left, rect.Bottom - 36);
    path.LineTo(rect.Left, rect.Top + 36);
    path.QuadTo(rect.Left, rect.Top, rect.Left + 36, rect.Top);
    path.Close();
    canvas.DrawPath(path, paint);

    if (!lines)
    {
        return;
    }

    // Folded corner and text lines
    using var corner = new SKPaint { IsAntialias = true, Color = new SKColor(0xC7, 0xD7, 0xF5) };
    using var cornerPath = new SKPath();
    cornerPath.MoveTo(rect.Right - fold, rect.Top);
    cornerPath.LineTo(rect.Right - fold, rect.Top + fold - 18);
    cornerPath.QuadTo(rect.Right - fold, rect.Top + fold, rect.Right - fold + 18, rect.Top + fold);
    cornerPath.LineTo(rect.Right, rect.Top + fold);
    cornerPath.Close();
    canvas.DrawPath(cornerPath, corner);

    using var line = new SKPaint { IsAntialias = true, Color = new SKColor(0x93, 0xB4, 0xF0), StrokeWidth = 26, StrokeCap = SKStrokeCap.Round };
    for (var i = 0; i < 4; i++)
    {
        var y = rect.Top + 150 + i * 62;
        var end = i == 3 ? rect.Left + 190 : rect.Right - 60;
        canvas.DrawLine(rect.Left + 60, y, end, y, line);
    }
}

using var master = new SKBitmap(1024, 1024);
using (var canvas = new SKCanvas(master))
{
    Draw(canvas);
}

byte[] Png(int size)
{
    using var scaled = master.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKCubicResampler.Mitchell));
    using var image = SKImage.FromBitmap(scaled);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

// PNG sizes, used by the app window and the Linux/Windows packages
foreach (var size in new[] { 16, 32, 48, 64, 128, 256, 512, 1024 })
{
    File.WriteAllBytes(Path.Combine(output, $"icon-{size}.png"), Png(size));
}

// macOS iconset: icon_<n>x<n>.png and the @2x variants
foreach (var size in new[] { 16, 32, 128, 256, 512 })
{
    File.WriteAllBytes(Path.Combine(iconset, $"icon_{size}x{size}.png"), Png(size));
    File.WriteAllBytes(Path.Combine(iconset, $"icon_{size}x{size}@2x.png"), Png(size * 2));
}

// Windows .ico with PNG entries (supported since Vista)
int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
var images = icoSizes.Select(Png).ToArray();
using (var ico = new BinaryWriter(File.Create(Path.Combine(output, "icon.ico"))))
{
    ico.Write((ushort)0); // reserved
    ico.Write((ushort)1); // type: icon
    ico.Write((ushort)icoSizes.Length);
    var offset = 6 + 16 * icoSizes.Length;
    for (var i = 0; i < icoSizes.Length; i++)
    {
        ico.Write((byte)(icoSizes[i] % 256)); // 256 is written as 0
        ico.Write((byte)(icoSizes[i] % 256));
        ico.Write((byte)0); // no palette
        ico.Write((byte)0);
        ico.Write((ushort)1); // planes
        ico.Write((ushort)32); // bits per pixel
        ico.Write(images[i].Length);
        ico.Write(offset);
        offset += images[i].Length;
    }

    foreach (var image in images)
    {
        ico.Write(image);
    }
}

if (OperatingSystem.IsMacOS())
{
    using var iconutil = Process.Start("iconutil", ["-c", "icns", iconset, "-o", Path.Combine(output, "icon.icns")]);
    iconutil.WaitForExit();
}

Console.WriteLine($"Icons written to {output}");

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GBFileCopier.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Run from inside the repository");
}
