# CMCLauncher 1.0.5

## 重新发布说明

本次重新构建修复了仓库迁移后自动更新包被误判为需要手动下载的问题，并同步官网、关于页面及 Linux 软件包中的仓库地址。版本号仍为 1.0.5。旧更新器无法通过发布侧修改获得修复，请手动安装本次构建。

## 更新提示

本次请从本发布页手动下载并安装 1.0.5。旧版更新器仍只认可迁移前的仓库下载地址，可能显示“此发行版需要手动下载”。本次重新发布的安装包已修复仓库地址识别，后续更新可正常识别新仓库的发布资产。

## 更新内容

- 新增 Linux、macOS 的 Avalonia 界面及跨平台构建，提供 Linux DEB、RPM、AppImage、TAR.GZ，以及 macOS Intel / Apple M 系列 TAR.GZ。
- 工具箱整合 FRP、陶瓦联机和 MinecraftPFP；设置中的 FRP、陶瓦管理改为独立小卡片，精简版本操作。
- 调整主页自定义卡片、账户操作按钮、任务日志入口及内存分配显示，新增细灰色滚动条开关。
- 修复 Windows 文件选择、受限目录中的数据与日志写入，以及窗口位置恢复问题。
- 修复 Java 下载包中存在多个可执行文件时的识别错误，完善 macOS Java 路径识别与启动参数。
- Forge / NeoForge 安装依赖复用启动器下载、校验及缓存；安装器跟随代理设置，增加 NeoForge 官方备用下载地址，改善连接失败提示。
- 修复仓库迁移后自动更新无法识别安装包的问题，同时保留历史下载地址兼容。
- 更新 README 平台支持表、翻译与构建说明，清理过时文档。

## 平台状态

| 平台 | 状态 |
| --- | --- |
| Windows x64 | 支持，持续维护 |
| Linux x64 | 实验性支持，继续修复至稳定；提供 Debian 系、Arch 系及 Red Hat 系使用的包或通用分发格式 |
| macOS Intel / Apple M 系列 | 实验性支持，低优先级维护；可能存在无法启动、崩溃等严重问题 |

Linux、macOS 构建供测试使用，请先备份实例。macOS 构建尚未签名或公证。

## 下载

- CMCLauncher-1.0.5-win-x64.zip：完整解压后运行 CMCLauncher.exe。
- CMCLauncher-1.0.5-win-x64.exe：单文件版本，首次运行会释放运行时文件。
- CMCLauncher-Avalonia-1.0.5-linux-x64：按发行版选择 DEB、RPM，或使用 AppImage / TAR.GZ。
- CMCLauncher-Avalonia-1.0.5-osx-arm64.tar.gz / osx-x64.tar.gz：分别用于 Apple M 系列 / Intel Mac。

发行包包含 .NET，Windows 包另含 Windows App SDK 运行时。Java 与游戏文件需另行下载或导入。
