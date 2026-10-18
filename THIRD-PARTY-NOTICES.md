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
