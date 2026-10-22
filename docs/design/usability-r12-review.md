# r12 实际使用问题与局部样板

状态：用户已批准整批实施及首次设置流程；“目录选择关系”不显示。修改总目录仅影响添加时的浏览起点，已有库路径保持不变；手动搬动后用重新定位预览并更新匹配的版本路径，不移动文件或改写部署记录。新增 library-relocate-r12.svg 对照图。代码与远程用例已编写，等待 GitHub 编译、运行和截图验收；本地未编译、测试或运行应用。

## 本批范围

- 近千文件资源库部署速度问题，须用远程计时区分预检、确认窗口创建、执行准备、实际写入及最终校验。
- 版本列表直接提供修改入口，复用现有 EditVersion；名称链接仍进入原详情弹窗。
- 预检明细按库默认折叠，展开时才创建列表，使用原生虚拟化及有界高度。摘要、阻塞原因、覆盖与修改保护提示在折叠区外。
- 设置常规页新增资源库总目录；添加库时选择器从总目录打开，添加版本时从该库自己的目录打开。总目录是浏览起点，不自动选作新库来源，不搬动现有文件、不自动扫描入库。

## 当前证据

GitHub 开发分支当前为 5784dd5309be3db654acd312465d32983dd1c229。本地着色器来源目录只读取名称、数量和大小，989 个文件，共约 126.81 MiB；未读取内容、部署、上传或修改实际资源。

LibrariesView 版本行目前有查看、移除、打开目录；ShowVersion 弹窗通过原生 PrimaryButton 进入 EditVersion。不是缺少编辑实现，而是直接入口缺失，尚未在本轮运行中复现用户是否还能看到详情中的按钮。

GamesView.Operations.Preflight 对全部变化逐个创建 StackPanel、TextBlock 和 Card，即便用户不查看明细也全部创建。

DeploymentEngine.ExecuteLibrary 每文件调用 EnsureIdentity，后者重新计算完整游戏 EXE 的 SHA-256、检查运行中游戏和路径隔离。每文件更新 IntentIndex 时 WriteJournal 会 CheckAvailable（读取并比较整个 workspace）并重新序列化、落盘完整 OperationJournal。BlobStore 和 FileReplacement 还有多次源/缓存/目标校验。上述调用可由代码确认，各自耗时和真实机器上的主要瓶颈尚未测量。

DeploymentPlanner 目标父子冲突检查为两两比较，989 个路径为 488566 对；Within 是内存字符串比较，不会对每对路径访问磁盘。不能将此直接宣称为极慢的主要原因。

## 保留的视觉结构

样板：visuals/usability-r12.svg、visuals/preflight-r12.svg、visuals/library-root-first-use-r12.svg、visuals/library-relocate-r12.svg；对应 PNG 用于对照。重新定位入口沿用公共顶部 180×36 操作栏，保持每行四项/窄窗口两项或一项的统一规则，版本行的三个操作仍同排。
沿用浅色、现有左侧导航、公共卡片边框、标题层级、表格普通行 80 DIP、紧凑按钮 112×36 与 8 DIP 间距；列表仍保持三按钮同排，窄窗口沿用现有整组下移规则。预检取消和确认继续用公共 ContentDialog 页脚，不随文件条数下移。样板中的目录关系说明不加入软件。

## 复用与验证边界

| 能力 | 采用方式及入口 | 来源、许可与状态 | 适配及验证 |
| --- | --- | --- | --- |
| 版本修改 | 直接复用 LibrariesView.EditVersion / ShowVersion、ConfigurationRules.Version | 本项目现有 5784dd5 实现；项目 GPL-3.0 | 仅调整入口；验证名称、路径、已部署引用、待恢复禁用及刷新 |
| 明细折叠 | 直接引用 Microsoft.UI.Xaml.Controls.Expander | 现有 WinUI 依赖；微软官方当前文档已查；无需新包 | 默认关闭，展开时再建内容；校验多库、错误、确认按钮固定 |
| 大列表 | 直接引用原生 ListView 及虚拟化模板 | 现有 WinUI 依赖；官方性能文档已查 | 禁止一次将全部 UIElement 塞入 StackPanel；需远程检查实际可视元素数、完整滚动和配置差异显示 |
| 默认目录 | 组合现有 Preferences、WindowsIntegration.PickFolder/PickFile、原生选择器 | 本项目与框架现有实现 | 在 Preferences 保存可选总目录，保存主题/语言须保留新字段；验证旧配置兼容、重启、缺失目录及取消 |
| 性能改进 | 先复用现有 Stopwatch/临时目录夹具做远程基准 | .NET 10 标准库与现有 GitHub 测试入口 | 模拟约 990 文件/127 MiB；记录阶段耗时、部署后全量核对及清除/恢复结果。真实用户来源不上传 |
| EXE 校验 | 直接组合 FileStream / FileShare.Read 与现有 DirectoryLease；每库执行期持有只读且拒绝写删的句柄，SHA-256 核验一次 | .NET 10 标准库；新增薄封装 ExecutableLease，不新增复制引擎；远程验证待完成 | 身份变化拒绝、祖先锁、并发写删拒绝、完成和回滚后释放；保留逐文件游戏运行检查、路径隔离、原件备份和落盘日志 |
| 日志持久化 | 本批先计时，保留既有日志与恢复协议 | WorkspaceStore.WriteJournal / AtomicJson / Recovery | 不因日志慢而批量跳过 IntentIndex 或关闭落盘；若必须改协议，另行给出有证据的复用与兼容路线 |

官方参考：
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/expander
- https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview
- https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0

不将 Robocopy 直接替换逐文件部署：本项目当前还要求每文件所有权、覆盖同意、原件备份、修改保护与跨进程中断恢复；复制命令本身不能替代这些协议。本轮不另造复制引擎，也不承诺未测量的提速倍数。
