# 参与开发

欢迎通过 [Issues](https://github.com/cynic004/VoiceMite/issues) 报告问题或提交 PR。开始前可以阅读 [架构说明](docs/ARCHITECTURE.md) 和 [测试报告](docs/TEST-REPORT.md)。

## 开发环境

- Windows x64
- PowerShell 7+
- `global.json` 指定的 .NET 10 SDK
- Inno Setup 7.1.1 x64（仅制作安装包时需要）

应用目前没有第三方 NuGet 依赖。在仓库根目录运行：

```powershell
dotnet build VoiceMite.sln -c Release
dotnet run --project tests/VoiceMite.Tests -c Release
dotnet run --project tests/VoiceMite.WorkerTests -c Release
```

CI 执行解决方案构建、使用模拟服务的核心测试和 STA 工作线程测试，并生成便携包、源码包。

## 桌面测试

在已解锁的 Windows 桌面上，按修改范围选择测试：

| 命令 | 测试内容 |
| --- | --- |
| `pwsh -File tools/test.ps1 -Ui` | 中英文界面和控制器 |
| `pwsh -File tools/test.ps1 -Target` | 悬浮图标、目标捕获和插入，操作独立测试窗口 |
| `pwsh -File tools/test.ps1 -Windows` | 工作线程、原生控件、剪贴板、DPAPI 和短时麦克风录音 |
| `pwsh -File tools/test.ps1 -External` | 隔离 Edge 页面和新建记事本文件 |

桌面测试请串行运行，期间暂停鼠标和剪贴板操作。上述测试不调用云端 API；真实 STT/LLM 测试需另行配置测试账号并使用非敏感录音。

## 提交修改

- 一个 PR 解决一个问题，说明复现方式、改动和测试结果。
- UI 文案集中放在中英文资源文件中，两种语言保持完整。
- 保持所有核心操作可用鼠标完成，文字由用户确认后插入。
- STT 和 LLM 分开配置；保留取消检查及旧请求隔离。
- 修改录音、焦点、剪贴板、安装或数据目录时，补充相应桌面测试；在测试报告中注明环境、结果和未测项。
- 提交前检查差异，排除构建缓存、开发工具、用户配置、密钥、录音和私人文本。

安全问题请按 [安全政策](SECURITY.md) 报告。发布与回退步骤见 [维护流程](docs/MAINTENANCE.md)。
