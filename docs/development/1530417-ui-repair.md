# Targeted UI repair based on 1530417

Base: `153041768b378f1046ac6ddaa16ac66cfeb427d4` (`RHI-CS-win-x64-1530417.zip`). This branch does not include later deployment, backup, Capture, or QuickCopy redesigns.

## Changes

- Header actions use the available width rather than a four-column cap; relocation follows opening the resource folder, with wrapping on narrower windows.
- Edit dialogs size to their content and no longer reserve empty validation space or freeze a pre-template content height. Long wrapped paths remain accessible.
- Target inspection publishes both busy transitions, including exceptional exits. Viewing differences uses the matching timestamped game/revision snapshot instead of repeating the check; a first view still performs inspection asynchronously. Repeated gestures and independent dialogs are serialized.
- Busy text says only that work is in progress.
- Protection rows can be removed from the displayed inventory after confirmation. This stores a hidden operation ID in workspace metadata. It does not delete, move, or modify any protection file, protection record, or game file. Refresh/restart keep it hidden, and retained-byte accounting still includes its files.
- Settings retain their existing explicit Save semantics for appearance, language, and default library folder; the page now explains this.

## Scope boundary

The deployment and recovery algorithms, source-copy path, blob storage, native file operations, and inspection implementation are unchanged from the selected base. Only the protection inventory's metadata/filtering changes in Core.

Base Git subtree identities for comparison:

- `src/Rhi.Core/Deployment`: `3f0a85775157ebdba57b0777e4f9c155c7a73a63`
- `src/Rhi.Core/Inspection`: `682be6dc394a43d934170c30d72d3f0f69bf9f0a`
- `src/Rhi.Core/Storage/Windows`: `19bdedcc6d631b83b33909d2ea7c29ca6f728d3a`

## Verification

Source checks cover the busy lifecycle, dialog routing, responsive actions, and metadata-only record removal. Runtime tests cover observation cleanup and persisted record hiding. The existing Windows workflow performs compilation, Core regression tests, synthetic UI fixtures, both languages, narrow/wide windows, available real display scales, and restart verification. New UI cases exercise a single uncached difference view, repeated clicks, denied target reads, cancellation, wrapped Chinese paths, and protection-record removal without file changes.

A source check is not a Windows runtime pass. Consult the workflow result for this commit; screenshots require visual review. No test operates on the user's actual game files.
