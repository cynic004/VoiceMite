# 参与维护 VoiceMite

先阅读 [README](README.md)、[架构说明](docs/ARCHITECTURE.md)、[实际测试报告](docs/TEST-REPORT.md) 和 [安全政策](SECURITY.md)。请围绕一个可复现的问题提交修改，保留鼠标操作、确认后插入和默认最少数据处理的边界。

## 开发环境与检查

开发需要 Windows x64、PowerShell 7 和 `global.json` 指定的 .NET 10 SDK。应用没有第三方 NuGet 包；安装程序使用 Inno Setup，具体版本与构建步骤见 README。

```powershell
dotnet build VoiceMite.sln -c Release
dotnet run --project tests/VoiceMite.Tests -c Release
dotnet run --project tests/VoiceMite.WorkerTests -c Release
```

核心测试使用模拟语音/文字服务。工作线程测试使用真实 Windows STA 调度器和模拟目标，不会证明真实麦克风、网页或云服务兼容。远端 CI 只执行这些非交互检查并生成未签名构建文件。

需要实际桌面检查时，在已解锁、允许测试占用鼠标和剪贴板的 Windows 桌面上运行对应选项：

```powershell
pwsh -File tools/test.ps1 -Ui
pwsh -File tools/test.ps1 -Target
pwsh -File tools/test.ps1 -Windows
pwsh -File tools/test.ps1 -External
```

`-Ui` 渲染本工具自己的演示窗口；`-Target` 操作独立测试窗口；`-Windows` 会短暂录音并使用剪贴板；`-External` 会打开隔离 Edge 页面和新记事本测试文件。不要把这些交互测试加入无人值守 CI，也不要对私人聊天、密码框或终端做试写。真实 STT/LLM 验证使用专门的测试账号和非敏感录音，成本与数据流须事先明确；不要向仓库提交密钥或原始私人音频。

## 修改与评审

- 提交前检查暂存文件，排除 `bin/`、`obj/`、`.tools/`、`artifacts/`、用户设置、加密密钥和原始语音历史。
- UI 文案放在中英文资源中，保持两种语言键一致。界面语言不得改变权限、服务或功能行为。
- STT 与 LLM 独立建模；不把文本接口视为音频接口。不添加不可控重试、后台摘要轮询或静默数据上传。
- 保留请求代次与取消检查；晚到请求不能恢复旧结果或触发插入。
- 修改目标验证、剪贴板、焦点恢复、安装或数据目录时，增加对应检查，并记录通过、失败和未测试范围。
- CI 通过不能替代 Windows 真机检查；不要把历史截图或模拟结果改名后当成新版本的实际证据。

普通问题可以使用 [Issues](https://github.com/cynic004/VoiceMite/issues)。涉及泄露密钥、越权读取或错误插入的漏洞，请按 SECURITY.md 处理，避免在公开 issue 中暴露敏感信息。

发布、版本号、依赖更新和回退步骤见 [维护流程](docs/MAINTENANCE.md)。
