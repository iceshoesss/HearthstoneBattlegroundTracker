using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HBT.Services;

namespace HBTCards
{
public partial class App : Application
{
    private static string ErrorLogPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "HBTCards_error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：未处理异常写日志并提示，而不是无声闪退
        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("UI线程", args.Exception);
            MessageBox.Show($"发生错误：{args.Exception.Message}\n\n详情见 {ErrorLogPath}",
                "HBTCards", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            LogCrash("严重", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash("后台任务", args.Exception);
            args.SetObserved();
        };

        // BattlegroundDB 自动更新检查移至 MainWindow.Loaded 串行执行，
        // 避免与数据加载竞态导致「未提示更新先崩溃」。
    }

    internal static void LogCrash(string source, Exception ex)
    {
        try
        {
            File.AppendAllText(ErrorLogPath,
                $"=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source} ===\r\n{ex}\r\n\r\n");
        }
        catch { /* 日志失败不能再生异常 */ }
    }
}
}
