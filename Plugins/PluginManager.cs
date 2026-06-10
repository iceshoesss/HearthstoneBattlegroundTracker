using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HBT.Plugins
{
    public class PluginManager : IDisposable
    {
        private readonly string _pluginDir;
        private readonly List<PluginWrapper> _plugins = new List<PluginWrapper>();
        private bool _disposed;
        public Action<string>? OnLog { get; set; }

        public PluginManager(string pluginDir)
        {
            _pluginDir = pluginDir;
            if (!Directory.Exists(_pluginDir))
                Directory.CreateDirectory(_pluginDir);
        }

        public IReadOnlyList<PluginWrapper> Plugins => _plugins;

        /// <summary>扫描并加载所有插件</summary>
        public void LoadAll()
        {
            var dllFiles = Directory.GetFiles(_pluginDir, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dllFiles)
            {
                try
                {
                    LoadPlugin(dll);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    OnLog?.Invoke($"[Plugin] 加载失败 {Path.GetFileName(dll)}: {ex.Message}");
                    foreach (var lex in ex.LoaderExceptions ?? Array.Empty<Exception>())
                        OnLog?.Invoke($"[Plugin]   → {lex?.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Plugin] 加载失败 {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
            Console.WriteLine($"[Plugin] 已加载 {_plugins.Count(p => p.Enabled)} 个插件");
        }

        private void LoadPlugin(string dllPath)
        {
            var assembly = Assembly.LoadFrom(dllPath);
            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                // HBT 原生插件
                if (typeof(IHbtPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                {
                    var plugin = (IHbtPlugin)Activator.CreateInstance(type);
                    var wrapper = new PluginWrapper(plugin, dllPath);
                    _plugins.Add(wrapper);
                    wrapper.Load();
                    Console.WriteLine($"[Plugin] 加载 HBT 插件: {plugin.Name} v{plugin.Version}");
                    return;
                }

                // HDT 兼容插件
                if (type.GetInterfaces().Any(i => i.FullName == "Hearthstone_Deck_Tracker.Plugins.IPlugin")
                    && !type.IsInterface && !type.IsAbstract)
                {
                    var hdtPlugin = Activator.CreateInstance(type);
                    var adapter = new HdtPluginAdapter(hdtPlugin, type);
                    var wrapper = new PluginWrapper(adapter, dllPath);
                    _plugins.Add(wrapper);
                    wrapper.Load();
                    Console.WriteLine($"[Plugin] 加载 HDT 插件: {adapter.Name} v{adapter.Version}");
                    return;
                }
            }
        }

        /// <summary>通知所有插件：游戏开始</summary>
        public void OnGameStart()
        {
            foreach (var p in _plugins.Where(p => p.Enabled))
                p.Plugin?.OnGameStart();
        }

        /// <summary>通知所有插件：游戏结束</summary>
        public void OnGameEnd(int placement, int ratingChange)
        {
            foreach (var p in _plugins.Where(p => p.Enabled))
                p.Plugin?.OnGameEnd(placement, ratingChange);
        }

        /// <summary>定时更新（每 ~100ms）</summary>
        public void Update()
        {
            foreach (var p in _plugins.Where(p => p.Enabled))
            {
                try
                {
                    p.Plugin?.OnUpdate();
                }
                catch (Exception ex)
                {
                    p.ConsecutiveErrors++;
                    if (p.ConsecutiveErrors >= 10)
                    {
                        Console.WriteLine($"[Plugin] {p.Plugin?.Name} 连续异常过多，已禁用");
                        p.Enabled = false;
                    }
                    else
                    {
                        Console.WriteLine($"[Plugin] {p.Plugin?.Name} OnUpdate 异常: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var p in _plugins)
            {
                try { p.Plugin?.OnUnload(); } catch { }
            }
            _plugins.Clear();
        }
    }
}
