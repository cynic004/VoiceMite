# VoiceMite

鼠标操控的 Windows AI 语音输入助手。点击悬浮图标，说出想法，VoiceMite 将语音转成文字，按搜索、聊天或提示词场景整理，确认后插入输入框。

[下载](https://github.com/cynic004/VoiceMite/releases/latest) · [反馈问题](https://github.com/cynic004/VoiceMite/issues/new/choose) · [更新记录](CHANGELOG.md)

[![Windows build and checks](https://github.com/cynic004/VoiceMite/actions/workflows/ci.yml/badge.svg)](https://github.com/cynic004/VoiceMite/actions/workflows/ci.yml)

![VoiceMite 中文界面（演示内容）](docs/evidence/rebrand/ui/panel-zh-CN.png)

## 功能

- 悬浮录音图标、音量与时长反馈、系统托盘，支持拖动和记住位置。
- 开始、停止、取消、选择模式、编辑和插入均可用鼠标完成；可调用 Windows 屏幕键盘。
- 同时查看转写原文和整理结果，支持重新整理、歧义选择和手动复制。
- 简体中文、英文界面；界面语言、识别语言和输出语言分别设置。
- STT（语音转写）与 LLM（文字整理）独立配置，支持只转写。
- 插入前核对原输入框和光标位置；目标发生变化时保留文字，供重新选择或复制。
- 可选的本地语言偏好，用于标点、用词和表达长度等设置。

VoiceMite 只准备和插入文字，发送消息、搜索和提交操作由你完成。

## 安装

从 [Releases](https://github.com/cynic004/VoiceMite/releases/latest) 下载 Windows x64 版本：

| 文件 | 用途 |
| --- | --- |
| `VoiceMite-Setup-0.1.0-x64.exe` | 当前用户安装，无需管理员权限 |
| `VoiceMite-0.1.0-win-x64-portable.zip` | 解压后运行 `VoiceMite.exe` |
| `VoiceMite-0.1.0-source.zip` | 完整源码 |
| `SHA256SUMS.txt` | 下载文件校验值 |

要求 Windows 10 2004（19041）及以上，或 Windows 11。发布包已包含 .NET 桌面运行库。当前安装包未签名，Windows 可能显示未知发布者提示。

升级时先从托盘退出 VoiceMite，再运行新版安装程序。卸载入口为 Windows“设置 → 应用”；默认保留设置，可在交互卸载时选择删除。便携版退出后删除解压目录即可，个人设置保存在 `%LOCALAPPDATA%\VoiceMite`。

## 快速开始

1. 启动 VoiceMite，打开设置，选择麦克风并配置 STT 和 LLM 的服务地址、模型及密钥。只需转写时可以不配置 LLM。
2. 分别测试已配置服务的连接，保存设置。Windows 麦克风隐私设置需要允许桌面应用访问。
3. 点击目标输入框，放好光标或选中要替换的文字。
4. 点击悬浮图标开始录音，再次点击停止。录音期间可以随时取消。
5. 查看并编辑结果，点击“插入”，或复制后自行粘贴。

需要更换输入框时，点击“重新选择目标”，在目标位置放好光标，再点击带确认标记的悬浮图标。面板重新展开后即可插入，期间不会开始录音。

首次启动也可选择演示服务，使用预设文字体验流程。实际语音识别需要配置 STT 服务。

## 输入模式

| 模式 | 用途 |
| --- | --- |
| 自动 | 根据转写内容判断场景，有明显歧义时询问 |
| 原文 | 直接使用转写文字，不调用 LLM |
| 搜索 | 整理搜索词或问题，保留限定条件 |
| 聊天 | 整理成自然消息，保留原意 |
| AI 提示词 | 整理目标、背景、要求和输出格式 |

“只转写”和“原文”保留识别文字的语言；其他模式可按设置输出为另一种语言。插入前请核对人名、数字、日期和专业术语。

## API 配置

两个服务可以使用不同的供应商、模型和密钥。当前实现以下 HTTP 协议：

| 配置 | STT 语音转写 | LLM 文字整理 |
| --- | --- | --- |
| 接口 | `POST {BaseUrl}/audio/transcriptions` | `POST {BaseUrl}/chat/completions` |
| 认证 | Bearer API Key | Bearer API Key |
| 请求格式 | multipart WAV；`model`、`language`、`response_format=json` | 文本 messages；`response_format={"type":"json_object"}` |
| 默认地址 | `https://api.openai.com/v1/` | `https://api.openai.com/v1/` |
| 初始模型 | `whisper-1` | `gpt-4o-mini` |

请按服务商实际支持的接口填写模型。第三方服务需分别支持上述音频转写、Chat Completions 和 JSON object 输出；当前不支持 Azure 专用鉴权、Realtime WebSocket 或厂商私有协议。地址使用 HTTPS。

转写在停止录音后发送，当前不支持实时转写和录音中摘要。每段录音最长 120 秒，WAV 最大 4 MiB，文字上限 16,000 字符。请求默认超时 60 秒，可设置为 5–120 秒，不自动重试。

连接测试会发送一次请求，可能产生 API 费用。STT 测试使用短静音 WAV，不打开麦克风。取消任务会停止本地处理；已发送到服务商的请求仍可能计费。

## 隐私与插入行为

- 仅在点击开始后录音。音频临时保存在内存，不保存完整语音历史。
- 音频发送给配置的 STT；文字及启用的少量语言偏好发送给配置的 LLM。应用不读取屏幕截图、浏览历史或其他聊天记录。
- API 密钥使用 Windows 当前用户范围的 DPAPI 加密，可在设置中更换或删除。
- 语言习惯学习默认关闭；开启后只从本工具内确认的文字中提取简短偏好。偏好保存在本地，可查看、编辑和删除，不保存完整对话。
- 插入通过剪贴板粘贴完成。程序会核对原窗口、输入控件、内容和选区，拒绝密码框、终端、权限更高或无法确认的目标。插入不确定时不自动重试。
- 粘贴后尽可能恢复剪贴板原内容；期间若复制了新内容则保留新内容。系统剪贴板历史、云同步或第三方剪贴板工具可能留存文字。

## 开发

开发环境：Windows x64、PowerShell 7+、.NET SDK **10.0.403**（见 `global.json`）。应用使用 .NET 10 / WPF、Windows UI Automation、Win32/winmm 和 DPAPI，无第三方 NuGet 依赖。编译安装包另需 **Inno Setup 7.1.1 x64**。

```powershell
git clone https://github.com/cynic004/VoiceMite.git
cd VoiceMite
pwsh -File tools/build.ps1
pwsh -File tools/test.ps1
```

启动开发版本：

```powershell
dotnet run --project src/VoiceMite.App -c Release
# 使用独立配置目录体验演示流程
dotnet run --project src/VoiceMite.App -c Release -- --demo
```

生成安装包、便携包、源码包及校验文件：

```powershell
pwsh -File tools/publish.ps1
# 仅打包便携版和源码，不需要 Inno Setup
pwsh -File tools/publish.ps1 -SkipInstaller
```

输出位于 `artifacts/`。脚本支持用 `-DotNetPath` 指定 SDK 路径，发布脚本支持 `-InnoCompilerPath` 指定安装编译器。

```text
src/VoiceMite.Core       配置、服务协议、场景整理、状态与偏好
src/VoiceMite.Windows    录音、密钥保护、目标验证与剪贴板
src/VoiceMite.App        WPF 界面、悬浮图标、托盘与本地化
tests/                  核心、工作线程和 Windows 桌面测试
tools/                  构建、测试、打包和安装脚本
docs/                   架构、测试记录与维护文档
```

GitHub Actions 自动构建并运行核心及 STA 工作线程测试。桌面测试按需运行：

```powershell
pwsh -File tools/test.ps1 -Ui        # 双语界面与控制器
pwsh -File tools/test.ps1 -Target    # 鼠标操作、目标捕获与插入
pwsh -File tools/test.ps1 -Windows   # 工作线程、原生控件、录音与剪贴板
pwsh -File tools/test.ps1 -External  # Edge 和记事本
```

桌面测试请逐项运行；`-Target`、`-Windows` 和 `-External` 会使用桌面或剪贴板，`-Windows` 会短暂录音。测试不调用外部 API。

## 当前限制

- 0.1.0 已在 Windows 11 的记事本和 Edge 本地测试页验证插入。部分应用无法提供足够的输入框信息，可改用复制。
- 真实 STT / LLM 服务尚未使用实际密钥完成端到端测试。
- Windows 10、Chrome、多显示器与混合 DPI、休眠恢复仍待测试。

具体结果见 [测试记录](docs/TEST-REPORT.md)。

## 常见问题

- **说什么都出现同一段文字？** STT 当前使用演示服务，在设置中切换服务类型并填写 API 配置。
- **无法录音？** 检查麦克风选择，以及 Windows 隐私设置中的桌面应用麦克风权限。
- **无法插入？** 重新选择输入框并确认光标位置；应用不兼容时使用复制。
- **转写或整理失败？** 分别测试 STT 和 LLM，检查地址、模型、密钥、账户余额及网络连接。

## 参与项目

问题与建议请提交 [Issue](https://github.com/cynic004/VoiceMite/issues/new/choose)，附上系统、应用版本和复现步骤，注意隐藏密钥及私人内容。

[贡献指南](CONTRIBUTING.md) · [架构说明](docs/ARCHITECTURE.md) · [维护与发布](docs/MAINTENANCE.md) · [安全政策](SECURITY.md) · [MIT License](LICENSE)
