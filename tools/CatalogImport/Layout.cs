using System.Text.Json;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CatalogImport;

/// <summary>One OCR line with the visual cues the text alone loses: grey heading bands, ● dots and red ritual titles.</summary>
public sealed record LayoutLine(
    int Page, int Column, int X0, int Y0, int X1, int Y1, string Text,
    int Background, int Dots, bool Red, int Height);

/// <summary>Renders PDF pages, OCRs them and measures each line's surroundings.</summary>
public static class Layout
{
    public const int RenderWidth = 1800;

    public static async Task<List<LayoutLine>> ReadAsync(string pdfPath, IEnumerable<int> pages, string? pngDir)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var doc = await PdfDocument.LoadFromFileAsync(file);
        var ocr = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("No OCR language installed.");
        var result = new List<LayoutLine>();

        foreach (var pageNumber in pages)
        {
            using var page = doc.GetPage((uint)(pageNumber - 1));
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = RenderWidth });

            if (pngDir is not null)
            {
                Directory.CreateDirectory(pngDir);
                stream.Seek(0);
                await using var png = File.Create(Path.Combine(pngDir, $"p{pageNumber:D4}.png"));
                await stream.AsStreamForRead().CopyToAsync(png);
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var image = new Image(pixels, (int)decoder.PixelWidth, (int)decoder.PixelHeight);

            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            var ocrResult = await ocr.RecognizeAsync(bitmap);
            foreach (var line in ocrResult.Lines) result.Add(Measure(pageNumber, line, image));
            Console.Error.Write($"\rpage {pageNumber}");
        }
        Console.Error.WriteLine();
        return result;
    }

    private static LayoutLine Measure(int page, OcrLine line, Image image)
    {
        // OCR sometimes reads the ● dots as junk words ("e", "•", "OO"); drop those before the first real word.
        var words = line.Words.SkipWhile(w => !IsRealWord(w.Text)).ToList();
        if (words.Count == 0) words = line.Words.ToList();

        var x0 = (int)words.Min(w => w.BoundingRect.X);
        var y0 = (int)line.Words.Min(w => w.BoundingRect.Y);
        var x1 = (int)words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
        var y1 = (int)line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
        var height = (int)words.Average(w => w.BoundingRect.Height);
        var mid = image.Width / 2;
        var column = x0 < mid - 40 && x1 > mid + 40 ? -1 : (x0 + x1) / 2 < mid ? 0 : 1;

        return new LayoutLine(page, column, x0, y0, x1, y1, string.Join(' ', words.Select(w => w.Text)),
            image.Background(x0, y0, x1, y1), image.CountDots(x0, y0, y1), image.IsRed(words), height);
    }

    private static bool IsRealWord(string text) => text.Count(char.IsLetterOrDigit) >= 2 && text is not ("OO" or "oo" or "OOO" or "ee");

    public static void Save(string path, List<LayoutLine> lines) =>
        File.WriteAllLines(path, lines.Select(l => JsonSerializer.Serialize(l)));

    public static List<LayoutLine> Load(string path) =>
        File.ReadLines(path).Select(l => JsonSerializer.Deserialize<LayoutLine>(l)!).ToList();

    private sealed class Image(byte[] bgra, int width, int height)
    {
        public int Width => width;

        private int Lum(int x, int y)
        {
            var i = (y * width + x) * 4;
            return (bgra[i] * 114 + bgra[i + 1] * 587 + bgra[i + 2] * 299) / 1000;
        }

        /// <summary>Median brightness of the non-ink pixels around a line: ~255 page, ~240 sidebar box, ~220 heading band.</summary>
        public int Background(int x0, int y0, int x1, int y1)
        {
            var values = new List<int>();
            for (var y = Math.Max(0, y0 - 3); y < Math.Min(height, y1 + 3); y += 2)
            for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x += 3)
            {
                var lum = Lum(x, y);
                if (lum > 170) values.Add(lum);
            }
            if (values.Count == 0) return 255;
            values.Sort();
            return values[values.Count / 2];
        }

        /// <summary>Counts round, solid dark blobs in the strip just left of the heading text.</summary>
        public int CountDots(int x0, int y0, int y1)
        {
            var h = y1 - y0;
            var left = Math.Max(0, x0 - h * 8);
            var top = Math.Max(0, y0 - h / 3);
            var bottom = Math.Min(height, y1 + h / 3);
            var right = Math.Max(left, x0 - 2);
            var w = right - left;
            if (w <= 0) return 0;

            var seen = new bool[w * (bottom - top)];
            var dots = 0;
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var idx = (y - top) * w + (x - left);
                if (seen[idx] || Lum(x, y) > 90) continue;

                // Flood fill one dark component.
                int minX = x, maxX = x, minY = y, maxY = y, area = 0;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                seen[idx] = true;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    area++;
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                    minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    {
                        if (nx < left || nx >= right || ny < top || ny >= bottom) continue;
                        var nIdx = (ny - top) * w + (nx - left);
                        if (seen[nIdx] || Lum(nx, ny) > 90) continue;
                        seen[nIdx] = true;
                        stack.Push((nx, ny));
                    }
                }

                var bw = maxX - minX + 1;
                var bh = maxY - minY + 1;
                var round = Math.Abs(bw - bh) <= Math.Max(2, Math.Max(bw, bh) / 4);
                var sized = bh >= h * 0.4 && bh <= h * 1.3;
                var solid = area >= bw * bh * 0.6; // a filled disc covers ~78% of its box
                if (round && sized && solid) dots++;
            }
            return dots;
        }

        /// <summary>True when the ink of the words is clearly red (ritual titles in the sorcery chapters).</summary>
        public bool IsRed(IEnumerable<OcrWord> words)
        {
            long redness = 0, count = 0;
            foreach (var word in words)
            {
                var r = word.BoundingRect;
                for (var y = (int)r.Y; y < Math.Min(height, (int)(r.Y + r.Height)); y++)
                for (var x = (int)r.X; x < Math.Min(width, (int)(r.X + r.Width)); x++)
                {
                    if (Lum(x, y) > 160) continue;
                    var i = (y * width + x) * 4;
                    redness += bgra[i + 2] - (bgra[i] + bgra[i + 1]) / 2;
                    count++;
                }
            }
            return count > 0 && redness / count > 40;
        }
    }
}
