# R6 修正边界

保留既定部署、换版、原件恢复、预检、日志和重启恢复机制。本地不编译、不运行测试或应用。

## 复用与接口

- 完整保护副本：适配已有 DeploymentEngine、WorkspaceStore.Protect、BlobStore、ProtectionLayout；从旧 receipt 取得原版本和受管路径，用本次操作前的已校验 blob 输出，不从候选版本复制。无新部署引擎或备份依赖。
- 直接清除：OperationConsent 新增默认 false 的明确授权，只允许 Clear；保留操作日志和原件恢复。缺失文件进入快照记录，文件不存在时不伪造内容。
- 目录入口：复用 WindowsIntegration 与 Windows Explorer；不移动现有文件。
- 对话框：复用 WinUI ContentDialog 三个原生按钮及现有主题；版本详情底栏为修改、移除、关闭，保护清除为备份后清除、直接清除、取消。
- 校验：所有 PageContext.Edit 表单采用同一提交后提示规则；提交始终重新校验。
- 原生按钮参考：https://learn.microsoft.com/zh-cn/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs

## 视觉约束

依据 visuals/usability-batch-r6.svg。沿用浅色主题、分栏、字体层级、边框、页面边距。行内操作宽 112，高至少 36，间距 8；表格所有单元格居中对齐，操作列宽固定，窄窗口整组换行。版本详情使用单一三按钮底栏。新表单不预先标红，错误区固定。

## 验证

GitHub 核心回归覆盖整套版本、旧版本身份、缺失文件、旧记录兼容、失败回滚和明确直接清除授权。远程 UI 覆盖空表单与提交错误、目录入口、详情底栏、混合部署状态、三按钮取消/备份/直接清除、中英文、深色和高 DPI。实际截图必须与样板逐项比较；用户真实游戏兼容性不等于自动检查。
