using NetCord.Services.ApplicationCommands;
using FFMpegCore;
using FFMpegCore.Pipes;
using SkiaSharp;
using NetCord.Rest;

namespace JohnVGDCBot;

public partial class WheelModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private const int FPS = 30;
    private const float SPIN_DURATION = 2.5f;
    private const float RESULT_DURATION = 2.0f;
    private const float FULL_ROTATIONS = 3f;

    private const int OUTPUT_SIZE = 512;
    private const float CENTER = OUTPUT_SIZE / 2f;
    private const float BASE_FONT_SIZE = 32f;
    private const float TEXT_MARGIN = 20f;
    private const float MIN_TEXT_SECTOR_GAP = 4f;
    private const int MAX_OPTIONS = 30;

    // Pixel measurements from Assets/WheelFrame.png (1126x1110), update if the art changes
    private const float ART_CENTER_X = 562.5f;
    private const float ART_CENTER_Y = 558f;
    private const float ART_RING_RADIUS = 414f;
    private const float ART_HUB_RADIUS = 66f;
    private const float ART_EXTENT = 556f;

    private const float ART_SCALE = CENTER / ART_EXTENT;
    private const float WHEEL_RADIUS = ART_RING_RADIUS * ART_SCALE;
    private const float HUB_RADIUS = ART_HUB_RADIUS * ART_SCALE;

    private static readonly SKColor[] WheelColors = [
        NetcordColorToSkColor(VGDCColors.Teal),
        NetcordColorToSkColor(VGDCColors.Cerulean),
        NetcordColorToSkColor(VGDCColors.Aqua),
        NetcordColorToSkColor(VGDCColors.Mint),
    ];

    private static readonly Lazy<SKTypeface> Typeface = new(() => SKTypeface.FromStream(OpenResource("Fonts.Bungee-Regular.ttf")));
    private static readonly Lazy<SKImage> Overlay = new(CreateOverlay);

    public static SKColor NetcordColorToSkColor(NetCord.Color color)
        => new(color.Red, color.Green, color.Blue);

    [SlashCommand("wheel", "Spins a wheel with the provided options")]
    public async Task SpinList(
        [SlashCommandParameter(Name = "options", Description = "A list of comma separated options (e.g. apple, banana")] string options
        )
    {
        string[] optionsList = options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (optionsList.Length == 0 || optionsList.Length > MAX_OPTIONS)
        {
            await RespondAsync(InteractionCallback.Message(new()
            {
                Content = $"Give me between 1 and {MAX_OPTIONS} options, separated by commas",
                Flags = NetCord.MessageFlags.Ephemeral,
            }));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var random = new Random();
        float randomAngle = (float)random.NextDouble() * 360f;
        string selectedOption = optionsList[GetSectorIndex(randomAngle, optionsList.Length)];

        try
        {
            byte[] videoBytes;
            await using (var ms = await CreateWheelVideo(optionsList, randomAngle))
                videoBytes = ms.ToArray();

            await ModifyResponseAsync(m =>
            {
                m.Attachments = [new AttachmentProperties("wheel.webp", new MemoryStream(videoBytes))];
            });

            await Task.Delay(TimeSpan.FromSeconds(SPIN_DURATION));

            await ModifyResponseAsync(m =>
            {
                m.Content = $"**We have a winner!**\n## {selectedOption}";
                m.Attachments = [new AttachmentProperties("wheel.webp", new MemoryStream(videoBytes))];
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CreateWheelVideo failed: {ex}");
            await ModifyResponseAsync(m => m.Content = "Something went wrong generating the wheel");
        }
    }

    private sealed class WheelFramePipeSource(
        string[] options,
        float randomAngle,
        float[] fontSizes) : IPipeSource
    {
        public string GetStreamArguments() =>
            $"-f rawvideo -r {FPS} -pix_fmt bgra -s {OUTPUT_SIZE}x{OUTPUT_SIZE}";

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            float finalAngle = 360f * FULL_ROTATIONS + randomAngle;

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var font = new SKFont(Typeface.Value, BASE_FONT_SIZE);

            // ffmpeg expects straight alpha but skia draws premultiplied, so convert each frame before writing
            using var bitmap = new SKBitmap(new SKImageInfo(OUTPUT_SIZE, OUTPUT_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            using var unpremulBitmap = new SKBitmap(new SKImageInfo(OUTPUT_SIZE, OUTPUT_SIZE, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            using var srcPixmap = bitmap.PeekPixels();
            using var dstPixmap = unpremulBitmap.PeekPixels();

            byte[] buffer = new byte[OUTPUT_SIZE * OUTPUT_SIZE * 4];

            int frameCount = (int)(SPIN_DURATION * FPS);
            int resultFrameCount = (int)(RESULT_DURATION * FPS);

            for (int i = 0; i < frameCount + resultFrameCount; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                float t = i < frameCount ? EaseInOutQuint((float)i / frameCount) : 1f;
                DrawWheelFrame(canvas, options, fontSizes, font, paint, t * finalAngle);

                srcPixmap.ReadPixels(dstPixmap);
                unpremulBitmap.GetPixelSpan().CopyTo(buffer);
                await outputStream.WriteAsync(buffer, cancellationToken);
            }
        }
    }

    private static void DrawWheelFrame(SKCanvas canvas, string[] options, float[] fontSizes, SKFont font, SKPaint paint, float rotation)
    {
        int sectorCount = options.Length;
        float degreesPerSector = 360f / sectorCount;
        var rect = new SKRect(CENTER - WHEEL_RADIUS, CENTER - WHEEL_RADIUS, CENTER + WHEEL_RADIUS, CENTER + WHEEL_RADIUS);

        canvas.Clear(SKColors.Transparent);

        paint.Style = SKPaintStyle.Fill;
        for (int j = 0; j < sectorCount; j++)
        {
            paint.Color = GetSectorColor(j, sectorCount, WheelColors);
            canvas.DrawArc(rect, j * degreesPerSector + rotation, degreesPerSector, true, paint);
        }

        paint.Color = SKColors.Black;
        for (int j = 0; j < sectorCount; j++)
        {
            canvas.Save();
            float labelRotation = sectorCount > 1
                ? degreesPerSector * (0.5f + j) + rotation
                : rotation;
            canvas.RotateDegrees(labelRotation, CENTER, CENTER);
            font.Size = fontSizes[j];
            font.MeasureText(options[j], out var fontRect, paint);
            canvas.DrawText(options[j], CENTER + WHEEL_RADIUS - TEXT_MARGIN, CENTER + fontRect.Height / 2f, SKTextAlign.Right, font, paint);
            canvas.Restore();
        }

        canvas.DrawImage(Overlay.Value, 0, 0);
    }

    private static SKImage CreateOverlay()
    {
        using var frame = LoadImage("Assets.WheelFrame.png");
        using var pointer = LoadImage("Assets.WheelPointer.png");

        using var surface = SKSurface.Create(new SKImageInfo(OUTPUT_SIZE, OUTPUT_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(CENTER, CENTER);
        canvas.Scale(ART_SCALE);
        canvas.Translate(-ART_CENTER_X, -ART_CENTER_Y);

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(frame, 0, 0, sampling);
        canvas.DrawImage(pointer, 0, 0, sampling);

        return surface.Snapshot();
    }

    private static Stream OpenResource(string name) =>
        typeof(WheelModule).Assembly.GetManifestResourceStream($"JohnVGDCBot.{name}")
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");

    private static SKImage LoadImage(string name)
    {
        using var stream = OpenResource(name);
        using var data = SKData.Create(stream);
        return SKImage.FromEncodedData(data);
    }

    private static float[] GetFontSizes(string[] options)
    {
        int sectorCount = options.Length;
        float degreesPerSector = 360f / sectorCount;
        float availableWidth = WHEEL_RADIUS - HUB_RADIUS - 2f * TEXT_MARGIN;
        float availableHeight = sectorCount > 1
            ? 2f * (WHEEL_RADIUS * MathF.Sin(degreesPerSector / 2f * MathF.PI / 180f) - MIN_TEXT_SECTOR_GAP)
            : 2f * (WHEEL_RADIUS - MIN_TEXT_SECTOR_GAP);

        using var measurePaint = new SKPaint { IsAntialias = true };
        using var measureFont = new SKFont(Typeface.Value, BASE_FONT_SIZE);
        float[] fontSizes = new float[sectorCount];
        for (int j = 0; j < sectorCount; j++)
        {
            measureFont.Size = BASE_FONT_SIZE;
            measureFont.MeasureText(options[j], out var fontRect, measurePaint);
            while (fontRect.Width > availableWidth || fontRect.Height > availableHeight)
            {
                float shrinkFactor = MathF.Min(availableWidth / fontRect.Width, availableHeight / fontRect.Height);
                measureFont.Size *= shrinkFactor;
                measureFont.MeasureText(options[j], out fontRect, measurePaint);
            }
            fontSizes[j] = measureFont.Size;
        }
        return fontSizes;
    }

    public static async Task<MemoryStream> CreateWheelVideo(string[] options, float randomAngle)
    {
        var output = new MemoryStream();
        var sink = new StreamPipeSink(output);

        var source = new WheelFramePipeSource(options, randomAngle, GetFontSizes(options));
        await FFMpegArguments
            .FromPipeInput(source)
            .OutputToPipe(sink, o => o
                .WithVideoCodec("libwebp_anim")
                .WithCustomArgument("-pix_fmt yuva420p")
                .WithCustomArgument("-loop 0")
                .WithCustomArgument("-lossless 0")
                .WithCustomArgument("-quality 80")
                .ForceFormat("webp"))
            .ProcessAsynchronously();

        output.Position = 0;
        return output;
    }

    // Still of the last frame, handy for checking the art in tests
    public static byte[] RenderFinalFramePng(string[] options, float randomAngle)
    {
        float[] fontSizes = GetFontSizes(options);

        using var paint = new SKPaint { IsAntialias = true };
        using var font = new SKFont(Typeface.Value, BASE_FONT_SIZE);
        using var bitmap = new SKBitmap(new SKImageInfo(OUTPUT_SIZE, OUTPUT_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        DrawWheelFrame(canvas, options, fontSizes, font, paint, 360f * FULL_ROTATIONS + randomAngle);

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static SKColor GetSectorColor(int sectorIndex, int sectorCount, SKColor[] colors) =>
        sectorIndex == sectorCount - 1 && sectorIndex != 0 && sectorIndex % colors.Length == 0
            ? colors[(sectorIndex + 1) % colors.Length]
            : colors[sectorIndex % colors.Length];

    public static float Wrap(float value, float min, float max)
    {
        float range = max - min;
        if (range == 0f) return min;

        return min + (value - min) % range;
    }

    public static int GetSectorIndex(float angleDegrees, int totalSectors)
        => (int)((1f - Wrap(angleDegrees, 0f, 360f) / 360f) * totalSectors) % totalSectors;

    public static float Smoothstep(float x) => 3f * x * x - 2 * x * x * x;
    public static float EaseInOutCirc(float x) => x < 0.5f
        ? 0.5f * (1f - MathF.Sqrt(1f - 4f * x * x))
        : 0.5f * (1f + MathF.Sqrt(1f - 4f * (x - 1f) * (x - 1f)));
    public static float EaseInOutQuint(float x) => x < 0.5f
        ? 16f * x * x * x * x * x
        : 1f - 0.5f * MathF.Pow(-2f * x + 2f, 5f);
}
