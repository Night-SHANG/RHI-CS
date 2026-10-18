using Rhi.Core.Deployment;
using Rhi.Core.Vendor.Rhi;

namespace Rhi.Core.Discovery;

public static class GraphicsInspection
{
    public static string SuggestApi(string executable)
    {
        PathPolicy.CheckNoLinks(executable);
        var apis = GraphicsApiDetector.DetectAllApis(executable);
        var families = apis.Select(api => api switch
        {
            GraphicsApiType.DirectX9 => "DirectX 9",
            GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12 => "DirectX 10/11/12",
            GraphicsApiType.OpenGL => "OpenGL",
            GraphicsApiType.Vulkan => "Vulkan",
            _ => "Unknown"
        }).Distinct(StringComparer.Ordinal).ToArray();
        return families.Length == 1 ? families[0] : "Unknown";
    }
}
