#!/usr/bin/env python3
"""Run: python3 .github/scripts/test_flaky_retry.py"""
import os, subprocess, sys, tempfile, unittest

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "flaky_retry.py")
K = "SendMessageStream_Cancel_during_thinking"
FLAKY = K + "_x"


def trx(results, **over):
    n = lambda o: sum(1 for _, x in results if x == o)
    c = dict(executed=sum(1 for _, o in results if o != "NotExecuted"), passed=n("Passed"), failed=n("Failed"),
             error=n("Error"), timeout=n("Timeout"), aborted=n("Aborted"), inconclusive=0, notExecuted=n("NotExecuted"))
    c.update(over)
    cs = " ".join(f'{k}="{v}"' for k, v in c.items())
    rs = "".join(f'<UnitTestResult testName="{t}" outcome="{o}"/>' for t, o in results)
    return f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{rs}</Results><ResultSummary><Counters {cs}/></ResultSummary></TestRun>'


def run(mode, content, *args):
    with tempfile.TemporaryDirectory() as d:
        if content is not None:
            with open(d + "/a.trx", "w") as f:
                f.write(content)
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
