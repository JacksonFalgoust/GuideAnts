using FluentAssertions;
using GuideAntsApi.BackgroundJobs;

namespace GuideAntsApi.Tests.BackgroundJobs;

[TestClass]
public sealed class JobQueueClaimSqlCharacterizationTests
{
    [TestMethod]
    public void TryClaimSql_RechecksPendingStatusAndEmptyClaimTokenOnOuterUpdate()
    {
        var claimSql = JobQueueClaimSql.Claim;

        claimSql.Should().Contain("WITH Candidate AS");
        claimSql.Should().Contain("TOP (1)");
        claimSql.Should().Contain("WHERE j.Status = {0}");
        claimSql.Should().Contain("AND j.ClaimToken = {2}");
        claimSql.Should().MatchRegex("UPDATE j[\\s\\S]*WHERE j\\.Status = \\{0\\}[\\s\\S]*AND j\\.ClaimToken = \\{2\\}");
    }

    [TestMethod]
    public void TryClaimSql_OnPostgres_RechecksPendingStatusAndEmptyClaimTokenOnOuterUpdate()
    {
        var claimSql = JobQueueClaimSql.ClaimPostgres;

        // Same placeholders and order as the SQL Server claim; no SKIP LOCKED, so a claimer that loses the race for
        // the candidate row updates nothing (as on SQL Server) instead of moving on to the next row.
        claimSql.Should().NotContainEquivalentOf("SKIP LOCKED");
        claimSql.Should().Contain("ORDER BY c.\"Priority\" DESC, c.\"Created\" ASC");
        claimSql.Should().Contain("LIMIT 1");
        claimSql.Should().Contain("(CAST({3} AS text) IS NULL OR c.\"JobType\" = CAST({3} AS text))");
        claimSql.Should().MatchRegex("UPDATE \"JobQueue\" AS j[\\s\\S]*WHERE j\\.\"Id\" = \\([\\s\\S]*\\)\\s*AND j\\.\"Status\" = \\{0\\} AND j\\.\"ClaimToken\" = \\{2\\}\\s*$");
        for (var placeholder = 0; placeholder <= 6; placeholder++)
        {
            claimSql.Should().Contain($"{{{placeholder}}}");
        }
    }

    [TestMethod]
    public void LeaseOwnershipLost_DoesNotBurnAttemptBudget()
    {
        JobRetryPolicy.BurnsAttemptBudget(JobFailureClass.LeaseOwnershipLost).Should().BeFalse();
    }

    [TestMethod]
    public void PlanFailure_LeaseOwnershipLost_RemainsRetryable()
    {
        var policy = new JobRetryPolicy(new JobRetryOptions());
        var created = DateTime.UtcNow.AddHours(-1);
        var now = DateTime.UtcNow;

        var plan = policy.PlanFailure(
            JobFailureClass.LeaseOwnershipLost,
            currentAttempts: 3,
            maxAttempts: 40,
            jobCreatedUtc: created,
            nowUtc: now);

        plan.WillRetry.Should().BeTrue();
    }
}
