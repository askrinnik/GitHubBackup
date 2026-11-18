using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Holds the pixels of a screenshot as 32-bit ARGB values, so that the measurements read them without the locking and
/// the per-call cost of <see cref="Bitmap.GetPixel(int, int)"/>.
/// </summary>
internal sealed class PixelImage
{
    /// <summary>The pixels, row by row from the top left corner.</summary>
    private readonly int[] _pixels;

    /// <summary>
    /// Wraps pixels laid out row by row.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixels">The ARGB value of every pixel; the array is not copied.</param>
    /// <exception cref="ArgumentException">The length of <paramref name="pixels"/> is not the width times the height.</exception>
    public PixelImage(int width, int height, int[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"{pixels.Length} pixels do not fill an image of {width} x {height}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the rectangle that the image covers, with its origin at zero.</summary>
    public Rectangle Bounds => new(0, 0, Width, Height);

    /// <summary>
    /// Gets the ARGB value of one pixel.
    /// </summary>
    /// <param name="x">The column, from the left.</param>
    /// <param name="y">The row, from the top.</param>
    /// <returns>The ARGB value.</returns>
    public int this[int x, int y] => _pixels[(y * Width) + x];

    /// <summary>
    /// Copies the pixels of a bitmap.
    /// </summary>
    /// <param name="bitmap">The bitmap, in any pixel format.</param>
    /// <returns>The image.</returns>
    public static PixelImage FromBitmap(Bitmap bitmap)
    {
        var pixels = new int[bitmap.Width * bitmap.Height];
        var data = bitmap.LockBits(
            new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < bitmap.Height; row++)
            {
                Marshal.Copy(data.Scan0 + (row * data.Stride), pixels, row * bitmap.Width, bitmap.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new(bitmap.Width, bitmap.Height, pixels);
    }

    /// <summary>
    /// Reads an image file, such as a PNG.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The image.</returns>
    public static PixelImage Load(string path)
    {
        using var bitmap = new Bitmap(path);
        return FromBitmap(bitmap);
    }

    /// <summary>
    /// Returns the relative luminance of one pixel, without gamma correction, which is enough to compare two colors
    /// of one screenshot.
    /// </summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The luminance from 0 (black) to 255 (white).</returns>
    public double Luminance(int x, int y) => Luminance(this[x, y]);

    /// <summary>
    /// Returns the relative luminance of an ARGB value, without gamma correction.
    /// </summary>
    /// <param name="argb">The ARGB value.</param>
    /// <returns>The luminance from 0 (black) to 255 (white).</returns>
    public static double Luminance(int argb)
    {
        var color = Color.FromArgb(argb);
        return (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
    }

    /// <summary>
    /// Returns how far apart two colors are: the largest difference of their red, green and blue values.
    /// </summary>
    /// <remarks>
    /// Colored text such as an error in red can have the luminance of its background; the largest channel difference
    /// still tells it apart.
    /// </remarks>
    /// <param name="first">The first ARGB value.</param>
    /// <param name="second">The second ARGB value.</param>
    /// <returns>The difference from 0 (same color) to 255.</returns>
    public static int Distance(int first, int second)
    {
        var a = Color.FromArgb(first);
        var b = Color.FromArgb(second);
        return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    }

    /// <summary>
    /// Writes the image to a PNG file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    public void Save(string path)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < Height; row++)
            {
                Marshal.Copy(_pixels, row * Width, data.Scan0 + (row * data.Stride), Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
