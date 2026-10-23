# Paired deployment measurements

This page describes the historical f26 comparison. The separately opted-in
delivered-baseline backup/small-update experiment is documented in [BACKUP.md](BACKUP.md).
It does not change this workflow or its expected baseline failures.

Run only in the disposable Windows GitHub Actions job. `tools/ci_performance.ps1`
builds this exact harness twice, against pinned baseline Core and candidate Core.
It does not build the UI or modify baseline production code. Only the seven new
integrity regression test cases are copied into baseline: all seven must fail
there and pass on candidate before performance measurements can start. TRX files
and assertion messages are retained; missing tests or compilation failures do
not count as a successful red/green gate.

Two persistent .NET workers each perform a 32-file warmup, then three measured
990-file trials in paired B/C, C/B, B/C order. Each file is 128 KiB; the unrelated
executable is 64 MiB. Fresh deterministic sources are generated per trial, with
independent target/workspace directories for fresh deploy, overwrite of existing
unmanaged files, same-version no-op Change, and clean Clear. No-op and Clear have
untimed setup deployments. Normal system File.Copy and File.Delete references
run on another directory. The reported scenario total is preflight plus execute
for each trial, followed by the median, not a median of already aggregated stages.

All source/target/backup bytes, receipt hashes, executable, unrelated sentinel,
temporary-file absence and clean-clear results are checked outside the timed
phases using independent SHA-256 code. Temporary fixtures are deleted after each
trial. All timings, warmups, OS I/O counters, endpoint process handle counts,
process CPU/managed allocations and before/after JSON file lengths remain in the
self-contained `artifacts/performance/current.json` used by the existing draft
Release uploader. Raw trial files, TRX and logs remain on the disposable runner;
the uploaded report contains the trial data and regression assertion results.
No GitHub Actions artifact upload is attempted.

Important interpretation limits:

- Fixture generation, setup and verification warm filesystem caches. This is not
  a cold-cache benchmark, and no privileged cache flushing is attempted.
- File.Copy/File.Delete do not offer equivalent transactional, journal, backup
  or explicit flush guarantees. Their ratios are context, not an equal-safety test.
- GetProcessIoCounters measures process-wide logical I/O, not physical disk bytes
  or exact copy/hash/open/JSON-write counts. Before/after JSON lengths cannot count
  repeated writes. Endpoint handles are not a peak or proof of low transient use.
- Antivirus, storage, runner load and JIT/tiering still affect samples. Three
  trials and medians are descriptive, not evidence of statistical significance.
- Slower candidate medians are retained and labelled slower. There is no absolute
  speed target, suppressed outlier, best-run selection or claim about the user's PC.
- This suite measures clean synthetic operations, not the complete crash/safety
  test matrix. The ordinary Windows workflow remains responsible for the full suite.

`Report.Tests.ps1` checks aggregation, warmup exclusion, visible regressions,
incomplete/failed-series rejection and per-trial totals without launching a worker.
