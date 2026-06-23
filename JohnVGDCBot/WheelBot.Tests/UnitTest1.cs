using JohnVGDCBot;
using System;

namespace JohnVGDCBot.WheelBot.Tests;

public class UnitTest1
{
    [Fact]
    public async Task WheelVideoTest()
    {
        var options = new[] { "Apples", "Bananas", "Coconuts", "Durians", "Eggplants", "Frumboblumfuggins" };
        var rand = new Random();
        float randomAngle = (float)rand.NextDouble() * 360f;
        string selectedOption = options[WheelModule.GetSectorIndex(randomAngle, options.Length)];
        await using var stream = await WheelModule.CreateWheelVideo(options, randomAngle);
        await using var file = File.Create("wheel_test.webp");
        await stream.CopyToAsync(file);
        Console.WriteLine($"{randomAngle}: {selectedOption}");
    }
}
