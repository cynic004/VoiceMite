# VoiceMite 维护流程

## 每次修改

1. 从当前 `main` 开始，确认本地改动和远端变化，避免覆盖其他人的工作。
2. 修复具体问题，执行相关检查，更新必要文档。涉及安全插入、录音、设置迁移或安装时应保留对应证据。
3. 推送前检查文件列表和差异，不提交密钥、用户配置、完整历史、录音、构建缓存或开发工具。
4. 查看 GitHub Actions 的实际运行结果。工作流文件存在不代表已经运行成功；CI 失败时先处理失败，不把它写成通过。

`Windows build and checks` 在 `main` 更新、pull request 和手动运行时执行：安装 `global.json` 的 SDK，构建解决方案，运行核心测试和模拟目标的 STA 隔离测试，生成未签名便携包与源码包。上传的 Actions 构建产物保存 14 天。它不调用外部 STT/LLM、不录音、不操作目标应用，不替代真机验收。

依赖更新由 Dependabot 每月检查 GitHub Actions。动作固定到提交 SHA，更新 PR 中保留对应版本注释；评审上游发布说明、权限变化和实际 CI 结果后再合并。当前应用没有第三方 NuGet 包。SDK 和 Inno Setup 版本由维护者根据官方来源手动升级并复测，不自动使用预览版或付费服务。

## 真实 Windows 检查

根据改动选择 `tools/test.ps1` 的 `-Ui`、`-Target`、`-Windows`、`-External`，以及 `tools/smoke-install.ps1`。这些选项的桌面、麦克风和剪贴板影响见 CONTRIBUTING。不要并行运行占用桌面或写入同一个输出目录的测试；每轮启动使用新的报告目录并检查报告时间与退出码。

维护报告需分别记录构建、核心逻辑、模拟目标、实际桌面、真实服务、安装/升级/卸载。历史结果只能说明当时的版本和环境。没有真实服务密钥时继续标记未验证；合成静音连接测试和离线中文样例都不是中文语音识别质量验证。

## 发布新版本

1. 同步 `Directory.Build.props`、发布脚本使用的版本号和 CHANGELOG，检查安装 AppId、数据目录及旧配置兼容。版本标签使用 `vMAJOR.MINOR.PATCH`。
2. 执行构建与所需分层测试，检查中英文 UI，更新测试报告中的通过、失败、未测试范围。用实际安装产物验证安装、同版本重装/升级、启动、卸载与数据保留。
3. 生成自包含 x64 安装包、便携 ZIP、源码 ZIP 和 `SHA256SUMS.txt`。检查源码包不含 `.tools`、`bin`、`obj`、`artifacts` 或用户秘密；验证最终文件哈希。
4. 确认版本提交已在远端且 CI 通过，再创建对应标签。不要移动或强制覆盖已经发布的标签。
5. 在 Actions 运行 `Prepare draft Windows release`，输入已存在的版本标签。工作流校验版本、构建、执行非交互测试、下载固定哈希和有效发布者签名的 Inno Setup 7.1.1，生成安装文件并创建 **草稿**。该流程不会公开发布；如果同标签已有 release，会停止而不会替换资产。
6. 审阅草稿及下载文件的校验值，补充真实桌面测试结果、未签名说明、数据流与已知限制，再由获得授权的维护者发布。也可以上传本地已验证的同一版本产物，但不要混合来自不同提交的二进制和校验文件。

发布工作流使用普通仓库 `GITHUB_TOKEN`，只在该手动工作流中授予 `contents: write`。不需要保存 STT/LLM 密钥，不执行第三方网站或私人窗口测试。工作流产物中的固定日期测试报告可能是历史证据，发布前应确认它与版本范围一致。

## 修复与回退

- 普通修复通过新的提交和补丁版本交付，不重写已发布历史。
- 若出现错误插入、密钥泄露或越权访问，先停止推荐受影响版本，在安全渠道处理，给出范围清晰的修复与测试说明。
- 回退源码使用可审阅的 revert 提交；回退应用时保留配置和 DPAPI 密钥，先验证旧版本能读取当前配置，不自行删除用户数据。
- 不自动关闭同步软件或其他应用来完成安装；要求用户退出 VoiceMite 本身即可。遇到外部文件锁时报告具体阻碍，不绕过系统保护。

官方参考：[GitHub Actions 安全加固](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions)、[setup-dotnet](https://github.com/actions/setup-dotnet)、[Inno Setup 下载与签名](https://jrsoftware.org/isdl.php)。
