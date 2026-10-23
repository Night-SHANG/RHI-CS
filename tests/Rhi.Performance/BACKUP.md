# Backup capture versus a managed small update

This opt-in suite does not replace the historical f26 deployment experiment.
`tools/ci_backup_performance.ps1` compares the delivered baseline
`76efd504ed0d3b473a5d4cc281cbf7d387fe2e32` with the candidate on one disposable
GitHub-hosted Windows runner. Both revisions must **pass** the seven existing
execution-integrity cases. A missing TRX, failed build, missing case, or failed
assertion is not a valid baseline result. The new maintenance workflow also
runs the full candidate Core tests, but never builds the UI.

## Two deliberately different scenarios

- `firstUnmanagedOverwrite`: 990 deterministic 128 KiB files already exist in
  the game directory, but no receipt exists. Deploy captures 990 originals,
  installs the new files, and verifies every installed and backed-up SHA-256,
  receipt, source, unrelated executable/sentinel, and absence of temporary
  files or pending journals. An **untimed** Clear then restores all 990 original
  file hashes, checks the exact file set, and removes the receipt. Restoration
  is never included in a Capture or deploy timing.
- `managedSmallUpdate`: an untimed real deploy first installs 990 files. A
  second immutable source version changes exactly indices 0, 99, ..., 891;
  the other 980 files retain identical hashes. The timed Change must plan
  exactly ten changed and 980 unchanged files, without overwrite/protection
  consent requirements, and make **zero Capture calls**. Every final target,
  receipt/version, both source versions and unrelated file is independently
  checked. No target is edited to manufacture the update.

Each worker does one 32-file warmup, then exactly three measured 990-file
trials. The measured order is B/C, C/B, B/C. Workers stay alive for JIT warmup
but never measure concurrently. Each trial/scenario has fresh targets and a
fresh workspace. System File.Copy/File.Delete references use another directory
and do not provide equivalent transactional or durability guarantees.

## Measurement-only baseline overlay

The baseline does not have the new observer API. Before building it, the runner
checks the normalized-LF SHA-256 of its original BlobStore.cs against
`0b01267219cf171801737eb707df7a80ba748cbbb9b3ab234622aaae5ad8ff00`, then copies
the two reviewed text templates from `BaselineOverlay/` into that disposable
checkout. `current.json` and console JSON record the original hash and the
actual installed overlay file hashes. Templates use `.cs.txt`, so normal builds
cannot accidentally compile an additional BlobStore implementation.

This overlay retains the original copy, WriteThrough mode, sharing modes,
Flush(true), close, temp hash, source rehash, path move and final destination
hash, in that order. Its only additions are an optional observer, stopwatches,
and logical byte counters taken from actual stream positions. It does not
import the candidate algorithm, alter backups or weaken durability. The
candidate uses the same observer schema but its own production Capture path.
Thus both results include diagnostic overhead, not pristine uninstrumented
product timings. Observer exceptions do not change Capture outcomes; the
harness rejects a missing observation through exact capture-count checks.

The observer adapter is compiled only with `RHI_CAPTURE_METRICS`. A worker
command with `suite: "backup"` fails if this instrumentation is unavailable.
The default `suite: "deployment"` keeps historical scenario names/protocol and
can still compile against f26 without a reference to the new Core API. Neither
the old workflow nor `tools/ci_performance.ps1` is changed.

## Counters and interpretation

Each scenario retains Capture call/success counts; elapsed seconds; source and
backup logical bytes read; bytes written/hashed; hash passes; explicit durable
flush calls; published/reused blobs; and calls using WriteThrough. Copy,
verification, durable-flush and publish call-time sums plus unattributed time
are descriptive subphases. The observer is installed only around timed Execute,
never setup or restoration.

On a successful **baseline** fresh original capture of N bytes, expected counters
are source read 2N, backup read 2N, written N, hashed 3N, three hash passes, one
explicit Flush(true), one published blob, zero reused blobs and WriteThrough.
The candidate retains one source read with inline hash and one locked output
readback, plus two explicit durable barriers (before and after no-replace
publication). It does not silently substitute input hashing for output verification
or remove the pre-publication guarantee used by cache hits after a crash.
The copy's read is not itself a hash pass in the baseline. Hash byte counts include completed
hash passes; partial failed calls are retained but never aggregate as success.
All these bytes are logical stream traffic, not physical storage I/O.

Baseline CopySeconds is copy-only; a candidate may hash inline during that
phase. Baseline VerificationSeconds includes temp, source and final hashes,
while candidate checks can be grouped differently. **Compare total Capture
elapsed time; do not call regrouped subphase differences pure copy speedups.**
WriteThrough write cost belongs to the copy phase, not a fabricated explicit
flush-call count. Capture is nested in Execute: never add its time again to
Execute or to the per-trial preflight + Execute total.

All three raw measurements, warmups, process I/O/CPU/allocations, endpoint
handle counts, JSON inventories and slower results are retained. Cache warming
from fixture creation, verification and untimed setup is intentional and
disclosed. This is not cold-cache, physical-I/O, user-PC or statistically
significant evidence. Endpoint handles are not peaks; JSON lengths are not
write counts. No cache-flushing privilege or security-setting change is used.

## Running and bounds

Only `.github/workflows/backup-capture.yml` on `maintenance/backup-capture/**`
pushes starts this experiment. It has read-only repository permission, a
30-minute job timeout, eight-minute per-worker-response timeout, no dispatch/PR
trigger and no artifact/release upload. The output directory must be fresh;
stale results cannot fill a partial run. Only all three valid pairs produce
comparisons. A missing successful completion is a failure, not a result.

`BACKUP_TRIAL`, `BACKUP_SUMMARY` and final self-contained `BACKUP_REPORT` JSON
are printed to the console. The temporary runner also stores
`artifacts/backup-performance/current.json`, raw trials, logs and TRX. There is
no automated external upload. `BackupReport.Tests.ps1` covers warmup exclusion,
all-sample medians, visible slowdowns, nested Capture accounting, zero-baseline
ratios, missing phases/counters/restoration, NaN, failed/incomplete series and
duplicate trial IDs without launching a worker.

## Design references and limits

The candidate keeps RHI's copy-based deployment and independently implements
streaming hash + owned temporary backup publication. Comparable copy-flow
references include [modman's hash-and-backup routine](https://github.com/mrkline/modman/blob/master/src/add.rs)
and [hash-and-write helper](https://github.com/mrkline/modman/blob/master/src/file_utils.rs).
Those projects are design references, not copied code or proof of RHI recovery.
RHI additionally retains locked output readback, both explicit durable boundaries,
no-replace publication, and its existing transaction/receipt rules.

[Vortex's deployment implementation](https://github.com/Nexus-Mods/Vortex/blob/master/src/renderer/src/extensions/mod_management/LinkingDeployment.ts)
illustrates manifest differences and rename-based original backups. Permanent
rename backup, hardlinks and MO2-style process virtualization are separate
architecture choices, not part of this candidate. No third-party source was
incorporated and no license compatibility is assumed.
