# 页面顶部操作统一 r9

用户已授权统一游戏页与资源库页的顶部按钮。只调整顶部工具栏，保留原顺序、文案、禁用条件和业务行为；表格内操作、设置、弹窗底栏不在本轮范围。

视觉样板：visuals/header-actions-r9.svg。示例不是应用截图。

- 两页共用 HeaderActions：按钮 180×36 DIP、横纵间隔 8，左边缘对齐页面正文。宽度为中英文留足空间，窗口变宽不拉伸按钮。
- 四个按钮需要 752 DIP 的布局槽位（按钮外侧预留 8）；不足时统一为两列，再不足时一列。三个按钮同样沿用这一排列规则。标题、路径与原有提示的顺序保持。
- 普通、悬停、禁用复用 WinUI Button 的统一原生样式。按钮文字不换行，不靠缩小字体塞入；英文须检查完整显示。
- 游戏按钮：修改、核对目标、重新定位游戏（原条件决定是否显示）、打开部署目录。资源库按钮：修改、添加版本、移除资源库、打开资源库目录。无新增菜单或功能。
- 复用当前 Microsoft.WindowsAppSDK 1.8.260921001 的 VariableSizedWrapGrid，直接使用 Orientation、ItemWidth、ItemHeight、MaximumRowsOrColumns 和现有 Button；仅做尺寸与断点配置，不实现自定义面板、不引入依赖。参考：[Microsoft 原生布局说明](https://learn.microsoft.com/en-us/windows/apps/develop/ui/layout-panels#variablesizedwrapgrid)。数据权威仍为 Session，布局不保存业务状态。
- GitHub 实机核对两页 180×36、8 间隔、左对齐及两列换行，中英文浅深色和可用 DPI 交叉覆盖。实际截图与本稿对照后交付；本地只静态核查，不编译或运行测试。
