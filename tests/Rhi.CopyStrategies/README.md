# Bounded copy/flush diagnostic

This standalone experiment builds **only this console project** and links Core's
real `CopyBuffer.cs`. It does not build or test Core, WinUI, or the application.
`NativeFiles.cs` is a small benchmark-only copy of the native open, single-link
check, and no-overwrite handle rename bindings used at Core revision `ec36f4e`.
It is not a replacement deployment implementation.

## Fixed scope and isolation

- Run only on a disposable **GitHub-hosted Windows** runner. Other environments,
  arguments, and configurable file paths are rejected.
- Every source and destination is generated under a unique temporary directory;
  there are no game, library, user-document, credential, or repository writes.
- Six strategies receive a 32-file warmup each, followed by three paired rounds
  per comparison. Every measured member has **330 files of 128 KiB**, not the
  original performance suite's 990 files. Every comparison has two adjacent
  members in each round: 30 measured trials, 9,900 measured file copies
  (1,297,612,800 logical bytes), plus 192 warmup copies.
- The smaller fixed workload bounds six variants and repeated current-strategy
  partners. It must **not** be compared as a total against the original 990-file
  suite or extrapolated to 990 files/the user's PC.
- The six-minute soft budget is checked between files and verification reads.
  Synchronous OS calls cannot be interrupted safely. The workflow's ten-minute
  hard timeout is the final bound. Incomplete output is a failure, not a result.
- The workflow only triggers from pushes to `maintenance/copy-strategies/**`
  changing this directory or its workflow. It has read-only repository access,
  no artifact/release upload, no dispatch/PR trigger, and does not modify the
  existing development workflows. Publishing the microbranch, rather than a
  development branch or PR, avoids triggering the existing full Windows CI.

## Strategies

All hashed strategies use the same source bytes, single-pass pooled copy/hash
loop, `CREATE_NEW` destination access/share flags, identity check, pre-rename
`Flush(false)`, and same-volume exact-handle no-overwrite rename. The copied
source is opened read-only, denying concurrent writers. Content is independently
checked outside timing for **every** strategy, including the weaker reference.

1. `buffered-current`: buffered native handle, then post-rename `Flush(true)` and
   close per file. This models the measured current copy/publication slice.
2. `write-through-only`: new handle has `FILE_FLAG_WRITE_THROUGH`; no explicit
   post-rename disk flush. **Weaker diagnostic only, never a publication
   candidate.** Write-through/FUA is not assumed equivalent to FlushFileBuffers
   on all hardware or for post-rename metadata durability.
3. `write-through-plus-flush`: write-through handle plus the same explicit
   post-rename `Flush(true)`, isolating write-through's additional cost.
4. `buffered-batch32-flush1`: copy/hash/pre-flush/rename each file, retain at most
   32 exact destination handles, flush every handle with maximum parallelism 1,
   wait for all calls, close all handles, then mark the batch complete.
5. `buffered-batch32-flush4`: identical to the preceding strategy except maximum
   parallelism is 4. It retains the same number of successful `Flush(true)` calls
   and the same all-flushes-before-batch-success barrier. Actual concurrent calls
   and peak owned staging handles are recorded, rather than assuming four calls
   really overlap. This does **not** prove that delaying individual boundaries is
   safe to integrate into the application's journal/rollback/recovery protocol.
6. `native-copy-weaker-reference`: `File.Copy` to staging, open that existing file
   with the current read/delete `WRITE_THROUGH` flags, then exact-handle rename.
   **Weaker reference only:** no timed source hash, no explicit disk flush, and
   opening an already-copied file write-through does not retroactively establish
   write-through semantics for its earlier writes.

The five paired comparisons are 1/2, 1/3, 1/4, **4/5**, and 1/6. Pairing 4 and 5
directly isolates flush parallelism on adjacent same-source trials. Comparing
4 against 1 also changes the timing of the per-file barrier, so that comparison
alone cannot authorize an application change.

## Measurements and checks

The process prints newline-delimited JSON (`plan`, `environment`, `trial-start`,
`trial`, and final `completion`) to the Actions console. There is no output file
or artifact upload. Capture the logs to retain data outside the runner.

Each trial reports wall time plus open, copy/hash (or native copy), managed
pre-flush, rename, disk-flush wall time, and close time. Disk-flush call-time sum
is also reported separately: concurrent call times overlap and must **not** be
added to the wall-time phases. Unattributed wall time makes instrumentation,
loop, scheduling, counters, and other overhead visible. Copy/hash is one combined
phase because it is intentionally a single streaming pass. Source open/close
is inside that combined phase; the separate close phase is for staging handles.
Concurrent flush counts describe overlapping C# invocations, including their
small instrumentation windows, not proven physical-disk parallelism.

Every trial verifies copied and source SHA-256 values, lengths, exact entry
count, no staging names left, closed owned handles, flush count, concurrency and
handle bounds, and cleanup. All verification and cleanup are outside the timed
phase. Failure emits the error, exits nonzero, and suppresses successful aggregate
results. A hard-killed process lacking a successful final `completion` is not a
pass. Flush errors are neither suppressed nor retried.

Each round reuses identical deterministic source bytes for its paired members,
but each member has its own empty destination. Each comparison's baseline runs
first in rounds 1/3 and second in round 2; comparison order rotates across rounds. All three raw
pairs, paired ratios, and medians are retained. Warmups are excluded from
aggregation. No outlier is discarded, slowdown hidden, or speed target imposed.

OS, runtime, CPU count, runner image, filesystem and drive type are reported.
Antivirus and physical storage details remain **unknown**; security settings
are not queried or changed. Stream-position byte counts are logical counts,
not physical I/O measurements; native `File.Copy` internals are unobserved.

This is a warm-cache microbenchmark affected by runner load, filesystem and
storage behavior, antivirus, JIT/tiering, and measurement overhead. Three pairs
are descriptive, not statistical proof. Content verification is not a
power-loss durability test. No result automatically recommends a production
change or replaces the full safety/regression suite.

## Execution gate

The harness is prepared for semantic review before its first execution. Do not
push the triggering maintenance branch until that review and the parent's
execution release are complete. No Core, benchmark-suite, or UI source file is
changed by this experiment.
