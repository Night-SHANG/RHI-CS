# Backup capture candidate: measured results

Candidate code: `9eea90373a89566b994183ef18e1a7b2edd8409e`.
Delivered baseline: `76efd504ed0d3b473a5d4cc281cbf7d387fe2e32`, with the
measurement-only overlay described in [BACKUP.md](BACKUP.md).
[Windows run 39915910002](https://github.com/Night-SHANG/RHI-CS/actions/runs/39915910002),
job `120289334758`, 2026-10-23 UTC. Full Core tests: 282 passed, zero failed or
skipped. Baseline and candidate each passed all seven execution-integrity cases.
Both report aggregation checks passed. All three measured pairs and warmups
passed source, receipt, backup and original-restoration checks.

GitHub-hosted Windows image `win25-vs2026`, version `20261007.264.1`, four CPUs.
Each measured fixture contains 990 files of 128 KiB, total 123.75 MiB. These are
warm synthetic measurements on one runner, not a forecast for a user's PC.

| Scenario/phase | Baseline median seconds | Candidate median seconds |
|---|---:|---:|
| First overwrite of 990 **unmanaged original** files, preflight + execute | 37.3659 | 19.5882 |
| That operation's preflight | 1.6073 | 1.5738 |
| That operation's execute | 35.6968 | 18.0234 |
| Capture original backups, already included in execute | 26.4220 | 8.6770 |
| Managed version update, **10 changed / 980 unchanged**, total | 3.5707 | 3.4090 |
| Managed update preflight | 1.9483 | 2.1665 |
| Managed update execute | 1.2944 | 1.2597 |

Total medians are medians of each trial's summed phases, not sums of separate
phase medians. The baseline first-overwrite totals were 37.3659, 25.1477 and
43.9376 seconds; candidate totals were 19.5538, 20.0327 and 19.5882 seconds.
This variability rules out treating the difference as a universal speed claim.
The unchanged system-copy reference also varied (medians 0.6473 vs 0.7786 sec).
Managed update improvement is small and must not be advertised as a major
speedup from Capture: it performed **zero Capture calls in both revisions**.

For the 990 original backups, exact successful-operation counters showed:

| Logical work | Baseline | Candidate |
|---|---:|---:|
| Source bytes read | 259,522,560 | 129,761,280 |
| Backup bytes read | 259,522,560 | 129,761,280 |
| Bytes written | 129,761,280 | 129,761,280 |
| Bytes hashed | 389,283,840 | 259,522,560 |
| Hash passes | 2,970 | 1,980 |
| Explicit durable Flush(true) calls | 990 | 1,980 |
| Calls using WriteThrough | 990 | 0 |
| Successfully published backups | 990 | 990 |

The candidate's copy-plus-inline-hash phase median was 0.2811 sec, locked
output verification 0.1652 sec, durable flushes 7.0268 sec, and rename 0.2758
sec. These are separate subphase medians, not an additive decomposition of the
Capture median. The baseline copy phase included WriteThrough and had no inline
hash, so its 18.4547 sec is not a like-for-like pure copy comparison.
Logical read counters do not measure physical disk traffic.

No durability barrier was removed to achieve this result. Candidate payload
and publication both complete an explicit flush before Capture returns; the
original is not overwritten before that return. Independent-process exit
checks are crash-recovery coverage, not hardware power-loss testing. Injected
before-flush failures validate exception ordering, not an emulated device error.
The run's `BACKUP_REPORT` log retains raw trials, subphases, counters, overlay
hashes and method caveats. Full application/UI acceptance remains a separate
release gate; this maintenance run does not publish an application.
