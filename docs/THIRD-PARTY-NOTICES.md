# 依赖与第三方说明

VoiceMite 自有源码使用 MIT 许可证，见根目录 `LICENSE`。

| 组件 | 开发/发布版本 | 用途 | 来源与许可 |
| --- | --- | --- | --- |
| .NET SDK | 10.0.403（本机） | C# 编译与自包含发布 | [Microsoft .NET](https://dotnet.microsoft.com/download/dotnet/10.0)，[.NET license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| .NET runtime / Windows Desktop / WPF | 10.0.14（当前自包含发布） | 基础运行库与 WPF 桌面界面 | [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT) |
| Windows API / UI Automation / DPAPI / winmm | Windows 系统组件 | 输入目标检查、密钥保护、录音 | 随 Windows 提供，不另外打包系统 DLL |
| Inno Setup compiler | 7.1.1 x64 | 生成当前用户安装程序 | [官方站点](https://jrsoftware.org/isdl.php)，[license](https://github.com/jrsoftware/issrc/blob/main/license.txt) |

应用项目没有第三方 NuGet 运行时包。安装程序和便携包包含所需的 Microsoft .NET 运行时文件；相应 Microsoft 许可证和第三方声明应随发布产物保留。编译器本身不随应用安装。

OpenAI 名称与协议仅用来说明实现的 HTTP 接口。应用不附带 API 额度，不代表第三方服务认可或认证。
