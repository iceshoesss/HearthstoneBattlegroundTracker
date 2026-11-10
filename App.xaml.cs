using System;
using System.IO;
using System.Reflection;
using System.Windows;
using HBT.Services;

namespace HBT
{

public partial class App : Application
{
    private System.Threading.Mutex _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new System.Threading.Mutex(true, "HearthstoneBattlegroundTracker_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 自动更新重启：旧进程可能仍在退出，静默等待锁，不弹「已在运行」
            var updating = Environment.GetEnvironmentVariable("HBT_UPDATING") == "1";
            if (updating)
            {
                var ok = false;
                for (int i = 0; i < 20; i++)
                {
                    System.Threading.Thread.Sleep(500);
                    _mutex.Dispose();
                    _mutex = new System.Threading.Mutex(true, "HearthstoneBattlegroundTracker_SingleInstance", out createdNew);
                    if (createdNew) { ok = true; break; }
                }
                if (!ok)
                {
                    Shutdown();
                    return;
                }
            }
            else
            {
                MessageBox.Show("HearthstoneBattlegroundTracker 已在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        // BattlegroundSpy / BattlegroundDB 程序集解析（Lib/ 优先）
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var asmName = new AssemblyName(args.Name).Name;
            if (asmName == null) return null;

            // Try Lib/ directory first
            var libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lib", asmName + ".dll");
            if (File.Exists(libPath))
                return Assembly.LoadFrom(libPath);

            // Try HDT_PATH environment variable
            var hdtPath = Environment.GetEnvironmentVariable("HDT_PATH");
            if (!string.IsNullOrEmpty(hdtPath))
            {
                var hdtPath2 = Path.Combine(hdtPath, asmName + ".dll");
                if (File.Exists(hdtPath2))
                    return Assembly.LoadFrom(hdtPath2);
            }

            return null;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var crashPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "HearthstoneBattlegroundTracker_crash.log");
            File.WriteAllText(crashPath,
                $"=== HearthstoneBattlegroundTracker 崩溃 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n{ex}");
        };

        // 异步检查 BGDB 更新（不阻塞启动）
        _ = BgdbUpdateService.CheckAndPromptAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
}
