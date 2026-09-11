using CrashScope.Agent.Sessions;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class AutomaticWorkloadPolicyTests
{
    [Fact]
    public void IsEligible_AllowsStrongRecommendedGame()
    {
        var candidate = Candidate(42, Utc(8, 0), WorkloadKind.Game, WorkloadRecommendation.Recommended, 95);

        Assert.True(AutomaticWorkloadPolicy.IsEligible(candidate));
    }

    [Theory]
    [InlineData(WorkloadKind.Game, WorkloadRecommendation.Recommended, 89)]
    [InlineData(WorkloadKind.Game, WorkloadRecommendation.Possible, 95)]
    [InlineData(WorkloadKind.GeneralApplication, WorkloadRecommendation.Recommended, 95)]
    [InlineData(WorkloadKind.HelperOrSystem, WorkloadRecommendation.Recommended, 95)]
    public void IsEligible_RejectsWeakOrAmbiguousCandidates(
        WorkloadKind kind,
        WorkloadRecommendation recommendation,
        int score)
    {
        var candidate = Candidate(42, Utc(8, 0), kind, recommendation, score);

        Assert.False(AutomaticWorkloadPolicy.IsEligible(candidate));
    }

    [Fact]
    public void CandidateGate_RequiresTwoMatchingIdentityObservations()
    {
        var gate = new AutomaticCandidateGate();
        var candidate = Candidate(42, Utc(8, 0), WorkloadKind.Game, WorkloadRecommendation.Recommended, 95);

        Assert.False(gate.Observe(candidate));
        Assert.Equal(1, gate.ConfirmationCount);
        Assert.True(gate.Observe(candidate));
        Assert.Equal(2, gate.ConfirmationCount);
    }

    [Fact]
    public void CandidateGate_ProcessStartChangeRestartsConfirmation()
    {
        var gate = new AutomaticCandidateGate();
        var original = Candidate(42, Utc(8, 0), WorkloadKind.Game, WorkloadRecommendation.Recommended, 95);
        var reusedPid = Candidate(42, Utc(8, 1), WorkloadKind.Game, WorkloadRecommendation.Recommended, 95);

        Assert.False(gate.Observe(original));
        Assert.False(gate.Observe(reusedPid));
        Assert.Equal(1, gate.ConfirmationCount);
        Assert.Equal(Utc(8, 1), gate.Candidate!.ProcessStartTimeUtc);
    }

    [Fact]
    public void CandidateGate_ResetClearsCandidateAndCount()
    {
        var gate = new AutomaticCandidateGate();
        gate.Observe(Candidate(42, Utc(8, 0), WorkloadKind.Game, WorkloadRecommendation.Recommended, 95));

        gate.Reset();

        Assert.Null(gate.Candidate);
        Assert.Equal(0, gate.ConfirmationCount);
    }

    private static WorkloadCandidate Candidate(
        int processId,
        DateTimeOffset start,
        WorkloadKind kind,
        WorkloadRecommendation recommendation,
        int score) =>
        new(
            processId,
            start,
            "test-game",
            @"C:\Games\test-game.exe",
            "Test Game",
            2048,
            kind,
            recommendation,
            score,
            new[] { "Test reason." });

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);
}
