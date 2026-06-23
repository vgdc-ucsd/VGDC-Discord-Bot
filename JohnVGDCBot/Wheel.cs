using NetCord.Services.ApplicationCommands;
using FFMpegCore;
using FFMpegCore.Extensions.SkiaSharp;
using FFMpegCore.Pipes;
using SkiaSharp;
using System.Drawing;
using System.Numerics;
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
    private const float BASE_FONT_SIZE = 30f;
    private const float TEXT_MARGIN = 20f;
    private const float MIN_TEXT_SECTOR_GAP = 4f;
    private const float WHEEL_MARGIN = 20f;
    private const float SELECTOR_WIDTH = 30f;
    private const float SELECTOR_ARC_LENGTH = 30f;

    public static SKColor NetcordColorToSkColor(NetCord.Color color)
        => new(color.Red, color.Green, color.Blue);

    [SlashCommand("list", "Spins a wheel with the provided options")]
    public async Task SpinList(
        [SlashCommandParameter(Name = "options", Description = "A list of comma separated options (e.g. apple, banana")] string options
        )
    {
        string[] optionsList = [.. options.Split(',').Select(o => o.Trim())];

        await RespondAsync(InteractionCallback.DeferredMessage());

        var random = new Random();
        float randomAngle = (float)random.NextDouble() * 360f;
        string selectedOption = optionsList[GetSectorIndex(randomAngle, optionsList.Length)];

        using var stream = await CreateWheelVideo(optionsList, randomAngle);

        await ModifyResponseAsync(m => 
        {
            m.Attachments = [new AttachmentProperties("wheel.webp", stream)];
        });

        await Task.Delay(TimeSpan.FromSeconds(SPIN_DURATION));

        await ModifyResponseAsync(m =>
        {
            m.Content = $"**We have a winner!**\n## {selectedOption}";
        });
    }

    // Bullshit vibecoded workaround for FFMpeg bug
    private sealed class IndexedVideoPipeSource(IReadOnlyList<IVideoFrame> frames, double frameRate) : IPipeSource
    {
        public string GetStreamArguments() =>
            $"-f rawvideo -r {frameRate} -pix_fmt {frames[0].Format} -s {frames[0].Width}x{frames[0].Height}";

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            foreach (var frame in frames)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await frame.SerializeAsync(outputStream, cancellationToken);
            }
        }
    }

    public static async Task<MemoryStream> CreateWheelVideo(string[] options, float randomAngle)
    {
        var output = new MemoryStream();
        var sink = new StreamPipeSink(output);

        var frames = new List<IVideoFrame>();

        var paint = new SKPaint()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SKColors.Blue,
            StrokeWidth = 3f,
        };

        var bitmaps = new List<SKBitmap>();
        try
        {
            var font = new SKFont();

            int frameCount = (int)(SPIN_DURATION * FPS);
            for (int i = 0; i < frameCount; i++)
            {
                var info = new SKImageInfo(FRAME_WIDTH, FRAME_HEIGHT, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var bitmap = new SKBitmap(info);
                bitmaps.Add(bitmap);

                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);

                float finalAngle = 360f * FULL_ROTATIONS + randomAngle;
                float margin = WHEEL_MARGIN;
                var rect = new SKRect(margin, margin, FRAME_WIDTH - margin, FRAME_HEIGHT - margin);
                float periodSeconds = 1f;
                float degreesPerFrame = 360f / FPS / periodSeconds;

                SKColor[] colors = [
                    NetcordColorToSkColor(VGDCColors.Teal),
                    NetcordColorToSkColor(VGDCColors.Cerulean),
                    NetcordColorToSkColor(VGDCColors.Aqua),
                    NetcordColorToSkColor(VGDCColors.Mint),
                ];

                int sectorCount = options.Length;

                float t = EaseInOutQuint((float)i / frameCount);

                for (int j = 0; j < sectorCount; j++)
                {
                    float degreesPerSector = 360f / sectorCount;
                    paint.Color = GetSectorColor(j, sectorCount, colors);
                    canvas.DrawArc(rect, j * degreesPerSector + t * finalAngle, degreesPerSector, true, paint);
                }

                paint.Color = SKColors.White;
                canvas.DrawCircle(FRAME_WIDTH / 2, FRAME_HEIGHT / 2, CENTER_CIRCLE_RADIUS, paint);

                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                canvas.DrawCircle(FRAME_WIDTH / 2, FRAME_HEIGHT / 2, FRAME_WIDTH / 2 - margin, paint);
                paint.Style = SKPaintStyle.Fill;

                for (int j = 0; j < sectorCount; j++)
                {
                    float degreesPerSector = 360f / sectorCount;

                    canvas.Save();
                    canvas.RotateDegrees(degreesPerSector * (0.5f + j) + t * finalAngle, FRAME_WIDTH / 2f, FRAME_HEIGHT / 2f);
                    paint.Color = SKColors.Black;

                    font.Size = BASE_FONT_SIZE;
                    var fontRect = new SKRect();
                    font.MeasureText(options[j], out fontRect, paint);
                    float wheelRadius = FRAME_WIDTH / 2f - margin;
                    float availableWidth = wheelRadius - CENTER_CIRCLE_RADIUS - 2f * TEXT_MARGIN;
                    float availableHeight = 2f * (wheelRadius * MathF.Sin(degreesPerSector / 2f * MathF.PI / 180f) - MIN_TEXT_SECTOR_GAP);
                    while (fontRect.Width > availableWidth || fontRect.Height > availableHeight)
                    {
                        float shrinkFactor = MathF.Min(availableWidth / fontRect.Width, availableHeight / fontRect.Height);
                        font.Size *= shrinkFactor;
                        font.MeasureText(options[j], out fontRect, paint);
                    }

                    canvas.DrawText(options[j], FRAME_WIDTH - margin - TEXT_MARGIN, FRAME_HEIGHT / 2f + fontRect.Height / 2f, SKTextAlign.Right, font, paint);
                    canvas.Restore();
                }

                var selectorPath = new SKPath();
                float halfAngle = SELECTOR_ARC_LENGTH / 2f * MathF.PI / 180f;
                SKPoint selectorHeadPos = new(FRAME_WIDTH - margin - 0.75f * SELECTOR_WIDTH, FRAME_HEIGHT / 2f);
                SKPoint selectorUpperPos = new(selectorHeadPos.X + SELECTOR_WIDTH * MathF.Cos(halfAngle),
                                    selectorHeadPos.Y - SELECTOR_WIDTH * MathF.Sin(halfAngle));
                SKPoint selectorLowerPos = new(selectorHeadPos.X + SELECTOR_WIDTH * MathF.Cos(halfAngle),
                                    selectorHeadPos.Y + SELECTOR_WIDTH * MathF.Sin(halfAngle));
                selectorPath.MoveTo(selectorHeadPos);
                selectorPath.LineTo(selectorUpperPos);
                selectorPath.LineTo(selectorLowerPos);
                selectorPath.Close();
                paint.Color = GetSectorColor(GetSectorIndex(t * finalAngle, options.Length), sectorCount, colors);
                canvas.DrawPath(selectorPath, paint);
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 2f;
                canvas.DrawPath(selectorPath, paint);
                paint.Style = SKPaintStyle.Fill;

                frames.Add(new BitmapVideoFrameWrapper(bitmap));

                // Add copies of the last frame as the result frames
                if (i == frameCount - 1)
                {
                    int resultFrameCount = (int)(RESULT_DURATION * FPS);
                    for (int j = 0; j < resultFrameCount; ++j) 
                        frames.Add((BitmapVideoFrameWrapper)frames[i]);
                }
            } 

            var source = new IndexedVideoPipeSource(frames, FPS);
            await FFMpegArguments
                .FromPipeInput(source)
                .OutputToPipe(sink, options => options
                    .WithVideoCodec("libwebp_anim")
                    .WithCustomArgument("-loop 0")
                    .WithCustomArgument("-lossless 1")
                    .ForceFormat("webp"))
                .ProcessAsynchronously();
        }
        finally
        {
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }


        output.Position = 0;

        return output;
    }

    public static SKColor GetSectorColor(int sectorIndex, int sectorCount, SKColor[] colors) =>
        sectorIndex == sectorCount - 1 && sectorIndex != 0 && sectorIndex % colors.Length == 0 // We don't want two adjacent sectors to have the same color
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
