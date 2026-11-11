using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DatabaseSpeed.Tests;

[TestClass]
public class LogSinkTests
{
    [TestMethod]
    public void An_error_a_service_logs_fails_the_run()
    {
        var sink = new LogSink();
        sink.CreateLogger("GuideAntsApi.BackgroundJobs.Services.Search.PostgresHybridSearcher")
            .LogError(new InvalidOperationException("boom"), "Hybrid search failed for query: {Query}", "x");

        FluentActions.Invoking(() => sink.ThrowIfFailed("search"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Hybrid search failed*InvalidOperationException: boom*");
        FluentActions.Invoking(() => sink.ThrowIfFailed("search")).Should().NotThrow("the failures were reported once");
    }

    [TestMethod]
    public void A_failed_EF_command_is_counted_not_fatal_because_EF_may_have_retried_it()
    {
        var sink = new LogSink();
        sink.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogError("Failed executing DbCommand (7,508ms) ...");

        FluentActions.Invoking(() => sink.ThrowIfFailed("claims")).Should().NotThrow();
        sink.WarningCounts.Should().ContainSingle().Which.Value.Should().Be(1);
    }

    [TestMethod]
    public void Warnings_are_counted_by_category_and_opening_words()
    {
        var sink = new LogSink();
        var logger = sink.CreateLogger("GuideAntsApi.BackgroundJobs.JobQueueService");
        logger.LogWarning("Job claim failed with SqlException Number=1205 State=51");
        logger.LogWarning("Job claim failed with SqlException Number=1205 State=52");
        logger.LogInformation("Completed job with ID {JobId}", Guid.Empty);

        FluentActions.Invoking(() => sink.ThrowIfFailed("claims")).Should().NotThrow();
        sink.WarningCounts.Should().ContainSingle().Which.Value.Should().Be(2);
    }
}
