# 2026-10-19 使用反馈修正

最终界面名称采用用户指定的“资源库目录”（Library folder），下文“所属目录”均指该可选浏览起点，不是部署目标。

用户授权：部署后换版、多选与刷新整批修正；先更新布局样板；库增加所属目录，版本选择默认打开该目录，不搬动文件。用户后续明确要求添加资源库的名称提醒延后：首次打开不显示红字，点击保存时才显示空名称提示；不改变其他路径校验和保存条件。

本记录覆盖旧交互规范中“已部署无批量复选框”和“差异窗口选择后续操作”：已部署行保留候选版本、多选；按收据生成 Change 请求，未部署生成 Deploy 请求，仍走同一完整预检、保护和恢复。当前版本与候选分开显示，不提前更改已部署身份。无候选/来源失联/待修复等原因明确提示；清除继续不依赖来源。

样板：`../design/visuals/usability-batch-r5.svg`。保留项目共享样式与固定操作区域，资源行操作横排，窄窗口整体下移一行；版本详情改弹窗，列表保留查看和移除入口；填写游戏时合理使用可用高度；统计只更新统计区，查看详情不刷新列表。

## 复用与状态边界

- WorkspaceStore.State 是库、版本和收据的唯一权威。ResourceLibrary 增加可选 SourceRoot；旧 JSON 缺少此字段按 null 读取，所有版本 SourcePath 原值保留，不扫描、复制、移动或隐式入库。
- SourceRoot 只是浏览起点，允许现有外部版本继续使用；新配置验证本地目录存在及数据/游戏隔离，使用现有 PathPolicy。无法使用的起点明确提示，允许手动选择其他目录。
- 直接复用项目 PathPolicy、ConfigurationRules、WorkspaceStore、PageContext、WinUI3Localizer、DeploymentPlanner/Engine；无新的部署/备份/恢复算法。
- 现有 Windows.Storage.Pickers 不能指定任意起始目录；WindowsAppSDK release/1.8-stable 的 FolderPicker 公开接口也无 SuggestedFolder。main 文档提供的接口不能证明当前固定包支持，不为此升级整个 UI SDK。
- 带所属目录的选择直接复用 .NET 10 Windows Forms 的 FolderBrowserDialog/OpenFileDialog 系统对话框：InitialDirectory/SelectedPath、ShowDialog(IWin32Window)、取消返回 null，using 释放。官方 dotnet/winforms（MIT），跟随项目 .NET 10 维护；仅通过窄接口返回路径，不使用 Forms 构建应用页面。代价为自包含发布增加 WindowsDesktop 运行库；验证原生起点、选择、取消及宿主窗口。无指定起点的既有入口继续使用原选择器。
- 参考：https://github.com/microsoft/WindowsAppSDK/blob/release/1.8-stable/specs/Storage.Pickers/FolderPicker.md ; https://learn.microsoft.com/dotnet/api/system.windows.forms.folderbrowserdialog ; https://learn.microsoft.com/dotnet/api/system.windows.forms.openfiledialog 。

## 验证

首轮远程结果：180 项核心测试通过；UseWindowsForms 引入 WindowsDesktop XAML 构建目标并触发 MC6000，与 WinUI XAML 冲突。改为仅 FrameworkReference Microsoft.WindowsDesktop.App.WindowsForms，直接引用系统对话框类与随包运行库，不导入 Forms/WPF 的项目构建目标。该修正仍须远程编译与实际原生窗口检查。

补充授权：已部署复选框也用于批量清除；表头全选/取消全选及部分选中状态。采用 Microsoft selection modes 的复选框与批量命令模式（https://learn.microsoft.com/windows/uwp/design/controls-and-patterns/selection-modes），没有选择时禁用批量命令。候选默认仍是当前版本；全选不会隐式换到其他版本。混合选择时部署/换版和清除按钮分别显示适用项数量，预检列出实际处理对象；取消勾选不触发清除。

本地只静态检查。GitHub 验证旧数据兼容、所属目录不改写来源、混合批次换版/部署、失效来源与禁用原因、取消不写入、清除恢复、局部刷新保持草稿/选择/滚动、目录选择器实际打开指定起点，以及中英文浅深色和高 DPI 截图。编译、自动检查、逐图视觉对照和用户实际使用分开记录。

