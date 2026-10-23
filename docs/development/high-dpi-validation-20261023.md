# 175% 缩放表单误报排障

2026-10-23 12:06 北京时间（UTC+08:00）。基线：development/safety-corrections，d6cbf2380697b1ecfcb502061a882579749492e3。

## 已核实事实

- Windows run 39895175874 的 224 项核心测试、WinUI 编译、资源检查及此前的部署/恢复交互均通过；失败点是 175% 深色英文表单的几何断言，不是编译器或 Release 上传失败。
- UIA 给出字段滚动区底部 774、提示顶部 757；但同一时刻、恢复缩放前的真实截图显示字段在 y=743 被正确裁剪，提示文字完整可见，底部按钮不重叠。截图来源是该 run 的草稿 Release 421427221，资产 667868143，ZIP SHA-256：186056022e4d0863e44882143661344f4a5bbdf0c5c24936d5deff9f6d2fe7de。
- 因此不能继续靠增加间距或缩小表单来修复这个几何误报，也不能删除无重叠断言。
- WinUI 原生 FrameworkElementAutomationPeer 在 OneCoreTransforms 模式下查询全局边界时会忽略 ScrollContentPresenter 裁剪；FrameworkElement 的 RenderSize 也可能大于实际布局槽。UIA 元素边界不能直接视为滚动视口边界。

## 最小修正与回归

- 仅为共用编辑表单的 ScrollViewer 添加与真实布局槽一致的显式矩形裁剪，使其可访问性边界与实际可见视口一致。保留原生 Grid、自然高度、错误时动态增长、原有字体/色彩/间距/按钮顺序。
- 在布局更新时取 RenderSize 与布局槽的较小范围，并仅在矩形改变时更新，避免仅监听 SizeChanged 遗漏布局槽变化。
- 保留所有现有视觉、几何和功能断言；增加滚动到末尾后 Graphics API 字段完整可达、错误提示仍可见、页脚仍正确的回归检查，防止裁剪掩盖不可达控件。
- 云端静态结构/本地化检查通过（154 个源码/配置文件，双语各 320 键），diff whitespace 检查通过。本机不运行应用；Windows 编译与完整 UI 验证待新 CI，未验证前不能宣称交付完成。

## 参考

- https://github.com/microsoft/microsoft-ui-xaml/blob/51dbbd540b87aee73c0295ed9aa8f49df5d7a109/dxaml/xcp/dxaml/lib/FrameworkElementAutomationPeer_partial.cpp
- https://github.com/microsoft/microsoft-ui-xaml/blob/51dbbd540b87aee73c0295ed9aa8f49df5d7a109/dxaml/xcp/core/core/elements/framework.cpp
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.clip

用户明确授权的一次性截图诊断只读取指定测试图，没有打印密钥。诊断成功 run 39898299350 后，临时权限和 push 触发已在维护分支提交 683dd82 撤回；保留历史，不改开发分支发布权限。

## 候选范围

本次候选仅包含 DPI 视口边界修复和对应 UI 回归，不包含审计提出的部署目标/来源变动保护或失效目录选择器修复。这些新增项仍待方案确认与独立验证；草稿不代表这些风险已解决。

## 实际回归后的更新

2026-10-23 12:48 北京时间。f26ed43 / run 39900237935 编译及224核心测试通过；2062f41 / run 39901753442 编译及232核心测试通过（含新增执行完整性/部分复制崩溃用例），失效目录选择器的两类错误与恢复检查也执行通过。两轮均在96 DPI的后续表单间距断言停止，尚未验证175%。

数值诊断显示该表单 Clip 只在初次 Loaded 更新为349，随后来源输入换行与反馈改变布局时没有 LayoutUpdated 更新记录；UIA仍报告349，而错误提示位置已随着布局变化。不能把旧 Clip 当成当前真实视口，也不应改松间距断言。后续仅补充强引用布局委托，以及滚动器/容器 SizeChanged 通知和队列末尾的最终布局读取，保持原裁剪计算与所有断言不变。新的运行结果待CI，未把静态修改记成通过。

12:57 补充：9087e11 / run 39902349824 的 SizeChanged 补充仍出现同样初始尺寸，232核心测试通过；这不足以证明具体是事件解绑还是 IsLoaded 门控。官方 ContentDialog 源码记录过模板测量/弹出层重挂时紧随 Loaded 的异常 Unloaded，因此下一轮保留生产更新路径，仅加入绑定 ShowAsync finally 生命周期的只读 CI timer 和事件计数，读取尺寸、Clip 与 IsLoaded，不调用 UpdateLayout 或更新裁剪，避免诊断本身掩盖缺陷。同轮只读取已完成的合成性能数字，不重新运行基准。
