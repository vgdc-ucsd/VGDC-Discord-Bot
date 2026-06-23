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

    private const int FRAME_WIDTH = 400;
    private const int FRAME_HEIGHT = 400;
    private const float CENTER_CIRCLE_RADIUS = 30f;
    private const float BASE_FONT_SIZE = 32f;
    private const float TEXT_MARGIN = 20f;
    private const float MIN_TEXT_SECTOR_GAP = 4f;
    private const float WHEEL_MARGIN = 20f;
    private const float SELECTOR_WIDTH = 30f;
    private const float SELECTOR_ARC_LENGTH = 30f;

    public static SKColor NetcordColorToSkColor(NetCord.Color color)
        => new(color.Red, color.Green, color.Blue);

    [SlashCommand("wheel", "Spins a wheel with the provided options")]
    public async Task SpinList(
        [SlashCommandParameter(Name = "options", Description = "A list of comma separated options (e.g. apple, banana")] string options
        )
    {
        string[] optionsList = [.. options.Split(',').Select(o => o.Trim())];

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
        float[] fontSizes,
        SKColor[] colors,
        SKTypeface typeface) : IPipeSource
    {
        public string GetStreamArguments() =>
            $"-f rawvideo -r {FPS} -pix_fmt bgra -s {FRAME_WIDTH}x{FRAME_HEIGHT}";

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            int sectorCount = options.Length;
            float degreesPerSector = 360f / sectorCount;
            float finalAngle = 360f * FULL_ROTATIONS + randomAngle;
            float margin = WHEEL_MARGIN;
            var rect = new SKRect(margin, margin, FRAME_WIDTH - margin, FRAME_HEIGHT - margin);

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, StrokeWidth = 2f };
            using var font = new SKFont(typeface, BASE_FONT_SIZE);

            var info = new SKImageInfo(FRAME_WIDTH, FRAME_HEIGHT, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);

            float halfAngle = SELECTOR_ARC_LENGTH / 2f * MathF.PI / 180f;
            SKPoint selectorHeadPos = new(FRAME_WIDTH - margin - 0.75f * SELECTOR_WIDTH, FRAME_HEIGHT / 2f);
            SKPoint selectorUpperPos = new(selectorHeadPos.X + SELECTOR_WIDTH * MathF.Cos(halfAngle),
                                selectorHeadPos.Y - SELECTOR_WIDTH * MathF.Sin(halfAngle));
            SKPoint selectorLowerPos = new(selectorHeadPos.X + SELECTOR_WIDTH * MathF.Cos(halfAngle),
                                selectorHeadPos.Y + SELECTOR_WIDTH * MathF.Sin(halfAngle));
            using var selectorPath = new SKPath();
            selectorPath.MoveTo(selectorHeadPos);
            selectorPath.LineTo(selectorUpperPos);
            selectorPath.LineTo(selectorLowerPos);
            selectorPath.Close();

            byte[] buffer = new byte[FRAME_WIDTH * FRAME_HEIGHT * 4];

            int frameCount = (int)(SPIN_DURATION * FPS);
            int resultFrameCount = (int)(RESULT_DURATION * FPS);

            for (int i = 0; i < frameCount + resultFrameCount; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                float t = i < frameCount ? EaseInOutQuint((float)i / frameCount) : 1f;

                canvas.Clear(SKColors.White);

                paint.Style = SKPaintStyle.Fill;
                for (int j = 0; j < sectorCount; j++)
                {
                    paint.Color = GetSectorColor(j, sectorCount, colors);
                    canvas.DrawArc(rect, j * degreesPerSector + t * finalAngle, degreesPerSector, true, paint);
                }

                paint.Color = SKColors.White;
                canvas.DrawCircle(FRAME_WIDTH / 2, FRAME_HEIGHT / 2, CENTER_CIRCLE_RADIUS, paint);

                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                canvas.DrawCircle(FRAME_WIDTH / 2, FRAME_HEIGHT / 2, FRAME_WIDTH / 2 - margin, paint);
                canvas.DrawCircle(FRAME_WIDTH / 2, FRAME_HEIGHT / 2, CENTER_CIRCLE_RADIUS, paint);
                paint.Style = SKPaintStyle.Fill;

                for (int j = 0; j < sectorCount; j++)
                {
                    canvas.Save();
                    float rotation = sectorCount > 1
                        ? degreesPerSector * (0.5f + j) + t * finalAngle
                        : t * finalAngle;
                    canvas.RotateDegrees(rotation, FRAME_WIDTH / 2f, FRAME_HEIGHT / 2f);
                    paint.Color = SKColors.Black;
                    font.Size = fontSizes[j];
                    font.MeasureText(options[j], out var fontRect, paint);
                    canvas.DrawText(options[j], FRAME_WIDTH - margin - TEXT_MARGIN, FRAME_HEIGHT / 2f + fontRect.Height / 2f, SKTextAlign.Right, font, paint);
                    canvas.Restore();
                }

                paint.Style = SKPaintStyle.Fill;
                paint.Color = GetSectorColor(GetSectorIndex(t * finalAngle, options.Length), sectorCount, colors);
                canvas.DrawPath(selectorPath, paint);
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                canvas.DrawPath(selectorPath, paint);

                bitmap.GetPixelSpan().CopyTo(buffer);
                await outputStream.WriteAsync(buffer, cancellationToken);
            }
        }
    }

    public static async Task<MemoryStream> CreateWheelVideo(string[] options, float randomAngle)
    {
        var output = new MemoryStream();
        var sink = new StreamPipeSink(output);

        SKColor[] colors = [
            NetcordColorToSkColor(VGDCColors.Teal),
            NetcordColorToSkColor(VGDCColors.Cerulean),
            NetcordColorToSkColor(VGDCColors.Aqua),
            NetcordColorToSkColor(VGDCColors.Mint),
        ];

        var assembly = typeof(WheelModule).Assembly;
        using var fontStream = assembly.GetManifestResourceStream("JohnVGDCBot.Fonts.Inter_28pt-SemiBold.ttf");
        var typeface = SKTypeface.FromStream(fontStream);

        int sectorCount = options.Length;
        float degreesPerSector = 360f / sectorCount;
        float wheelRadius = FRAME_WIDTH / 2f - WHEEL_MARGIN;
        float availableWidth = wheelRadius - CENTER_CIRCLE_RADIUS - 2f * TEXT_MARGIN;
        float availableHeight = sectorCount > 1
            ? 2f * (wheelRadius * MathF.Sin(degreesPerSector / 2f * MathF.PI / 180f) - MIN_TEXT_SECTOR_GAP)
            : FRAME_HEIGHT - 2f * WHEEL_MARGIN - 2f * MIN_TEXT_SECTOR_GAP;

        using var measurePaint = new SKPaint { IsAntialias = true };
        using var measureFont = new SKFont(typeface, BASE_FONT_SIZE);
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

        var source = new WheelFramePipeSource(options, randomAngle, fontSizes, colors, typeface);
        await FFMpegArguments
            .FromPipeInput(source)
            .OutputToPipe(sink, o => o
                .WithVideoCodec("libwebp_anim")
                .WithCustomArgument("-loop 0")
                .WithCustomArgument("-lossless 1")
                .ForceFormat("webp"))
            .ProcessAsynchronously();

        output.Position = 0;
        return output;
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
