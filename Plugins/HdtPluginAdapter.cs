using System;
using System.Reflection;

namespace HBT.Plugins
{
    /// <summary>
    /// 将 HDT IPlugin 适配为 IHbtPlugin。
    /// 通过反射调用，不直接引用 HDT 类型。
    /// </summary>
    public class HdtPluginAdapter : IHbtPlugin
    {
        private readonly object _hdtPlugin;
        private readonly Type _pluginType;

        public HdtPluginAdapter(object hdtPlugin, Type pluginType)
        {
            _hdtPlugin = hdtPlugin;
            _pluginType = pluginType;
        }

        public string Name => GetProperty<string>("Name") ?? "Unknown HDT Plugin";
        public string Description => GetProperty<string>("Description") ?? "";
        public string Author => GetProperty<string>("Author") ?? "";
        public Version Version => GetProperty<Version>("Version") ?? new Version(1, 0);

        public void OnLoad() => InvokeMethod("OnLoad");
        public void OnUnload() => InvokeMethod("OnUnload");
        public void OnUpdate() => InvokeMethod("OnUpdate");

        // HDT 插件没有 OnGameStart/OnGameEnd，空实现
        public void OnGameStart() { }
        public void OnGameEnd(int placement, int ratingChange) { }

        private T GetProperty<T>(string name)
        {
            try
            {
                var prop = _pluginType.GetProperty(name);
                if (prop == null) return default;
                return (T)prop.GetValue(_hdtPlugin);
            }
            catch { return default; }
        }

        private void InvokeMethod(string name)
        {
            try
            {
                var method = _pluginType.GetMethod(name);
                method?.Invoke(_hdtPlugin, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugin] {Name}.{name} 异常: {ex.Message}");
            }
        }
    }
}
