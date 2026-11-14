<div align="center">

<img src="docs/assets/cmc-logo.png" alt="CMCLauncher logo" width="120" height="120" />

# CMCLauncher

**你的 Minecraft Java 世界，从这里开始。**

[English](README.md) · [简体中文](README_zh.md) · [日本語](README_ja.md)

<p>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/CMCLauncher-Dev/CMCLauncher?style=flat-square&amp;color=2088ff" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/CMCLauncher-Dev/CMCLauncher/total?style=flat-square&amp;color=35b879" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/CMCLauncher-Dev/CMCLauncher/ci.yml?branch=main&amp;style=flat-square&amp;label=build" /></a>
<a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0-7654c4?style=flat-square" /></a>
<br />
<img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078d4?style=flat-square" />
<img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4?style=flat-square" />
<img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-00a4ef?style=flat-square" />
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/CMCLauncher-Dev/CMCLauncher?style=flat-square" /></a>
</p>

### [下载启动器](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) · [访问官网](https://cmcl.ctfun.top/) · [反馈问题](https://github.com/CMCLauncher-Dev/CMCLauncher/issues)

</div>

一款用于 Minecraft Java 版的 Windows 原生启动器，基于 C#、.NET 8 与 WinUI 3 构建。

本启动器内置了英文、简体中文、日文、俄文、法文、西班牙文与阿拉伯文的界面翻译，可在**设置 → 常规 → 语言**中自动或手动选择.

## 平台支持

| 平台 | 界面 | 兼容性 | 更新维护状态 | 支持系统 / 硬件 |
| --- | --- | --- | --- | --- |
| Windows | WinUI 3 | ✅支持 | 持续维护 | Windows 10（2004 及以上）/ 11，x64 |
| Linux | Avalonia | ⚠️实验性 | 实验性更新，直到稳定 | Debian 系、Arch 系、Red Hat 系发行版，x64 |
| macOS | Avalonia | ⚠️早期阶段，**缺乏测试** | 低优先级维护 | Intel（x64）、Apple M 系列（ARM64） |

Linux 和 macOS 构建供测试使用。macOS 构建 **尚未签名或公证**。测试前请备份实例。

平台要求与已知限制请参阅 [Linux 打包说明](docs/LINUX_PACKAGES.md)和 [Avalonia 前端说明](CMCLauncher.Avalonia/README.md)。

## 界面预览

<div align="center">
<img src="docs/assets/launcher-home.png" alt="CMCLauncher home" width="960" />
</div>

## 为下一个世界准备就绪

<table>
<tr>
<td width="33%" valign="top">
<h3>🧩 实例，一处管理</h3>
<p>独立游戏目录、本地导入，兼容 PCL / HMCL 实例格式。</p>
</td>
<td width="33%" valign="top">
<h3>📦 整合包与加载器</h3>
<p>支持 Fabric、Forge、NeoForge，以及 Modrinth 整合包和受支持的 ZIP 导入。</p>
</td>
<td width="33%" valign="top">
<h3>🔎 发现社区内容</h3>
<p>从 Modrinth / CurseForge 查找 Mod、资源包、光影等内容。</p>
</td>
</tr>
<tr>
<td width="33%" valign="top">
<h3>🎨 打造你的界面</h3>
<p>深浅主题、自定义配色与字体，内置七种界面语言。</p>
</td>
<td width="33%" valign="top">
<h3>📈 掌握系统状态</h3>
<p>CPU、内存与各显卡实时图表，自动计算 Java 内存预算。</p>
</td>
<td width="33%" valign="top">
<h3>🔄 更新与连接</h3>
<p>校验启动器更新、可选 Beta 通道，以及 Frp Client 版本和连接管理。</p>
</td>
</tr>
</table>

## 快速开始

### 运行要求

- Windows 10 2004 版本（内部版本 19041）或更高版本，x64、Linux、MacOS。
- 用拥有 Minecraft Java 版档案的 Microsoft 账号登录。如您想使用离线账户，请至少登录一个 Microsoft 正版账户

### 开始使用
1. 从 [Releases](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) 下载 `CMCLauncher-1.0.5-win-x64.zip`，解压整个压缩包，并运行 `CMCLauncher.exe`。
2. 打开 **设置 → Java**，扫描或导入已有的 Java 安装，或下载 Temurin Java。
3. 打开 **设置 → 账号**，选择 Microsoft 设备代码登录。在浏览器中完成授权，然后等待启动器验证你的 Minecraft Java 档案。
4. 下载游戏版本、导入已有的 `.minecraft` 文件夹，或导入整合包。在主界面选择版本与账号，然后启动。

**设置 → 账号**下提供外部登录（authlib-injector）。在 Microsoft 账号通过 Minecraft Java 版档案验证后可用。

发布压缩包已包含 .NET 与 Windows App SDK 运行时，因此你无需单独安装 .NET。


## 功能特性

| 领域 | 支持的功能 |
| --- | --- |
| 游戏与实例 | 浏览正式版、快照版与愚人节版本；创建隔离实例；导入本地游戏文件夹 |
| 加载器 | 查询并安装 Fabric、Forge 与 NeoForge；识别导入版本所使用的加载器 |
| 整合包 | 导入 Modrinth `.mrpack` 文件、包含受支持清单的 ZIP 压缩包，以及 MultiMC / Prism 的 ZIP 导出；支持拖拽导入 |
| 社区内容 | 选择 Modrinth、CurseForge 或全部来源；浏览分页结果、查看项目版本，并核对游戏、加载器与安装目标的兼容性 |
| 内容管理 | 查看 Mod 元数据与图标；导入、禁用或移除内容；按世界管理数据包 |
| 结构蓝图 | 搜索、导入、导出、重命名与删除 `.litematic`、`.schematic`、`.schem` 与 `.nbt` 文件 |
| Java 与内存 | 扫描、导入并下载 Temurin Java；自动匹配 Java 版本；采用自动或手动堆内存分配 |
| Frp 客户端 | 下载官方 Windows x64 发行版、校验 INI/TOML 配置、启动或停止客户端 |
| 外观与语言 | 浅色、深色与跟随系统主题；颜色、字体、缩放与外观预设；启动器与游戏的语言设置 |
| 账号与诊断 | Microsoft 设备代码登录、外部 authlib-injector 登录、Windows DPAPI 凭据加密、运行时日志，以及游戏错误报告导出 |

界面翻译从可执行文件旁的 `locales/` 下可编辑的 JSON 文件加载。额外的有效语言包会出现在语言选择器中。schema、重载行为与翻译指南详见 [界面语言包](docs/LOCALIZATION.md)。

## 支持范围与限制

### Frp 客户端

打开 **更多 → Frp 客户端**，从在线发行列表中选择一个版本，并选择 **在线下载**。**刷新发行版**会更新列表；**加载更多发行版**会获取更早的版本。下载来自 [官方 frp 发行版](https://github.com/fatedier/frp/releases)，且必须通过官方的 SHA-256 校验。客户端按需下载，不随启动器包分发。

## 数据与日志

| 位置 | 内容 |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | 启动器设置、实例记录与加密的账号凭据 |
| `%APPDATA%/CMCLauncher.Native/library/` | 默认实例与依赖库；可在启动器中选择不同的游戏根目录 |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | 由启动器下载的 Java 安装 |
| 可执行文件旁的 `logs/launcher-yyyyMMdd.log` | 启动器运行时日志 |
| 实例内的 `logs/` 与 `crash-reports/` | 游戏日志与崩溃报告 |

## 从源码运行

在 Windows 上，安装 .NET 8 SDK 以及构建 WinUI 3 项目所需的 Windows SDK / 开发工具。项目目标框架为 `net8.0-windows10.0.19041.0`，架构为 x64。克隆仓库后，在仓库根目录运行以下命令：

```powershell
git clone https://github.com/CMCLauncher-Dev/CMCLauncher.git
cd CMCLauncher
dotnet run --project CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -p:Platform=x64
```

如需一键本地构建，可双击仓库根目录中的 `build-and-run.cmd`。它会将自包含 Release 构建发布到 `.artifacts/local-runs/`

若要在不打开应用的情况下构建，可在 PowerShell 中运行 `./build-and-run.ps1 -NoLaunch`。

若要在本地测试 CurseForge，可在命令提示符中运行 `build-and-run.cmd "-curseforgekey=YOUR_KEY"`。

### 配置 Microsoft 登录

缺少 client ID 的源码构建可运行界面，但 Microsoft 登录不可用。若要在本地构建中启用，请创建 `.config/clientid.local.json` 并填入你已配置的公开 OAuth client ID：

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

### 构建与打包

构建 Release 配置：

```powershell
dotnet build CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64
```

发布自包含目录并创建 ZIP 压缩包：

```powershell
dotnet publish CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-1.0.5-win-x64.zip
```

### 测试

运行原生回归测试：

```powershell
dotnet run --project CMCLauncher.Tests/Windows/CMCLauncher.Tests.csproj -c Release
```

这些测试使用 Windows DPAPI，必须在 Windows 上运行。最后一项本地 Java 检测检查还需要 PATH 中存在 `java`。

发布应用目录后，运行 WinUI 集成检查：

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

该检查使用隔离的数据目录，并生成截图与 `passed.txt`。失败会记录在 `failure.txt` 中。

## 源码结构

| 目录 | 内容 |
| --- | --- |
| [`CMCLauncher.WinUI/`](CMCLauncher.WinUI/) | 桌面入口、窗口、页面、控件与 UI 服务 |
| [`CMCLauncher.Core/`](CMCLauncher.Core/) | 下载、认证协议、资源浏览、存储与平台无关服务 |
| [`CMCLauncher.Platform/Windows/`](CMCLauncher.Platform/Windows/) | Windows 凭据、内存与区域查询、注册表集成和更新安装 |
| [`CMCLauncher.Tests/Core/`](CMCLauncher.Tests/Core/) | 跨平台核心回归检查 |
| [`CMCLauncher.Tests/Windows/`](CMCLauncher.Tests/Windows/) | 原生回归测试与证书夹具 |
| [`scripts/`](scripts/) | 发布、资源整理与打包脚本 |
| [`docs/`](docs/) | 项目官网源码与截图资源 |
| [`.github/workflows/`](.github/workflows/) | 自动化测试、构建与打标签发布 |

## 反馈与贡献

通过 [Issues](https://github.com/CMCLauncher-Dev/CMCLauncher/issues) 反馈问题。请附上启动器版本、Windows 版本、Minecraft 与加载器版本，以及复现步骤。对于启动失败，请附上错误报告或相关日志。分享前请检查是否包含账号信息与本地路径。

提交 Pull Request 前，请运行与你改动相关的测试。布局改动请附上截图，并在修改下载、安装或导入功能时检查取消与失败后的清理情况。

## 许可证

CMCLauncher 基于 [GNU Affero 通用公共许可证 v3.0](LICENSE) 发布。第三方依赖保留各自的许可证。

Minecraft、Mod 与整合包归各自所有者所有。CMCLauncher 与 Mojang Studios、Microsoft 或 NetEase 无任何关联。
