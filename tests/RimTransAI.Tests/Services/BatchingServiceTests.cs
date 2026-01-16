using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class BatchingServiceTests
{
    private readonly BatchingService _sut = new();

    [Fact]
    public void CreateBatches_EmptyList_ReturnsEmptyResult()
    {
        var result = _sut.CreateBatches(new List<IGrouping<string, TranslationItem>>());

        result.TotalBatches.Should().Be(0);
        result.Batches.Should().BeEmpty();
    }

    [Fact]
    public void CreateBatches_NullList_ReturnsEmptyResult()
    {
        var result = _sut.CreateBatches(null!);

        result.TotalBatches.Should().Be(0);
    }

    [Fact]
    public void CreateBatches_ShortTexts_AggregatesIntoBatches()
    {
        var items = CreateTestItems(new[] { "label1", "label2", "label3", "label4", "label5" });
        var groups = items.GroupBy(x => x.OriginalText).ToList();

        var result = _sut.CreateBatches(groups, maxTokensPerBatch: 3000);

        result.TotalBatches.Should().BeLessThanOrEqualTo(2);
        result.OversizedBatches.Should().Be(0);
    }

    [Fact]
    public void CreateBatches_RespectsMaxItemsPerBatch()
    {
        var texts = Enumerable.Range(1, 100).Select(i => $"item{i}").ToArray();
        var items = CreateTestItems(texts);
        var groups = items.GroupBy(x => x.OriginalText).ToList();

        var result = _sut.CreateBatches(groups, maxItemsPerBatch: 20);

        result.TotalBatches.Should().BeGreaterOrEqualTo(5);
    }

    private List<TranslationItem> CreateTestItems(string[] texts)
    {
        return texts.Select((t, i) => new TranslationItem
        {
            Key = $"Test.item{i}",
            OriginalText = t,
            DefType = "TestDef"
        }).ToList();
    }
}
