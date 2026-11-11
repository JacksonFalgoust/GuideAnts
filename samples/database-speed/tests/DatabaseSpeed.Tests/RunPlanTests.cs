using FluentAssertions;

namespace DatabaseSpeed.Tests;

[TestClass]
public class RunPlanTests
{
    [TestMethod]
    public void The_full_plan_uses_the_spec_volumes_and_run_counts()
    {
        var plan = RunPlan.For(smoke: false);

        plan.Smoke.Should().BeFalse();
        plan.Volumes.Projects.Should().Be(50);
        plan.Volumes.NotebooksPerProject.Should().Be(10);
        plan.Volumes.ConversationsPerNotebook.Should().Be(20);
        plan.Volumes.MessagesPerConversation.Should().Be(50);
        plan.Volumes.UsageEvents.Should().Be(10_000);
        plan.Volumes.Chunks.Should().Be(20_000);
        plan.Volumes.LongConversationMessages.Should().Be(200);
        plan.Volumes.JobsPerDrain.Should().Be(1_000);
        plan.Volumes.JobWorkers.Should().Be(8);
        plan.WarmupRuns.Should().Be(20);
        plan.MeasuredRuns.Should().Be(200);
        plan.ColdStartWarmupRuns.Should().Be(1);
        plan.ColdStartMeasuredRuns.Should().Be(10);
        plan.ResultsFileName.Should().Be("RESULTS.md");
    }

    [TestMethod]
    public void The_smoke_plan_is_tiny_and_writes_a_separate_file()
    {
        var plan = RunPlan.For(smoke: true);

        plan.Smoke.Should().BeTrue();
        plan.Volumes.Projects.Should().Be(2);
        plan.Volumes.NotebooksPerProject.Should().Be(2);
        plan.Volumes.ConversationsPerNotebook.Should().Be(2);
        plan.Volumes.MessagesPerConversation.Should().Be(10);
        plan.Volumes.UsageEvents.Should().Be(200);
        plan.Volumes.Chunks.Should().Be(500);
        plan.WarmupRuns.Should().Be(3);
        plan.MeasuredRuns.Should().Be(10);
        plan.ColdStartWarmupRuns.Should().Be(0);
        plan.ColdStartMeasuredRuns.Should().Be(1);
        plan.ResultsFileName.Should().Be("RESULTS-smoke.md");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Message_counts_are_whole_turns(bool smoke)
    {
        var volumes = RunPlan.For(smoke).Volumes;

        // Every turn holds one user and one assistant message.
        (volumes.MessagesPerConversation % 2).Should().Be(0);
        (volumes.LongConversationMessages % 2).Should().Be(0);
        volumes.TotalMessages.Should().Be(volumes.Projects * volumes.NotebooksPerProject * volumes.ConversationsPerNotebook * volumes.MessagesPerConversation);
    }

    [TestMethod]
    public void The_smoke_plan_never_writes_the_committed_results_file()
    {
        RunPlan.For(smoke: true).ResultsFileName.Should().NotBe(RunPlan.For(smoke: false).ResultsFileName);
    }
}
