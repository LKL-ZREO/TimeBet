# 参与贡献

感谢参与 TimeBet。提交代码前请先阅读 `docs/` 中的产品基线和架构决策。

## 开发流程

1. 从 `main` 创建短生命周期分支。
2. 保持改动集中在一个明确问题上。
3. 为新增业务规则补充测试。
4. 提交前运行前端检查和 .NET 测试。
5. 在 Pull Request 中说明行为变化、验证方式和隐私影响。

```powershell
pnpm --dir frontend check
dotnet build TimeBet.sln
dotnet test TimeBet.sln
```

## 架构约束

- Domain 不得依赖 WPF、SQLite、WebView2 或 Windows API。
- React 不得直接访问数据库和系统 API。
- 时间余额必须通过追加式账本变更。
- 新增采集功能必须提供关闭、排除和删除能力。
- 不提交 ManicTime 二进制、反编译代码或受版权保护的视觉素材。

## 提交内容

- 不提交 `bin`、`obj`、`node_modules`、`dist` 或本地数据库。
- 不提交真实学习截图、窗口标题或个人活动数据。
- 新增第三方资源时说明许可证和来源。
