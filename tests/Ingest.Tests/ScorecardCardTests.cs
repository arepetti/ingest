using Ingest.Api.Odata;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="ScorecardCard.FromResult"/>: the flattening of the nested scorecard result
/// into the one-row-per-cell shape exposed over OData (the <c>scorecard(mode,period)</c> function).
/// </summary>
public class ScorecardCardTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ExploreScorecardResult Sample(params ExploreScorecardCell[] cells) => new(
        new[]
        {
            new ExploreServiceRef(ServiceA, "svc_a", "Alpha"),
            new ExploreServiceRef(ServiceB, "svc_b", "Bravo"),
        },
        new[]
        {
            new ExploreScorecardSchema("waste", "Waste", new[]
            {
                new ExploreScorecardValue(
                    "recycling", "Recycling", "%", Cadence.Monthly,
                    AmberMin: 0, GreenMin: 50, GreenMax: 80, AmberMax: 100, cells),
            }),
        });

    [Fact]
    public void FromResult_flattens_and_denormalises_a_classified_cell()
    {
        var submissionId = Guid.NewGuid();
        var submittedAt = Start.AddHours(5);
        var result = Sample(
            new ExploreScorecardCell(ServiceA, submissionId, 60d, RagStatus.Green, Start, End, submittedAt));

        var card = Assert.Single(ScorecardCard.FromResult(result));

        Assert.Equal("waste", card.SchemaName);
        Assert.Equal("Waste", card.SchemaLabel);
        Assert.Equal("recycling", card.ValueName);
        Assert.Equal("Recycling", card.ValueLabel);
        Assert.Equal("%", card.Unit);
        Assert.Equal(Cadence.Monthly, card.Cadence);
        Assert.Equal(ServiceA, card.ServiceId);
        Assert.Equal("svc_a", card.ServiceName);
        Assert.Equal("Alpha", card.ServiceLabel);
        Assert.Equal(Start, card.PeriodStart);
        Assert.Equal(End, card.PeriodEnd);
        Assert.Equal(60d, card.Value);
        Assert.Equal("Green", card.Status);
        Assert.Equal(submissionId, card.SubmissionId);
        Assert.Equal(submittedAt, card.SubmittedAt);
        Assert.Equal(0d, card.AmberMin);
        Assert.Equal(50d, card.GreenMin);
        Assert.Equal(80d, card.GreenMax);
        Assert.Equal(100d, card.AmberMax);
    }

    [Fact]
    public void FromResult_renders_a_missing_cell_as_Missing_with_null_payload()
    {
        var result = Sample(
            new ExploreScorecardCell(ServiceB, null, null, null, Start, End, null));

        var card = Assert.Single(ScorecardCard.FromResult(result));

        Assert.Equal("Missing", card.Status);
        Assert.Null(card.Value);
        Assert.Null(card.SubmissionId);
        Assert.Null(card.SubmittedAt);
        Assert.Equal("Bravo", card.ServiceLabel); // still resolved from the service refs
    }

    [Fact]
    public void FromResult_keys_are_stable_and_unique_per_cell()
    {
        var result = Sample(
            new ExploreScorecardCell(ServiceA, Guid.NewGuid(), 60d, RagStatus.Green, Start, End, Start),
            new ExploreScorecardCell(ServiceB, null, null, null, Start, End, null));

        var cards = ScorecardCard.FromResult(result).ToList();

        Assert.Equal(2, cards.Count);
        Assert.Equal(cards.Select(c => c.Id).Distinct().Count(), cards.Count);
        Assert.All(cards, c => Assert.Contains("waste|recycling|", c.Id));
    }
}
