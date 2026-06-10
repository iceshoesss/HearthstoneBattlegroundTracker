using System;

namespace HBT.Plugins
{
    /// <summary>
    /// HBT 插件接口。开发者实现此接口即可扩展 HBT 功能。
    /// </summary>
    public interface IHbtPlugin
    {
        /// <summary>插件名称</summary>
        string Name { get; }

        /// <summary>插件描述</summary>
        string Description { get; }

        /// <summary>作者</summary>
        string Author { get; }

        /// <summary>版本</summary>
        Version Version { get; }

        /// <summary>插件加载时调用</summary>
        void OnLoad();

        /// <summary>插件卸载时调用</summary>
        void OnUnload();

        /// <summary>每 ~100ms 调用一次</summary>
        void OnUpdate();

        /// <summary>游戏开始时调用</summary>
        void OnGameStart();

        /// <summary>游戏结束时调用</summary>
        /// <param name="placement">排名 1-8</param>
        /// <param name="ratingChange">MMR 变动</param>
        void OnGameEnd(int placement, int ratingChange);
    }
}
