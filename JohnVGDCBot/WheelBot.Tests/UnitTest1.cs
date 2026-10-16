using JohnVGDCBot;
using System;

namespace JohnVGDCBot.WheelBot.Tests;

public class UnitTest1
{
    private static readonly string[] Options = ["pizza", "tacos", "sushi", "burgers", "a much longer ramen option"];

    [Fact]
    public async Task WheelVideoTest()
    {
        // System.Diagnostics.Debugger.Launch();
        var rand = new Random();
        float randomAngle = (float)rand.NextDouble() * 360f;
        string selectedOption = Options[WheelModule.GetSectorIndex(randomAngle, Options.Length)];
        await using var stream = await WheelModule.CreateWheelVideo(Options, randomAngle);
        await using var file = File.Create("wheel_test.webp");
        await stream.CopyToAsync(file);
        Console.WriteLine($"{randomAngle}: {selectedOption}");
    }

    [Fact]
    public void WheelFinalFrameTest()
    {
        var rand = new Random();
        float randomAngle = (float)rand.NextDouble() * 360f;
        string selectedOption = Options[WheelModule.GetSectorIndex(randomAngle, Options.Length)];
        File.WriteAllBytes("wheel_test_final.png", WheelModule.RenderFinalFramePng(Options, randomAngle));
        Console.WriteLine($"final frame {randomAngle}: {selectedOption}");
    }
}
