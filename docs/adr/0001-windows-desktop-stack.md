# ADR 0001：Windows 桌面技术栈

- 状态：已接受
- 日期：2026-08-12

## 背景

TimeBet 需要在学习时间块内可靠处理计时、系统托盘、前台窗口、键鼠空闲、窗口截图、本地数据库和桌面通知，同时使用 React 实现具有主题能力的年轻化界面。

ManicTime 的公开资料表明，其自动追踪基于前台应用、网站/文档和空闲状态生成并行时间轴，截图作为可配置的独立数据附着到时间轴。官方 2026 插件示例使用 .NET 10、WPF、Windows API、依赖注入和插件清单。ManicTime 桌面核心未开源，因此 TimeBet 只采用可公开验证的架构思想并独立实现。

参考资料：

- https://www.manictime.com/features/automatic-time-tracking
- https://www.manictime.com/features/time-tracking-with-screenshots
- https://github.com/manictime/manictime-client-plugin-example

## 决策

TimeBet Windows 首版采用：

- `.NET 10 LTS`
- `WPF` 桌面外壳
- `WebView2` 前后端承载和消息桥
- `React + TypeScript + Vite` 界面
- `SQLite + Microsoft.Data.Sqlite` 本地数据
- Windows API 与 `Windows.Graphics.Capture` 系统采集
- VS Code 作为主要开发环境

正式项目目标框架为：

```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<UseWPF>true</UseWPF>
```

WPF 仅负责窗口、WebView2、托盘、通知、快捷键和应用生命周期。主要业务界面全部由 React 实现，因此不依赖 WPF 可视化设计器。

## 模块边界

```text
TimeBet.Desktop        -> TimeBet.Application -> TimeBet.Domain
TimeBet.Tracker.Windows -> TimeBet.Application -> TimeBet.Domain
TimeBet.Infrastructure -> TimeBet.Application -> TimeBet.Domain
```

`TimeBet.Domain` 不引用 WPF、WebView2、SQLite、Windows API 或前端代码。

v0.1 使用单桌面进程和后台服务。只有在需要崩溃隔离、独立升级或关闭 UI 后单独运行跟踪器时，才拆分 `TimeBet.Tracker.exe`。

## 理由

- .NET 对 Windows API、WPF、后台服务和 SQLite 支持成熟。
- React 更适合主题、统计、时间轴、奖励商店和动画界面。
- WebView2 已随当前 Windows 环境安装，无需捆绑完整浏览器。
- VS Code 足以完成 CLI 构建、测试和调试，降低贡献环境门槛。
- 单进程首版降低 IPC、生命周期和并发写库复杂度，同时保留未来拆分边界。

## 影响

- 首版只支持 Windows，不承诺直接跨平台。
- 前端与 .NET 之间必须使用有版本、可测试的消息协议。
- 打包流程需要同时构建 React 静态资源和 .NET 桌面程序。
- WebView2 与 Windows 捕获功能必须进行真实运行测试，不能只依赖单元测试。
- macOS/Linux 支持需要单独的桌面与采集适配层，不能假设 Windows 实现可直接复用。

## 未采用方案

- Visual Studio：保留为可选工具，不作为必需环境。
- Electron：全 TypeScript 生态较简单，但资源占用和安装体积更大。
- Tauri：适合跨平台，但会同时引入 Rust 和 Windows 采集适配复杂度。
- 纯 WPF 界面：原生集成优秀，但主题化和社区前端贡献门槛不如 React。
