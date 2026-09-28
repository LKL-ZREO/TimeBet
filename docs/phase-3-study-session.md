# TimeBet 阶段 3：学习时间块闭环

- 状态：完成
- 日期：2026-08-12

## 本阶段交付

1. WPF 启动组合根初始化 SQLite，并创建学习会话与账本应用服务。
2. WebView2 消息桥仅开放学习会话和余额白名单命令。
3. React 使用 Zustand 保存当前会话、余额、提交状态、错误和结算凭证。
4. 用户可设定目标、开始、暂停、恢复并完成一次学习时间块。
5. 完成时必须填写总结，可选填多项任务及完成状态。
6. 完成会话、保存任务和增加时间余额由同一个 SQLite 事务提交。
7. 应用重启后会从本地数据库恢复仍在进行或暂停的会话。

## 消息协议

前端请求使用：

```json
{
  "id": "request-id",
  "method": "session.start",
  "payload": { "goal": "完成高数极限章节" }
}
```

白名单命令：

- `session.current`
- `session.start`
- `session.pause`
- `session.resume`
- `session.finish`
- `ledger.balance`

后端状态变化会推送 `session.changed` 事件。请求错误以 `{ id, ok: false, error }` 返回，不向前端暴露数据库异常细节。

## 数据路径

默认数据库：`%LocalAppData%\TimeBet\timebet.db`

开发和烟测可设置：

```powershell
$env:TIMEBET_DATABASE_PATH='F:\temp\timebet-dev.db'
```

## 验收结果

- 前端格式、lint、4 条工作流测试和生产构建通过。
- .NET Release 构建 0 个警告、0 个错误。
- 领域 4 条、应用与 SQLite 8 条、桌面消息桥 2 条测试通过。
- 真实 WPF/WebView2 使用隔离数据库完成生产资源导航烟测，退出码为 0。
- 1180×760 和 900×640 页面截图无重叠或文本溢出。

## 下一阶段

阶段 4 实现活动窗口、空闲状态和可选截图采集，并把采集结果关联到当前学习会话。采集必须由用户主动开始的时间块触发，默认本地保存并提供关闭开关。
