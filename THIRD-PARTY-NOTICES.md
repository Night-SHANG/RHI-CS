# 第三方来源

- `RankFTW/RHI`，GPL-3.0：[项目](https://github.com/RankFTW/RHI)、[许可证](https://github.com/RankFTW/RHI/blob/main/LICENSE)。
  `PeInspector.cs` 适配 `PeHeaderService.cs`（blob `a2a7fcb29737075de7b26a9a28dd7c36fca0abd4`）；
  `SteamDiscovery.cs` 适配 `GameDetectionService.Steam.cs`（blob `1eab65dfcafb1f15e71476b1a88ecf566f1e85f8`）。
  更改：移除原 UI/日志依赖、保留只读识别、严格路径校验、不按最大 EXE 自动决定部署目录。
  保留原作者及贡献者版权；本项目衍生代码按 GPL-3.0 提供，完整条款见根目录 LICENSE。
- .NET / Windows App SDK：微软维护；通过 NuGet 使用，按各自包内许可证和第三方通知分发。
- CommunityToolkit.Mvvm：MIT；仅用于展示状态通知，不作为部署记录所有者。
- xUnit：Apache-2.0；Microsoft.NET.Test.Sdk：MIT；仅开发测试依赖。

不捆绑游戏、MOD、ReShade 官方安装器或用户资源。内容指纹和备份由用户在运行时生成。

适配依据：原 RHI 识别能力可独立分离；原主窗口/安装业务耦合在线流程且不满足本项目恢复语义，未复制。
OMM / Vortex / FreeFileSync 仅作为交互及恢复流程参考，本次未复制它们的源码。

ReShade 源码适配：crosire/reshade，固定提交 7bf9de8b33bcc76c3177007e65d73c72dd0f34c0，按 BSD-3-Clause 使用。src/Rhi.Core/Vendor/ReShade/IniFile.cs 复用官方 setup/Utilities/IniFile.cs，新增内存序列化入口；SetupArchive.cs 适配官方 MainWindow.xaml.cs 的附加 ZIP 查找与 DLL 提取流程，增加输入及输出大小限制和重复条目检查。未复用在线下载、安装器执行、注册表 Layer 管理、着色器同步或自动删除已有 DLL。完整许可证随应用分发于 Licenses/ReShade.txt。上游更新须按固定提交对比，并重新通过部署与恢复测试；不自动跟随 main。
