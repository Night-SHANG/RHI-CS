# CI 产物保存

用户于 2026-10-23 明确要求：Actions artifact 配额已满，保留构建、测试和签名流程，最终产物改为原仓库草稿 Release。本次核查的工作流没有独立签名步骤；不添加虚构签名或跳过验证。

采用 GitHub 官方 CLI `gh release create --draft --target <commit>`；只在 GitHub runner 中使用短期 GITHUB_TOKEN，权限为 contents:write，不使用本机凭据。文档：https://cli.github.com/manual/gh_release_create 。`--draft` 保证上传后仍不公开发布。

- Windows 工作流原有编译、资源/许可核查、核心测试、界面测试不变。成功后上传应用 ZIP、Git 跟踪的匹配源码 ZIP、截图 ZIP、诊断 ZIP 和 SHA256SUMS.txt。应用包必须不含测试 Data。
- 失败时只上传已有截图与诊断，不提供可用应用包；草稿标题和说明标注失败。取消和 PR 不上传草稿。
- 性能工作流保持独立，报告 JSON 和校验清单保存到单独的 performance 草稿，避免与开发包任务竞争同一个 Release。
- 草稿名含运行 ID 和重试次数，不覆盖已有草稿。上传后读取 Release，核对 draft、提交和附件名称/大小。保留旧产物，不自动清理或发布。
- 已移除本项目两份工作流中的 actions/upload-artifact。上传、下载及最终状态以真实 Actions/Release 结果为准；工作流代码修改不代表上传已成功。
