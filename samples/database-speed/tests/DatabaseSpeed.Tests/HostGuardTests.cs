using System.Runtime.InteropServices;
using FluentAssertions;

namespace DatabaseSpeed.Tests;

[TestClass]
public class HostGuardTests
{
    [DataTestMethod]
    [DataRow("x86_64")]
    [DataRow("amd64")]
    [DataRow("X86_64")]
    public void A_full_run_with_an_x86_64_harness_and_daemon_proceeds(string daemon)
    {
        var decision = HostGuard.Evaluate(Architecture.X64, daemon, smoke: false);

        decision.Proceed.Should().BeTrue();
        decision.ExitCode.Should().Be(0);
        decision.Message.Should().BeNull();
        decision.SqlServerEmulated.Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow(Architecture.Arm64)]
    [DataRow(Architecture.Arm)]
    [DataRow(Architecture.X86)]
    public void A_full_run_from_any_other_harness_architecture_exits_2_and_says_to_run_it_in_CI(Architecture architecture)
    {
        var decision = HostGuard.Evaluate(architecture, "x86_64", smoke: false);

        decision.Proceed.Should().BeFalse();
        decision.ExitCode.Should().Be(2);
        decision.Message.Should().Contain("x86-64").And.Contain("CI").And.Contain("--smoke");
        decision.Message.Should().Contain(architecture.ToString());
    }

    [DataTestMethod]
    [DataRow("aarch64")]
    [DataRow("arm64")]
    public void A_full_run_against_a_non_x86_64_Docker_daemon_exits_2_because_SQL_Server_would_be_emulated(string daemon)
    {
        var decision = HostGuard.Evaluate(Architecture.X64, daemon, smoke: false);

        decision.Proceed.Should().BeFalse();
        decision.ExitCode.Should().Be(2);
        decision.Message.Should().Contain("Docker daemon").And.Contain(daemon).And.Contain("emulat").And.Contain("CI");
        decision.SqlServerEmulated.Should().BeTrue();
    }

    [TestMethod]
    public void A_full_run_exits_2_when_the_daemon_architecture_is_unknown()
    {
        var decision = HostGuard.Evaluate(Architecture.X64, null, smoke: false);

        decision.Proceed.Should().BeFalse();
        decision.ExitCode.Should().Be(2);
        decision.Message.Should().Contain("Docker daemon");
    }

    [TestMethod]
    public void A_smoke_run_on_Apple_Silicon_proceeds_and_is_labelled_emulated()
    {
        var decision = HostGuard.Evaluate(Architecture.Arm64, "aarch64", smoke: true);

        decision.Proceed.Should().BeTrue();
        decision.ExitCode.Should().Be(0);
        decision.Message.Should().Contain("NOT VALID FOR COMPARISON").And.Contain("emulated");
        decision.SqlServerEmulated.Should().BeTrue();
    }

    [TestMethod]
    public void A_smoke_run_on_x86_64_is_labelled_not_valid_but_not_emulated()
    {
        var decision = HostGuard.Evaluate(Architecture.X64, "x86_64", smoke: true);

        decision.Proceed.Should().BeTrue();
        decision.Message.Should().Contain("NOT VALID FOR COMPARISON").And.NotContain("emulated");
        decision.SqlServerEmulated.Should().BeFalse();
    }
}
