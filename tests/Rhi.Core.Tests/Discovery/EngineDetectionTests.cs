using Rhi.App.Vendor.Rhi.Models;
using Rhi.App.Vendor.Rhi.Services;

namespace Rhi.Core.Tests.Discovery;

public sealed class EngineDetectionTests
{
    [Fact]
    public void Unreal_candidate_uses_game_binaries_and_skips_engine_tools()
    {
        var root = Root();
        Write(root, "Engine/Binaries/Win64/CrashReportClient.exe");
        var game = Write(root, "Project/Binaries/Win64/Project-Win64-Shipping.exe");
        var result = new GameDetectionService().DetectEngineAndPath(root);
        Assert.Equal(Path.GetDirectoryName(game), result.installPath);
        Assert.Equal(EngineType.Unreal, result.engine);
    }

    [Fact]
    public void Unity_candidate_ignores_bonus_content_and_linked_folders()
    {
        var root = Root();
        Write(root, "artbook/UnityPlayer.dll");
        var external = Root();
        Write(external, "UnityPlayer.dll");
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), external);
        var game = Write(root, "Game/UnityPlayer.dll");
        var result = new GameDetectionService().DetectEngineAndPath(root);
        Assert.Equal(Path.GetDirectoryName(game), result.installPath);
        Assert.Equal(EngineType.Unity, result.engine);
    }

    private static string Root()
    {
        var path = Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Write(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return path;
    }
}
