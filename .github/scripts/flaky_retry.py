#!/usr/bin/env python3
"""TRX helpers for the known-flaky integration-test retry in client-test.yml.

  flaky_retry.py classify <trx-dir> <known-flaky-name-prefix>...
      Exit 0 and print a `dotnet test --filter` (exact Name= equality) only if
      every non-passing result is a known-flaky test and the per-test list agrees
      with the TRX counters. Exit 1 otherwise (including no TRX / no failures).
  flaky_retry.py verify <trx-dir>
      Exit 0 only if at least one test executed and all of them passed.

See docs/postgres-parity-report.md rows 53/54.
"""
import glob
import sys
import xml.etree.ElementTree as ET

BENIGN = {"Passed", "NotExecuted"}  # NotExecuted = skipped / Assert.Inconclusive / [Ignore]
BAD_COUNTERS = ("failed", "error", "timeout", "aborted", "inconclusive",
                "passedButRunAborted", "notRunnable", "disconnected",
                "warning", "inProgress", "pending")


def load(d):
    results, counters, files = [], {k: 0 for k in BAD_COUNTERS + ("executed", "passed")}, glob.glob(d + "/*.trx")
    for f in files:
        root = ET.parse(f).getroot()
        for e in root.iter():
            tag = e.tag.rsplit("}", 1)[-1]
            if tag == "UnitTestResult":
                results.append((e.get("testName"), e.get("outcome")))
            elif tag == "Counters":
                for k in counters:
                    counters[k] += int(e.get(k, 0))
    return files, results, counters


def escape(n):
    for ch in "\\()&|=!~":
        n = n.replace(ch, "\\" + ch)
    return n


def classify(d, known):
    files, results, c = load(d)
    if not files:
        print("no TRX found", file=sys.stderr); return 1
    bad = [(n, o) for n, o in results if o not in BENIGN]
    if not bad:
        print("no non-passing results listed; not a flaky-only failure", file=sys.stderr); return 1
    if len(bad) != sum(c[k] for k in BAD_COUNTERS):
        print(f"listed non-passing ({len(bad)}) != TRX counters ({sum(c[k] for k in BAD_COUNTERS)})", file=sys.stderr); return 1
    other = [(n, o) for n, o in bad if o != "Failed" or not any(n.startswith(k) for k in known)]
    if other:
        print(f"non-flaky or non-Failed results: {other}", file=sys.stderr); return 1
    print("|".join("Name=" + escape(n) for n in sorted({n for n, _ in bad})))
    return 0


def verify(d):
    files, results, c = load(d)
    if not files:
        print("no retry TRX found", file=sys.stderr); return 1
    bad = [r for r in results if r[1] not in BENIGN]
    if c["executed"] < 1 or c["passed"] != c["executed"] or bad or any(c[k] for k in BAD_COUNTERS):
        print(f"retry not clean: counters={c} bad={bad}", file=sys.stderr); return 1
    return 0


if __name__ == "__main__":
    mode, d, rest = sys.argv[1], sys.argv[2], sys.argv[3:]
    sys.exit(classify(d, rest) if mode == "classify" else verify(d))
