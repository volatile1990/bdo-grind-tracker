using System.Text.Json;

namespace BdoGrindTracker.Core.Tests;

public sealed class DropQuantityBoundsTests
{
    [Theory]
    [InlineData(0u, null)]
    [InlineData(2147483648u, null)]
    [InlineData(uint.MaxValue, null)]
    [InlineData(1u, 0u)]
    [InlineData(4u, 3u)]
    [InlineData(1u, 2147483648u)]
    [InlineData(1u, uint.MaxValue)]
    public void RejectsInvalidBounds(uint minimum, uint? maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DropQuantityBounds(minimum, maximum));
    }

    [Fact]
    public void UnverifiedMaximumStaysUnbounded()
    {
        var bounds = new DropQuantityBounds(4);

        Assert.Equal(4u, bounds.Minimum);
        Assert.Null(bounds.Maximum);
        Assert.Equal(bounds, JsonSerializer.Deserialize<DropQuantityBounds>(JsonSerializer.Serialize(bounds)));
    }

    [Fact]
    public void LargestApplicationQuantityIsSupported()
    {
        var bounds = new DropQuantityBounds(int.MaxValue, int.MaxValue);

        Assert.Equal((uint)int.MaxValue, bounds.Minimum);
        Assert.Equal((uint)int.MaxValue, bounds.Maximum);
    }

    [Fact]
    public void ObservationRoundTripPreservesBoundsAndOriginalOcrQuantity()
    {
        var observation = new LootObservation(LootSource.Rare, 0, "Synthetic Rare x7", "Synthetic Rare",
            7, 1, 1, null, null)
        {
            QuantityBounds = new DropQuantityBounds(1, 1),
            NativeY = 250,
            UsesImplicitUnitQuantity = true,
        };

        var restored = JsonSerializer.Deserialize<LootObservation>(JsonSerializer.Serialize(observation));

        Assert.Equal(observation, restored);
        Assert.Equal(7, restored!.Quantity);
    }

    [Fact]
    public void OlderObservationsRemainReadableWithoutQuantityPolicies()
    {
        var observation = JsonSerializer.Deserialize<LootObservation>(
            """{"Source":1,"Slot":0,"RawText":"Synthetic Rare","ItemName":"Synthetic Rare","Quantity":1,"NameConfidence":1,"QuantityConfidence":0,"VisualFingerprint":null,"RejectionReason":null}""");

        Assert.Null(observation!.QuantityBounds);
        Assert.False(observation.UsesImplicitUnitQuantity);
    }
}
