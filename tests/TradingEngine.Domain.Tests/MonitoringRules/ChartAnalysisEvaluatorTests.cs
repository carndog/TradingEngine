using NodaTime;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartAnalysisEvaluatorTests
{
    private static readonly Guid RevisionId = Guid.Parse("b2000000-0000-0000-0000-000000000001");
    private static readonly Guid NextRevisionId = Guid.Parse("b2000000-0000-0000-0000-000000000002");
    private static readonly Instant Ten = Instant.FromUtc(2026, 11, 1, 10, 0);
    private static readonly Instant Eleven = Instant.FromUtc(2026, 11, 1, 11, 0);
    private static readonly Instant Twelve = Instant.FromUtc(2026, 11, 1, 12, 0);
    private static readonly Instant Thirteen = Instant.FromUtc(2026, 11, 1, 13, 0);
    private static readonly Instant Fourteen = Instant.FromUtc(2026, 11, 1, 14, 0);

    [Test]
    public void Evaluate_WithPriceEnteringBuyZoneFromAbove_ReturnsTriggeredMatch()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsSuccess, Is.True);
        TriggeredCondition match = result.Value.Matches[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Outcome, Is.EqualTo(EvaluationOutcome.Triggered));
            Assert.That(result.Value.Reason, Is.EqualTo(EvaluationReason.ConditionsTriggered));
            Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
            Assert.That(match.ZoneId.Value, Is.EqualTo("support-a"));
            Assert.That(match.Condition, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(match.ActionId.Value, Is.EqualTo("publish-signal"));
            Assert.That(match.Lower, Is.EqualTo(95m));
            Assert.That(match.Level, Is.EqualTo(100m));
            Assert.That(match.Upper, Is.EqualTo(105m));
            Assert.That(match.PreviousPrice, Is.EqualTo(106m));
            Assert.That(match.CurrentPrice, Is.EqualTo(104m));
        });
    }

    [Test]
    public void Evaluate_WithCurrentPriceAtUpperBoundary_ReturnsTriggeredBuyZoneMatch()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 105m, 105m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
        Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
    }

    [Test]
    public void Evaluate_WithCurrentPriceAtLevel_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 100m, 100m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPreviousPriceAtUpperBoundary_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 105m, 105m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceEnteringZoneFromBelow_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 90m, 90m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceRemainingWithinZone_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 103m, 103m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceLeavingZoneUpward_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 103m, 103m), Observation(Eleven, 106m, 106m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithRepeatedPriceAboveZone_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 106m, 106m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceReenteringBuyZoneFromAbove_TriggersAgain()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);

        EvaluationResult first = Evaluate(Input(definition, null, Observation(Ten, 106m, 106m))).Value;
        EvaluationResult entry = Evaluate(Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 104m, 104m))).Value;
        EvaluationResult within = Evaluate(Input(definition, Observation(Eleven, 104m, 104m), Observation(Twelve, 103m, 103m))).Value;
        EvaluationResult leaving = Evaluate(Input(definition, Observation(Twelve, 103m, 103m), Observation(Thirteen, 106m, 106m))).Value;
        EvaluationResult reentry = Evaluate(Input(definition, Observation(Thirteen, 106m, 106m), Observation(Fourteen, 104m, 104m))).Value;

        Assert.Multiple(() =>
        {
            Assert.That(first.Reason, Is.EqualTo(EvaluationReason.FirstObservation));
            Assert.That(entry.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(within.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(leaving.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(reentry.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
        });
    }

    [Test]
    public void Evaluate_WithPriceFallingBelowLower_ReturnsTriggeredSupportLossMatch()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 96m, 96m), Observation(Eleven, 94m, 94m));

        Result<EvaluationResult> result = Evaluate(input);

        TriggeredCondition match = result.Value.Matches[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
            Assert.That(match.Condition, Is.EqualTo(ChartConditionType.SupportLoss));
            Assert.That(match.PreviousPrice, Is.EqualTo(96m));
            Assert.That(match.CurrentPrice, Is.EqualTo(94m));
        });
    }

    [Test]
    public void Evaluate_WithPreviousPriceAtLowerBoundary_ReturnsTriggeredSupportLossMatch()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 95m, 95m), Observation(Eleven, 94m, 94m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.SupportLoss));
    }

    [Test]
    public void Evaluate_WithCurrentPriceAtLowerBoundary_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 96m, 96m), Observation(Eleven, 95m, 95m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceJumpingThroughZone_ReturnsSupportLossOnly()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 94m, 94m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.SupportLoss));
        });
    }

    [Test]
    public void Evaluate_WithPriceExceedingUpper_ReturnsTriggeredBreakoutMatch()
    {
        ChartAnalysisDefinition definition = Definition([], [ResistanceZone("resistance-a", 120m, 125m, 130m)]);
        EvaluationInput input = Input(definition, Observation(Ten, 129m, 129m), Observation(Eleven, 131m, 131m));

        Result<EvaluationResult> result = Evaluate(input);

        TriggeredCondition match = result.Value.Matches[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
            Assert.That(match.ZoneId.Value, Is.EqualTo("resistance-a"));
            Assert.That(match.Condition, Is.EqualTo(ChartConditionType.Breakout));
            Assert.That(match.PreviousPrice, Is.EqualTo(129m));
            Assert.That(match.CurrentPrice, Is.EqualTo(131m));
        });
    }

    [Test]
    public void Evaluate_WithPreviousPriceAtUpperBoundary_ReturnsTriggeredBreakoutMatch()
    {
        ChartAnalysisDefinition definition = Definition([], [ResistanceZone("resistance-a", 120m, 125m, 130m)]);
        EvaluationInput input = Input(definition, Observation(Ten, 130m, 130m), Observation(Eleven, 131m, 131m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.Breakout));
    }

    [Test]
    public void Evaluate_WithBreakoutCurrentPriceAtUpperBoundary_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([], [ResistanceZone("resistance-a", 120m, 125m, 130m)]);
        EvaluationInput input = Input(definition, Observation(Ten, 129m, 129m), Observation(Eleven, 130m, 130m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithPriceRemainingAboveResistance_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([], [ResistanceZone("resistance-a", 120m, 125m, 130m)]);
        EvaluationInput input = Input(definition, Observation(Ten, 131m, 131m), Observation(Eleven, 132m, 132m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithCurrentPriceFractionallyAboveUpper_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 105.00000001m, 105.00000001m));

        Result<EvaluationResult> result = Evaluate(input);

        AssertNoAction(result, EvaluationReason.NoConditionTriggered);
    }

    [Test]
    public void Evaluate_WithCurrentPriceFractionallyBelowLower_ReturnsTriggeredSupportLossMatch()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 95.5m, 95.5m), Observation(Eleven, 94.99999999m, 94.99999999m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.SupportLoss));
    }

    [Test]
    public void Evaluate_WithoutPreviousObservation_ReturnsNoActionFirstObservation()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, null, Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(result.Value.Reason, Is.EqualTo(EvaluationReason.FirstObservation));
            Assert.That(result.Value.Matches, Is.Empty);
            Assert.That(result.Value.ObservedAt, Is.EqualTo(Eleven));
            Assert.That(result.Value.PreviousObservedAt, Is.Null);
        });
    }

    [Test]
    public void Evaluate_WithEmptyDefinitionAndObservations_ReturnsNoActionNoConfiguredZones()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(4, [], []).Value;
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 107m), Observation(Eleven, 104m, 105m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(result.Value.Reason, Is.EqualTo(EvaluationReason.NoConfiguredZones));
            Assert.That(result.Value.Matches, Is.Empty);
            Assert.That(result.Value.MonitoringRuleRevisionId, Is.EqualTo(RevisionId));
            Assert.That(result.Value.ObservedAt, Is.EqualTo(Eleven));
            Assert.That(result.Value.PreviousObservedAt, Is.EqualTo(Ten));
            Assert.That(result.Value.PriceBasis, Is.EqualTo(PriceBasis.Bid));
            Assert.That(result.Value.QuoteCurrency, Is.EqualTo("USD"));
            Assert.That(result.Value.CurrentBid, Is.EqualTo(104m));
            Assert.That(result.Value.CurrentAsk, Is.EqualTo(105m));
        });
    }

    [Test]
    public void Evaluate_WithEmptyDefinitionAndNoPreviousObservation_ReturnsNoConfiguredZones()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(4, [], []).Value;
        EvaluationInput input = Input(definition, null, Observation(Eleven, 104m, 105m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(result.Value.Reason, Is.EqualTo(EvaluationReason.NoConfiguredZones));
            Assert.That(result.Value.Matches, Is.Empty);
            Assert.That(result.Value.PreviousObservedAt, Is.Null);
        });
    }

    [Test]
    public void Evaluate_WithPriceCrossingTwoZones_ReturnsBothMatchesOrderedByLower()
    {
        ChartAnalysisDefinition definition = Definition(
            [SupportZone("support-a", 95m, 100m, 105m), SupportZone("support-b", 60m, 70m, 80m)],
            []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 50m, 50m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Matches, Has.Count.EqualTo(2));
            Assert.That(result.Value.Matches[0].ZoneId.Value, Is.EqualTo("support-b"));
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.SupportLoss));
            Assert.That(result.Value.Matches[1].ZoneId.Value, Is.EqualTo("support-a"));
            Assert.That(result.Value.Matches[1].Condition, Is.EqualTo(ChartConditionType.SupportLoss));
        });
    }

    [Test]
    public void Evaluate_WithPriceEnteringOneOfTwoZones_ReturnsSingleMatch()
    {
        ChartAnalysisDefinition definition = Definition(
            [SupportZone("support-a", 95m, 100m, 105m)],
            [ResistanceZone("resistance-a", 120m, 125m, 130m)]);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Value.Matches[0].ZoneId.Value, Is.EqualTo("support-a"));
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
        });
    }

    [Test]
    public void Evaluate_WithIdenticalInputs_ProducesIdenticalOutputs()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, 104m, 104m));

        EvaluationResult first = Evaluate(input).Value;
        EvaluationResult second = Evaluate(input).Value;

        Assert.Multiple(() =>
        {
            Assert.That(second.MonitoringRuleRevisionId, Is.EqualTo(first.MonitoringRuleRevisionId));
            Assert.That(second.ObservedAt, Is.EqualTo(first.ObservedAt));
            Assert.That(second.PreviousObservedAt, Is.EqualTo(first.PreviousObservedAt));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(second.Reason, Is.EqualTo(first.Reason));
            Assert.That(second.PriceBasis, Is.EqualTo(first.PriceBasis));
            Assert.That(second.QuoteCurrency, Is.EqualTo(first.QuoteCurrency));
            Assert.That(second.Matches, Is.EqualTo(first.Matches));
        });
    }

    [Test]
    public void Evaluate_WithPreviousAtSameInstant_ReturnsObservationsOutOfOrderError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Eleven, 106m, 106m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.ObservationsOutOfOrder));
    }

    [Test]
    public void Evaluate_WithPreviousAfterCurrent_ReturnsObservationsOutOfOrderError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Twelve, 106m, 106m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.ObservationsOutOfOrder));
    }

    [Test]
    public void Evaluate_WithDifferentQuoteCurrencies_ReturnsQuoteCurrencyMismatchError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(
            definition,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, 104m, "GBX"));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.QuoteCurrencyMismatch));
    }

    [Test]
    public void Evaluate_WithLongElapsedGapBetweenObservations_StillEvaluatesTransition()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        Instant later = Instant.FromUtc(2026, 11, 4, 10, 0);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(later, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
    }

    [Test]
    public void Evaluate_WithRevisionChangedSincePreviousObservation_EvaluatesTransitionAgainstSuppliedRevision()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(
            definition,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, 104m),
            revisionId: NextRevisionId);

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.MonitoringRuleRevisionId, Is.EqualTo(NextRevisionId));
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
        });
    }

    [Test]
    public void Evaluate_WithCurrentMissingBasisSide_ReturnsQuoteSideMissingError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 106m), Observation(Eleven, null, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_evaluation.quote_side_missing"));
    }

    [Test]
    public void Evaluate_WithPreviousMissingBasisSide_ReturnsQuoteSideMissingError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, null, 106m), Observation(Eleven, 104m, 104m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_evaluation.quote_side_missing"));
    }

    [Test]
    public void Evaluate_WithCurrentMissingAskSide_ReturnsQuoteSideMissingError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(
            definition,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, null),
            PriceBasis.Ask);

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_evaluation.quote_side_missing"));
    }

    [Test]
    public void Evaluate_WithOppositeSideMissing_EvaluatesOnSelectedBasis()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, null), Observation(Eleven, 104m, null));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(result.Value.CurrentBid, Is.EqualTo(104m));
            Assert.That(result.Value.CurrentAsk, Is.Null);
        });
    }

    [Test]
    public void Evaluate_WithAskBasis_EvaluatesAskPricesOnly()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(
            definition,
            Observation(Ten, 99m, 106m),
            Observation(Eleven, 99m, 104m),
            PriceBasis.Ask);

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.PriceBasis, Is.EqualTo(PriceBasis.Ask));
            Assert.That(result.Value.Matches[0].Condition, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(result.Value.Matches[0].PreviousPrice, Is.EqualTo(106m));
            Assert.That(result.Value.Matches[0].CurrentPrice, Is.EqualTo(104m));
        });
    }

    [Test]
    public void Evaluate_WithEmptyRevisionId_ReturnsRevisionIdRequiredError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = new(
            Guid.Empty,
            definition,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, 104m),
            PriceBasis.Bid);

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.RevisionIdRequired));
    }

    [Test]
    public void Evaluate_WithUndefinedPriceBasis_ReturnsPriceBasisUndefinedError()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(
            definition,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, 104m),
            (PriceBasis)99);

        Result<EvaluationResult> result = Evaluate(input);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.PriceBasisUndefined));
    }

    [Test]
    public void Evaluate_WithNullInput_ThrowsArgumentNullException()
    {
        Assert.That(() => Evaluate(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Evaluate_WithNullDefinition_ThrowsArgumentNullException()
    {
        EvaluationInput input = new(
            RevisionId,
            null!,
            Observation(Ten, 106m, 106m),
            Observation(Eleven, 104m, 104m),
            PriceBasis.Bid);

        Assert.That(() => Evaluate(input), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Evaluate_WithNullCurrent_ThrowsArgumentNullException()
    {
        EvaluationInput input = new(
            RevisionId,
            Definition([SupportZone("support-a", 95m, 100m, 105m)], []),
            Observation(Ten, 106m, 106m),
            null!,
            PriceBasis.Bid);

        Assert.That(() => Evaluate(input), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Evaluate_WithTriggeredResult_CarriesEvaluationEvidence()
    {
        ChartAnalysisDefinition definition = Definition([SupportZone("support-a", 95m, 100m, 105m)], []);
        EvaluationInput input = Input(definition, Observation(Ten, 106m, 107m), Observation(Eleven, 104m, 105m));

        Result<EvaluationResult> result = Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.MonitoringRuleRevisionId, Is.EqualTo(RevisionId));
            Assert.That(result.Value.ObservedAt, Is.EqualTo(Eleven));
            Assert.That(result.Value.PreviousObservedAt, Is.EqualTo(Ten));
            Assert.That(result.Value.PriceBasis, Is.EqualTo(PriceBasis.Bid));
            Assert.That(result.Value.QuoteCurrency, Is.EqualTo("USD"));
            Assert.That(result.Value.CurrentBid, Is.EqualTo(104m));
            Assert.That(result.Value.CurrentAsk, Is.EqualTo(105m));
        });
    }

    private static void AssertNoAction(Result<EvaluationResult> result, EvaluationReason reason)
    {
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
            Assert.That(result.Value.Reason, Is.EqualTo(reason));
            Assert.That(result.Value.Matches, Is.Empty);
        });
    }

    private static Result<EvaluationResult> Evaluate(EvaluationInput input)
    {
        return new ChartAnalysisEvaluator().Evaluate(input);
    }

    private static EvaluationInput Input(
        ChartAnalysisDefinition definition,
        PriceObservation? previous,
        PriceObservation current,
        PriceBasis basis = PriceBasis.Bid,
        Guid? revisionId = null)
    {
        return new EvaluationInput(revisionId ?? RevisionId, definition, previous, current, basis);
    }

    private static PriceObservation Observation(
        Instant observedAt,
        decimal? bid,
        decimal? ask,
        string quoteCurrency = "USD")
    {
        return PriceObservation.Create(observedAt, bid, ask, quoteCurrency).Value;
    }

    private static ChartAnalysisDefinition Definition(
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        return ChartAnalysisDefinition.Create(4, supportZones, resistanceZones).Value;
    }

    private static ChartZone SupportZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;
    }

    private static ChartZone ResistanceZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal").Value).Value]).Value;
    }
}
