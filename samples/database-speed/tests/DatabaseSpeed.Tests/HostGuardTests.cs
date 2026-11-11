using System.Runtime.InteropServices;
using FluentAssertions;

namespace DatabaseSpeed.Tests;

[TestClass]
public class HostGuardTests
{
    [TestMethod]
    public void A_full_run_on_x86_64_proceeds()
    {
        var decision = HostGuard.Evaluate(Architecture.X64, smoke: false);

        decision.Proceed.Should().BeTrue();
        decision.ExitCode.Should().Be(0);
        decision.Message.Should().BeNull();
    }

    [DataTestMethod]
    [DataRow(Architecture.Arm64)]
    [DataRow(Architecture.Arm)]
    [DataRow(Architecture.X86)]
    public void A_full_run_on_any_other_architecture_exits_2_and_says_to_run_it_in_CI(Architecture architecture)
    {
        var decision = HostGuard.Evaluate(architecture, smoke: false);

        decision.Proceed.Should().BeFalse();
        decision.ExitCode.Should().Be(2);
        decision.Message.Should().Contain("x86-64").And.Contain("CI").And.Contain("emulat").And.Contain("--smoke");
        decision.Message.Should().Contain(architecture.ToString());
    }

    [TestMethod]
    public void A_smoke_run_on_Apple_Silicon_proceeds_with_a_warning()
    {
        var decision = HostGuard.Evaluate(Architecture.Arm64, smoke: true);

        decision.Proceed.Should().BeTrue();
        decision.ExitCode.Should().Be(0);
        decision.Message.Should().Contain("NOT VALID FOR COMPARISON");
    }

    [TestMethod]
    public void A_smoke_run_on_x86_64_is_still_labelled_not_valid()
    {
        var decision = HostGuard.Evaluate(Architecture.X64, smoke: true);

        decision.Proceed.Should().BeTrue();
        decision.Message.Should().Contain("NOT VALID FOR COMPARISON");
    }
}
