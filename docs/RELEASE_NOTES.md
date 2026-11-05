# CMCLauncher 1.0.3

## 更新内容

- 新增 CurseForge 地图下载，支持搜索、筛选、分页和安装到实例；同名存档自动另存。
- 新增 CMS 机械动力蓝图来源，统一各蓝图来源的列表与详情页，加入缓存和请求限速。
- 调整下载页面的标题对齐，移除多余提示，简化单一来源页面的筛选项。
- 拆出 CMCLauncher.Core，隔离 Windows 平台功能，并加入 Windows、Linux 和 macOS 核心测试流程，为后续跨平台适配做准备。目前启动器仍仅支持 Windows x64。
- 精简 README，更新源码结构和构建说明。

## 下载

- CMCLauncher-1.0.3-win-x64.zip：完整解压后运行 CMCLauncher.exe。
- CMCLauncher-1.0.3-win-x64.exe：单文件版本，首次运行会释放运行时文件。

发行包包含 .NET 和 Windows App SDK 运行时，Java 与游戏文件需另行下载或导入。
