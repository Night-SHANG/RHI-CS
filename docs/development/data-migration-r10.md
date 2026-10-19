# Data 迁移复用审查与边界

用户已确认：整体迁移 Data；新位置校验通过、定位切换完成后清理旧目录。Data 外的游戏与资源来源不移动。普通软件文案直接维护。

| 能力 | 采用入口 / 版本 | 分类、许可与维护 | 取舍与验证 |
|---|---|---|---|
| 目录搬运、跨卷、重试 | Windows 系统 `robocopy.exe`，`/E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /XJ /SL`，运行时记录系统提供版本 | 直接调用 Windows 组件；不分发、不拷贝源码；随 Windows 维护 | 不用 `/MOVE`、`/MIR`、`/PURGE`，复制失败仍保留源；CI 实际执行 |
| 单次移动候选 | .NET 10 `Directory.Move`、`Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory` | 已审查，不采用 | 前者不支持跨卷目录；后者的逐项移动不提供本项目数据定位提交点，失败可能分散内容 |
| 路径/身份/占用保护 | 项目 `PathPolicy`、`DirectoryLease`、`NativeFile` | 直接复用项目现有模块 | 拒绝相互包含、游戏/源目录重叠及重解析路径；源/目标身份复核、文件句柄校验后删除 |
| 内容与数据可用性 | `BlobStore.Hash`、`WorkspaceStore.Open/CheckAvailable` | 直接复用 | 整体文件清单与 SHA-256 验证（不只备份文件），打开目标复核数据 ID 与原件备份 |
| 定位与迁移阶段落盘 | `AtomicJson`、现有 Bootstrap | 复用并适配接入 | 独立迁移记录先于复制持久化；定位切换前源为权威，切换后目标为权威 |
| 接续与清理协调 | `Storage/Migration` 小模块 | 项目逻辑；无现成组件知道 RHI 工作区锁/定位提交/保护记录的边界 | 只按记录与已核对文件清理；缺失文件允许接续，变化/新增文件不能被删除；异常不宣称成功 |

官方证据：
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy
- https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.move?view=net-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.fileio.filesystem.movedirectory?view=net-10.0
- https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.VisualBasic.Core/src/Microsoft/VisualBasic/FileIO/FileSystem.vb （MIT，上游仅审查未复制）

## 数据规则

- 用户选新父目录，确认界面明确展示新父目录下的 `Data` 完整路径；已有同名目标不合并、不覆盖。
- 全部实际 Data 内容迁移，包括未被业务索引引用的普通文件和空目录；工作区运行锁不复制，目标由 WorkspaceStore 建立新锁。
- 不搬游戏与资源来源；新旧 Data 均须与它们隔离；尚有待恢复部署时阻止迁移。
- 迁移期间互斥，拒绝并发配置/部署；任务成功须包含旧目录清理完成。失败和进程中断保留阶段，重启提供继续入口。
- 首次复制前、复制后、定位切换前、清理中、完成落盘前均应有可验证恢复路径；不可只测试正常复制。

## 验证环境

本地只源码和静态核查；GitHub 执行隔离临时目录测试、真实 Robocopy、故障注入、重启状态检查与 WinUI 截图。测试不能访问用户真实 Data。旧包保留。
