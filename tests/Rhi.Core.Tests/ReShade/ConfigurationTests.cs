using System.Text;
using ReShade.Setup.Utilities;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.ReShade;

public sealed class ConfigurationTests
{
    [Fact]
    public void Necessary_paths_are_previewed_and_preset_and_unrelated_values_are_preserved()
    {
        using var f = new ReShadeFixture();
        const string original = "[GENERAL]\nPresetPath=.\\我的预设.ini\nPerformanceMode=1\n[INPUT]\nKeyOverlay=36,0,0,0\n[CUSTOM]\nValue=a=b,c\n";
        File.WriteAllText(f.Config, original, Encoding.UTF8);
        var before = File.ReadAllBytes(f.Config);
        var plan = f.Box.Plan(f.Deploy);
        Assert.True(plan.CanExecute);
        var config = Assert.Single(plan.Libraries[0].Changes.Where(c => c.RelativePath == "ReShade.ini"));
        Assert.Equal(2, config.ConfigurationEdits!.Length);
        Assert.All(config.ConfigurationEdits, edit => Assert.Equal("GENERAL", edit.Section));
        Assert.Equal(before, File.ReadAllBytes(f.Config));
        var parsed = new IniFile(new MemoryStream(config.GeneratedContent!));
        Assert.Equal(@".\我的预设.ini", parsed.GetString("GENERAL", "PresetPath"));
        Assert.Equal("1", parsed.GetString("GENERAL", "PerformanceMode"));
        Assert.Equal("36,0,0,0", parsed.GetString("INPUT", "KeyOverlay"));
        Assert.Equal("a=b,c", parsed.GetString("CUSTOM", "Value"));
        Assert.Equal(@".\reshade-shaders\Shaders\**", parsed.GetString("GENERAL", "EffectSearchPaths"));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Engine.Execute(plan, new(true, true))).Status);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Run(f.Clear)).Status);
        Assert.Equal(before, File.ReadAllBytes(f.Config));
    }

    [Fact]
    public void Existing_complete_paths_keep_exact_bytes_comments_encoding_and_spacing()
    {
        using var f = new ReShadeFixture();
        const string original = "; user comment\r\n[GENERAL]\r\nEffectSearchPaths = .\\custom\\**\r\nTextureSearchPaths=\r\nPresetPath=.\\my.ini\r\n";
        File.WriteAllText(f.Config, original, Encoding.Unicode);
        var before = File.ReadAllBytes(f.Config);
        var plan = f.Box.Plan(f.Deploy);
        Assert.True(plan.CanExecute);
        var config = plan.Libraries[0].Changes.Single(c => c.RelativePath == "ReShade.ini");
        Assert.Empty(config.ConfigurationEdits!);
        Assert.Equal(before, config.GeneratedContent);
    }

    [Theory]
    [InlineData("[GENERAL]\nPresetPath=a\nPresetPath=b")]
    [InlineData("[GENERAL]\nPresetPath=a\n[GENERAL]\nValue=b")]
    [InlineData("[GENERAL] trailing\nValue=b")]
    [InlineData("[GENERAL]\neffectsearchpaths=.")]
    [InlineData("[GENERAL]\nEffectSearchPaths=..\\source\\**")]
    [InlineData("[GENERAL]\nEffectSearchPaths=%USERPROFILE%\\shaders")]
    [InlineData("[GENERAL]\nMissingEquals")]
    public void Ambiguous_or_external_configuration_blocks_without_modifying_it(string text)
    {
        using var f = new ReShadeFixture();
        File.WriteAllText(f.Config, text, Encoding.UTF8);
        var before = File.ReadAllBytes(f.Config);
        var plan = f.Box.Plan(f.Deploy);
        Assert.Contains(plan.Issues, i => i.Code == "ReShadeInvalidIni");
        Assert.Equal(before, File.ReadAllBytes(f.Config));
        Assert.Empty(f.Box.Store.Pending());
    }

    [Fact]
    public void Configuration_created_after_confirmation_is_not_overwritten()
    {
        using var f = new ReShadeFixture();
        var plan = f.Box.Plan(f.Deploy);
        File.WriteAllText(f.Config, "[GENERAL]\nPresetPath=new.ini", Encoding.UTF8);
        Assert.Equal("ChangedSincePreflight", Assert.Throws<DeploymentException>(() =>
            f.Box.Engine.Execute(plan, new(true, true))).Code);
        Assert.Contains("new.ini", File.ReadAllText(f.Config, Encoding.UTF8));
        Assert.False(File.Exists(Path.Combine(f.Box.Game.DeploymentRoot, "dxgi.dll")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void INI_from_another_library_is_a_conflict_regardless_of_batch_order(bool reshadeFirst)
    {
        using var f = new ReShadeFixture();
        var source = Path.Combine(Path.GetDirectoryName(f.Source)!, "ReShade.ini");
        File.WriteAllText(source, "[GENERAL]\nPresetPath=pack.ini", Encoding.UTF8);
        var version = new ResourceVersion(Guid.NewGuid(), "pack", source, null, ResourceKind.Files);
        var other = new ResourceLibrary(Guid.NewGuid(), "pack", "", [version]);
        f.Box.Store.Save(f.Box.Store.State with { Libraries = [.. f.Box.Store.State.Libraries, other] });
        var request = new DeploymentRequest(other.Id, version.Id, OperationKind.Deploy);
        var plan = f.Box.Plan(reshadeFirst ? [f.Deploy, request] : [request, f.Deploy]);
        Assert.Contains(plan.Issues, i => i.Code == "TargetConflict");
        Assert.False(File.Exists(f.Config));
    }
}
