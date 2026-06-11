using System;
using System.Collections.Generic;
using System.IO;

namespace HBT
{

/// <summary>
/// Power.log 文件读取器。
/// 只负责文件读取、位置追踪、行过滤。
/// 不解析游戏事件，不关心游戏逻辑。
/// </summary>
public class LogWatcher
{
    private const long InitialReadBytes = 20 * 1024 * 1024; // 首次读取 20MB（游戏时间长时需要更多）

    string _currentPath;
    long _pos;
    bool _initialRead = true;

    /// <summary>当前的日志路径</summary>
    public string CurrentPath => _currentPath;

    /// <summary>当前读取位置</summary>
    public long CurrentPosition => _pos;

    /// <summary>发生 FileNotFoundException 时触发，外部可以搜索新日志</summary>
    public event Action OnFileNotFound;

    public LogWatcher(string path)
    {
        _currentPath = path;
        _pos = 0;
    }

    /// <summary>
    /// 切换到新日志文件（重置读取位置到文件尾）
    /// </summary>
    public void SwitchTo(string path)
    {
        _currentPath = path;
        _pos = 0;
        _initialRead = true;
    }

    /// <summary>
    /// 设置读取位置
    /// </summary>
    public void SetPosition(long pos)
    {
        _pos = pos;
    }

    /// <summary>
    /// 尝试读取新行。
    /// 首次读取只读最后 5MB，之后读取所有新增行。
    /// 返回 null 表示文件不存在 / 出错，返回空数组表示无新行。
    /// </summary>
    public string[]? TryReadLines()
    {
        long fileLen;
        try
        {
            fileLen = new FileInfo(_currentPath).Length;
        }
        catch (FileNotFoundException)
        {
            OnFileNotFound?.Invoke();
            return null;
        }
        catch
        {
            return null;
        }

        if (fileLen < _pos)
        {
            Console.WriteLine($"[日志] 🔄 检测到日志文件变化（{_pos}→{fileLen}），重置读取位置");
            _pos = 0;
            _initialRead = true;
        }

        // 首次读取：只读最后 5MB
        if (_initialRead && _pos == 0)
        {
            _pos = Math.Max(0, fileLen - InitialReadBytes);
            _initialRead = false;
            Console.WriteLine($"[日志] 首次读取，从位置 {_pos:N0}/{fileLen:N0} 开始");
        }

        if (fileLen <= _pos)
            return new string[0];

        return ReadLinesFrom(_pos, out _pos);
    }

    /// <summary>
    /// 从指定位置向前扩展读取，直到找到 CREATE_GAME。
    /// 用于首次 5MB 读取未找到 CREATE_GAME 时继续扫描。
    /// </summary>
    /// <param name="startFrom">从这个位置开始向前搜索</param>
    /// <returns>读取到的行数组，如果到达文件头则返回所有行</returns>
    public string[]? ReadLinesBackwardToCreateGame(long startFrom)
    {
        try
        {
            var fileLen = new FileInfo(_currentPath).Length;
            var readStart = Math.Max(0, startFrom - InitialReadBytes);
            Console.WriteLine($"[日志] 向前扩展读取: {readStart:N0} → {startFrom:N0}");
            var lines = ReadLinesFrom(readStart, out _);
            _pos = readStart; // 更新读取位置
            return lines;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从文件末尾向前扫描，找到上一局结束标记（tag=STATE value=COMPLETE）的字节偏移量。
    /// 用于中途启动时从当前局开头开始读取。
    /// 如果找不到标记（第一局），返回 0（从头读取）。
    /// </summary>
    public long FindGameStartOffset()
    {
        try
        {
            var fileLen = new FileInfo(_currentPath).Length;
            if (fileLen == 0) return 0;

            const string endMarker = "tag=STATE value=COMPLETE";
            const int chunkSize = 64 * 1024; // 64KB chunks

            using (var fs = new FileStream(_currentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long searchPos = fileLen;
                string remainder = "";

                while (searchPos > 0)
                {
                    long readStart = Math.Max(0, searchPos - chunkSize);
                    int readLen = (int)(searchPos - readStart);

                    fs.Seek(readStart, SeekOrigin.Begin);
                    var buffer = new byte[readLen];
                    fs.Read(buffer, 0, readLen);

                    var text = System.Text.Encoding.UTF8.GetString(buffer) + remainder;
                    var idx = text.LastIndexOf(endMarker, StringComparison.Ordinal);

                    if (idx >= 0)
                    {
                        // 找到标记，返回标记所在行的起始位置
                        var beforeMarker = text.Substring(0, idx);
                        var lastNewline = beforeMarker.LastIndexOf('\n');
                        var offset = readStart + (lastNewline >= 0 ? lastNewline + 1 : 0);
                        Console.WriteLine($"[日志] 找到上一局结束标记，从位置 {offset:N0}/{fileLen:N0} 开始读取");
                        return offset;
                    }

                    remainder = text.Substring(0, Math.Min(text.Length, endMarker.Length));
                    searchPos = readStart;
                }
            }

            // 没找到标记（第一局），从头读取
            Console.WriteLine($"[日志] 未找到上一局结束标记，从头读取");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[日志] 查找结束标记失败: {ex.Message}，从头读取");
            return 0;
        }
    }

    private string[] ReadLinesFrom(long startPos, out long newPos)
    {
        string[] lines;
        using (var fs = new FileStream(_currentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs))
        {
            fs.Seek(startPos, SeekOrigin.Begin);
            var list = new List<string>();
            while (!reader.EndOfStream)
                list.Add(reader.ReadLine());
            newPos = fs.Position;
            lines = list.ToArray();
        }
        return lines;
    }
}

}
