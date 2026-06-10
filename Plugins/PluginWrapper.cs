namespace HBT.Plugins
{
    public class PluginWrapper
    {
        public IHbtPlugin Plugin { get; }
        public string FilePath { get; }
        public bool Enabled { get; set; } = true;
        public int ConsecutiveErrors { get; set; }

        public PluginWrapper(IHbtPlugin plugin, string filePath)
        {
            Plugin = plugin;
            FilePath = filePath;
        }

        public void Load()
        {
            try
            {
                Plugin.OnLoad();
                Enabled = true;
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[Plugin] {Plugin.Name} OnLoad 失败: {ex.Message}");
                Enabled = false;
            }
        }
    }
}
