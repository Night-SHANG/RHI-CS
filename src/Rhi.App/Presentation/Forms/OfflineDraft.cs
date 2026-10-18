using Rhi.Core.Preparation;

namespace Rhi.App.Presentation;

// Transient UI draft only. Workspace state and file operations remain owned by AppSession.
public sealed record OfflineDraft(OfflineReShadeRequest Request, string? Output = null, Exception? Failure = null)
{
    public static OfflineDraft Empty => new(new OfflineReShadeRequest("", "", "ReShade-offline", "x64", "DirectX 10/11/12"));
}
