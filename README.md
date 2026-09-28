# TimeBet 时间银行

TimeBet 是一款面向学生的本地优先开源学习工具。用户完成学习时间块并记录实际成果后，会获得可用于兑换自定义奖励的时间余额。

项目目前已完成本地任务、今日与本周排程、学习时间块闭环、Windows 活动时间轴、当前任务关联度、自定义奖励商店、数据统计和可选星愿机：创建带预计时长和截止日期的任务，把任务安排到具体日期和时间，再从任务或计划时间块直接开始专注；完成学习后，任务与时间块的实际投入会和时间余额在同一个事务中结算。本周计划按周一到周日展示七天安排和计划/实际汇总，尚未执行的时间块可以直接拖到另一天，也可以通过表单调整日期、时间和时长，或按相同本地时间延期到次日。拖拽会保留开始时间和计划时长，冲突落点不会修改原计划，成功移动后可以立即撤销；任务截止日期不会随排程变化而被改写。

应用同时记录学习期间的前台应用与空闲状态，并在计时页根据任务名称、前台进程和窗口标题显示可解释的关联度提示。该判断完全在本地完成，只表示窗口证据与任务的相关性，不替代用户判断。统计页可查看日周趋势、应用分布和追加式时间账本，并导出本地 CSV 摘要。星愿机默认关闭，主动开启后每天最多参与 3 次，概率和返还规则公开。截图默认关闭，且同时需要全局开启与本次会话明确授权。账号、云服务、支付、真实商品交易和产品遥测均不在范围内。

## 技术栈

- .NET 10、WPF、WebView2
- React 19、TypeScript、Vite
- SQLite、迁移和追加式时间账本
- xUnit、Vitest、GitHub Actions

## 仓库结构

```text
src/TimeBet.Domain          核心业务规则
src/TimeBet.Application     应用用例和接口
src/TimeBet.Infrastructure  SQLite、文件和设置实现
src/TimeBet.Tracker.Windows Windows 活动、空闲和截图采集
src/TimeBet.Desktop         WPF 与 WebView2 桌面外壳
frontend                    React 用户界面
tests/TimeBet.Domain.Tests  领域规则测试
tests/TimeBet.Infrastructure.Tests  SQLite 集成测试
docs                        产品和架构文档
```

## 开发环境

- Windows 10 19041 或更高版本
- .NET SDK 10.0.302 或同系列更新补丁
- Node.js 24
- pnpm 11.4
- WebView2 Runtime

Visual Studio 不是必需环境，VS Code 可以完成构建、测试和调试。

## 本地运行

安装并检查前端：

```powershell
pnpm --dir frontend install
pnpm --dir frontend check
```

构建前端资源并启动桌面程序：

```powershell
pnpm --dir frontend build
dotnet run --project .\src\TimeBet.Desktop\TimeBet.Desktop.csproj
```

前端热更新开发：

```powershell
pnpm --dir frontend dev
$env:TIMEBET_FRONTEND_DEV_URL='http://127.0.0.1:5173'
dotnet run --project .\src\TimeBet.Desktop\TimeBet.Desktop.csproj
```

运行全部测试：

```powershell
pnpm --dir frontend test
dotnet test TimeBet.sln
```

当前持久化层使用本地 SQLite 文件。学习时间块完成和时间入账在同一个事务中提交，账本条目由数据库触发器保护为不可更新、不可删除。

默认数据库位于 `%LocalAppData%\TimeBet\timebet.db`，截图位于 `%LocalAppData%\TimeBet\Screenshots`。开发和自动化验证可通过 `TIMEBET_DATABASE_PATH` 与 `TIMEBET_SCREENSHOT_PATH` 指向隔离目录。

## 构建 Windows 安装包

安装包使用 Inno Setup 6。首次构建前安装编译器：

```powershell
winget install --id JRSoftware.InnoSetup -e -s winget
```

然后在仓库根目录执行：

```powershell
.\installer\build-installer.ps1 -Version 0.1.0
```

脚本会锁定安装前端依赖、构建前端、发布自包含的 Windows x64 单文件程序、下载并验证微软签名的 WebView2 Evergreen 引导程序，最后生成：

```text
output\installer\TimeBet-Setup-0.1.0-win-x64.exe
```

安装包按当前 Windows 用户安装到 `%LocalAppData%\Programs\TimeBet`，不需要管理员权限；它会创建开始菜单入口，默认创建桌面快捷方式，并注册标准卸载入口。安装时会先检测 WebView2 Runtime，缺失时通过微软官方引导程序静默安装。卸载会删除程序文件和 WebView2 临时缓存，但会保留 `%LocalAppData%\TimeBet` 中的数据库与截图，避免误删用户数据。

可以用隔离目录执行完整的安装、启动和卸载冒烟测试：

```powershell
.\installer\smoke-installer.ps1 -Version 0.1.0
```

发布新版本时修改 `-Version`；不要更改 `installer\TimeBet.iss` 中的 `AppId`，否则 Windows 会把新版本当成另一个应用。

## 项目原则

- 学习历史和可用时间余额分开保存。
- 截图和活动信息只在用户主动学习期间采集。
- 所有学习数据默认保存在本地。
- ManicTime 只作为公开架构参考，TimeBet 独立实现。

详细基线参见 [阶段 0 文档](docs/phase-0-baseline.md)、[技术栈 ADR](docs/adr/0001-windows-desktop-stack.md)、[阶段 3 文档](docs/phase-3-study-session.md)、[阶段 4 文档](docs/phase-4-windows-tracking.md)、[阶段 5 文档](docs/phase-5-reward-store.md)、[阶段 6 文档](docs/phase-6-analytics-export.md)、[阶段 7 文档](docs/phase-7-star-wish-challenge.md)、[阶段 11 任务闭环](docs/phase-11-task-execution-loop.md)、[阶段 12 今日排程](docs/phase-12-today-planning.md)、[阶段 13 本周计划](docs/phase-13-weekly-planning.md)、[阶段 14 交互式排程](docs/phase-14-interactive-planning.md) 和 [阶段 15 任务关联度](docs/phase-15-task-relevance.md)。

## License

Apache-2.0
