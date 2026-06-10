# HBT 插件开发手册

## 概述

HBT 支持两种插件接口：
- **IHbtPlugin**：HBT 原生接口，推荐新插件使用
- **IPlugin**（HDT 兼容）：兼容现有 HDT 插件

## 快速开始

### 1. 创建项目

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <PlatformTarget>x86</PlatformTarget>
    <UseWPF>true</UseWPF>
    <LangVersion>10</LangVersion>
  </PropertyGroup>
</Project>
```

### 2. 实现 IHbtPlugin

```csharp
using System;
using HBT.Plugins;

public class MyPlugin : IHbtPlugin
{
    public string Name => "我的插件";
    public string Description => "一个示例插件";
    public string Author => "开发者";
    public Version Version => new Version(1, 0, 0);

    public void OnLoad()
    {
        Console.WriteLine("[MyPlugin] 加载成功！");
    }

    public void OnUnload()
    {
        Console.WriteLine("[MyPlugin] 已卸载");
    }

    public void OnUpdate()
    {
        // 每 ~100ms 调用一次
        // 可用于轮询状态、更新 UI 等
    }

    public void OnGameStart()
    {
        Console.WriteLine("[MyPlugin] 游戏开始");
    }

    public void OnGameEnd(int placement, int ratingChange)
    {
        Console.WriteLine($"[MyPlugin] 游戏结束: 第{placement}名, MMR变动{ratingChange:+#;-#;0}");
    }
}
```

### 3. 编译部署

```bash
dotnet build -c Release
```

将编译产物（DLL）复制到 HBT 的 `Plugins/` 目录：

```
HearthstoneBattlegroundTracker/
├── Plugins/
│   └── MyPlugin.dll    ← 放这里
```

HBT 启动时会自动扫描 `Plugins/` 目录并加载所有插件。

## IHbtPlugin 接口参考

| 方法 | 调用时机 | 说明 |
|------|----------|------|
| `OnLoad()` | 插件被加载时 | 初始化资源 |
| `OnUnload()` | 插件被卸载时 | 释放资源 |
| `OnUpdate()` | 每 ~100ms | 轮询、UI 更新 |
| `OnGameStart()` | 游戏开始时 | CREATE_GAME 检测到后 |
| `OnGameEnd(placement, ratingChange)` | 游戏结束时 | 排名和 MMR 变动 |

## HDT 插件兼容

HBT 可以加载实现了 `Hearthstone_Deck_Tracker.Plugins.IPlugin` 接口的 HDT 插件。

HDT 插件通过适配器自动转换为 IHbtPlugin：
- `OnLoad` → `OnLoad`
- `OnUnload` → `OnUnload`
- `OnUpdate` → `OnUpdate`
- `OnGameStart` / `OnGameEnd` → 无对应（空实现）

## 注意事项

- 插件运行在 HBT 主线程中，不要在 `OnUpdate` 中执行耗时操作
- 异常会被捕获，连续 10 次异常后插件会被自动禁用
- 插件目录默认为 `Plugins/`，位于 HBT 可执行文件同级目录
