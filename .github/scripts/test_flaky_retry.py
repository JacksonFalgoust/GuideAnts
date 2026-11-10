#!/usr/bin/env python3
"""Run: python3 .github/scripts/test_flaky_retry.py"""
import os, subprocess, sys, tempfile, unittest

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "flaky_retry.py")
K = "SendMessageStream_Cancel_during_thinking_persists_thinking_without_zero_token_usage"
FLAKY = K


from xml.sax.saxutils import quoteattr


def escape_xml(t):
    return quoteattr(t)[1:-1]


def trx(results, runinfo=(), **over):
    n = lambda o: sum(1 for _, x in results if x == o)
    c = dict(total=len(results), executed=sum(1 for _, o in results if o != "NotExecuted"), passed=n("Passed"), failed=n("Failed"),
             error=n("Error"), timeout=n("Timeout"), aborted=n("Aborted"), inconclusive=0, notExecuted=n("NotExecuted"))
    c.update(over)
    cs = " ".join(f'{k}="{v}"' for k, v in c.items())
    rs = "".join(f'<UnitTestResult testName="{escape_xml(t)}" outcome="{o}"/>' for t, o in results)
    ri = "".join(f'<RunInfo outcome="{l}" computerName="x"><Text>boom</Text></RunInfo>' for l in runinfo)
    return f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{rs}</Results><ResultSummary><Counters {cs}/><RunInfos>{ri}</RunInfos></ResultSummary></TestRun>'


def run(mode, content, *args):
    with tempfile.TemporaryDirectory() as d:
        for i, c in enumerate(content if isinstance(content, list) else [content]):
            if c is not None:
                with open(f"{d}/{i}.trx", "w") as f:
                    f.write(c)
        p = subprocess.run([sys.executable, S, mode, d, *args], capture_output=True, text=True)
        return p.returncode, p.stdout.strip()


class T(unittest.TestCase):
    def test_only_flaky(self):
        rc, out = run("classify", trx([(FLAKY, "Failed"), ("Ok", "Passed")]), K)
        self.assertEqual((rc, out), (0, "Name=" + FLAKY))
    def test_flaky_plus_failed(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("Real", "Failed")]), K)[0], 1)
    def test_flaky_plus_timeout(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("Real", "Timeout")]), K)[0], 1)
    def test_flaky_plus_error(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("Real", "Error")]), K)[0], 1)
    def test_flaky_plus_aborted(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("Real", "Aborted")]), K)[0], 1)
    def test_counters_exceed_listed(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed")], failed=2), K)[0], 1)
    def test_no_trx(self):
        self.assertEqual(run("classify", None, K)[0], 1)
    def test_zero_failed(self):
        self.assertEqual(run("classify", trx([("Ok", "Passed")]), K)[0], 1)
    def test_not_executed_allowed(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("Skip", "NotExecuted")]), K)[0], 0)
    def test_filter_escapes(self):
        rc, out = run("classify", trx([(K + "(a=1)", "Failed")]), K)
        self.assertEqual(out, "Name=" + K + "\\(a\\=1\\)")
    def test_verify_zero_executed(self):
        self.assertEqual(run("verify", trx([("Skip", "NotExecuted")]))[0], 1)
    def test_verify_failed(self):
        self.assertEqual(run("verify", trx([(FLAKY, "Failed")]))[0], 1)
    def test_verify_no_trx(self):
        self.assertEqual(run("verify", None)[0], 1)
    def test_verify_pass(self):
        self.assertEqual(run("verify", trx([(FLAKY, "Passed")]))[0], 0)

    def test_truncated_run(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed")], total=50), K)[0], 1)
    def test_runinfo_error(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed")], runinfo=["Error"]), K)[0], 1)
    def test_runinfo_informational_ok(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed")], runinfo=["Informational"]), K)[0], 0)
    def test_inconclusive_outcome(self):
        self.assertEqual(run("classify", trx([(FLAKY, "Failed"), ("R", "Inconclusive")], inconclusive=1), K)[0], 1)
    def test_name_merely_starts_with_flaky(self):
        self.assertEqual(run("classify", trx([(K + "_other", "Failed")]), K)[0], 1)
    def test_parameterised_suffix_ok(self):
        self.assertEqual(run("classify", trx([(K + "(1)", "Failed")]), K)[0], 0)
    def test_two_trx_counters_summed(self):
        a = trx([(FLAKY, "Failed")]); b = trx([("R", "Failed")])
        self.assertEqual(run("classify", [a, b], K)[0], 1)
        self.assertEqual(run("classify", [a, trx([("Ok", "Passed")])], K)[0], 0)
    def test_verify_counter_only_failure(self):
        self.assertEqual(run("verify", trx([(FLAKY, "Passed")], failed=1))[0], 1)
    def test_verify_truncated(self):
        self.assertEqual(run("verify", trx([(FLAKY, "Passed")], total=5))[0], 1)
    def test_escape_all(self):
        rc, out = run("classify", trx([(K + "(\\&|!~)", "Failed")]), K)
        self.assertEqual(out, "Name=" + K + "\\(\\\\\\&\\|\\!\\~\\)")


if __name__ == "__main__":
    unittest.main(verbosity=2)
