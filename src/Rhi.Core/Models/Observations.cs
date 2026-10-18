namespace Rhi.Core.Models;

public enum TargetHealth { TargetMatched, TargetModified, TargetMissing, TargetUnavailable }
public sealed record TargetFileObservation(string RelativePath, TargetHealth Health);
public sealed record ReceiptObservation(Guid LibraryId, TargetHealth Health,
    TargetFileObservation[] Files, PlanIssue[] Issues);
public sealed record GameObservation(Guid GameId, long Revision, DateTimeOffset CheckedAt, ReceiptObservation[] Receipts);
public sealed record ProtectedOperation(ProtectionEntry Entry, string Directory);
public sealed record ProtectionInventory(long Bytes, DateTimeOffset CheckedAt, ProtectedOperation[] Operations);
