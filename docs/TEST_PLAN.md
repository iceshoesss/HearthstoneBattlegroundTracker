# HBT 手动测试计划

> 测试时间: 2026-05-29
> 分支: feat/HBT
> 当前状态: 项目骨架 + Blazor 布局 + HearthMirror 服务

## 前置条件

1. 安装 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
2. 安装 WebView2 Runtime (Win10/11 通常自带)
3. 炉石传说正在运行 (用于 HM 连接测试)

## 测试步骤

### 1. 编译项目

```powershell
cd D:\coding\HDT_BGTracker\HearthstoneBattlegroundTracker
dotnet build -c Debug
```

**预期**: 编译成功，0 errors

### 2. 运行程序

```powershell
dotnet run
```

**预期**: 
- 窗口打开，显示深色主题 UI
- 左侧有导航栏 (仪表盘、战斗模拟、对手追踪、卡牌查询、设置)
- 右侧显示仪表盘页面
- 无崩溃

### 3. 测试导航

点击左侧导航栏各菜单项:
- 仪表盘 → 显示 "仪表盘" 标题 + 状态卡片
- 战斗模拟 → 显示模拟面板布局
- 对手追踪 → 显示搜索框 + 空列表
- 卡牌查询 → 显示筛选栏 + 空网格
- 设置 → 显示设置表单

**预期**: 页面切换正常，无报错

### 4. 测试 HearthMirror 连接

如果炉石正在运行:
- 控制台应输出 `[HM] Detected Hearthstone PID=xxxx`
- 控制台应输出 `[HM] Connected`

如果炉石未运行:
- 控制台应输出 `[HM] Hearthstone not running, disconnected`

### 5. 测试 Blazor 交互

在仪表盘页面:
- 查看 "游戏状态" 卡片是否显示 "等待游戏启动..."
- 查看 "最近对局" 是否显示 "暂无对局记录"

### 6. 测试窗口行为

- 调整窗口大小 → 内容自适应
- 最小化/恢复 → 正常
- 关闭窗口 → 程序退出

## 已知问题 (预期)

1. **HM 连接可能失败**: 因为 HM 需要 `ScryDotNet.dll` (untapped-scry-dotnet.dll) 在运行时可用。如果报错，检查 Lib/ 目录是否有该文件
2. **仪表盘数据为空**: 尚未接入 GameSessionManager，这是正常的
3. **BobsBuddy 未接入**: 战斗模拟页面显示空状态，这是正常的

## 如果遇到问题

1. **编译失败**: 检查 .NET 8 SDK 是否安装: `dotnet --version`
2. **窗口不显示**: 检查是否有 WebView2 Runtime
3. **HM 报错**: 确认 `Lib/untapped-scry-dotnet.dll` 存在
4. **崩溃**: 检查 `HearthstoneBattlegroundTracker_crash.log` 文件

## 下一步 (尚未实现)

- [ ] BobsBuddy 战斗模拟服务
- [ ] CardDatabaseService 卡牌查询
- [ ] GameSessionManager 对局状态管理
- [ ] PowerLogWatcher 日志监控
- [ ] OpponentTrackerService 对手追踪
