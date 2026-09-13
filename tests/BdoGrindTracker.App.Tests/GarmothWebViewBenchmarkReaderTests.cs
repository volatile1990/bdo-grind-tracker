using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothWebViewBenchmarkReaderTests
{
    [Fact]
    public void RecognizesBothPublicSourcesWithoutTreatingStatisticsAsMetadata()
    {
        Assert.True(GarmothWebViewBenchmarkReader.TryMatchResponse(
            "https://api.garmoth.com/api/grind-tracker/collective/all", out var collective, out var index, out var count));
        Assert.True(collective);
        Assert.Equal(-1, index);
        Assert.Equal(0, count);

        Assert.True(GarmothWebViewBenchmarkReader.TryMatchResponse(
            "https://garmoth.com/api/trpc/grindMeta.list", out collective, out index, out count));
        Assert.False(collective);
        Assert.Equal(-1, index);
        Assert.Equal(0, count);
    }

    [Fact]
    public void SelectsOnlyTheMetadataEnvelopeInSingleAndMultipleProcedureBatches()
    {
        Assert.True(GarmothWebViewBenchmarkReader.TryMatchResponse(
            "https://garmoth.com/api/trpc/grindMeta.list?batch=1", out var collective, out var index, out var count));
        Assert.False(collective);
        Assert.Equal(0, index);
        Assert.Equal(1, count);

        // Other procedure names are synthetic; only grindMeta.list belongs in the payload.
        Assert.True(GarmothWebViewBenchmarkReader.TryMatchResponse(
            "https://garmoth.com/api/trpc/other.list,grindMeta.list,another.list?batch=1",
            out collective, out index, out count));
        Assert.False(collective);
        Assert.Equal(1, index);
        Assert.Equal(3, count);
    }

    [Theory]
    [InlineData("http://api.garmoth.com/api/grind-tracker/collective/all")]
    [InlineData("https://garmoth.com:8443/api/trpc/grindMeta.list")]
    [InlineData("https://user@garmoth.com/api/trpc/grindMeta.list")]
    [InlineData("https://api.garmoth.com.example.org/api/grind-tracker/collective/all")]
    [InlineData("https://garmoth.com/api/trpc/grindMeta.listExtra")]
    [InlineData("https://garmoth.com/api/trpc/grindMeta.list,grindMeta.list?batch=1")]
    [InlineData("https://garmoth.com/api/trpc/other.list,grindMeta.list")]
    public void RejectsResponsesWithTheWrongOriginOrAmbiguousMetadataSelection(string address)
    {
        Assert.False(GarmothWebViewBenchmarkReader.TryMatchResponse(address, out _, out _, out _));
    }
}
