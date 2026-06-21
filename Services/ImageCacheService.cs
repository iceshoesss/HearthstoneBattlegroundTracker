using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace HBT.Services
{

/// <summary>
/// 卡牌图片缓存服务
/// 来源: https://art.hearthstonejson.com/v1/tiles/{cardId}.png 或 256x/{cardId}.jpg
/// 策略: LRU 内存缓存 + ETag 验证（无过期时间）
/// </summary>
public class ImageCacheService
{
    private const int MaxMemoryCache = 300;

    private static readonly HttpClient _http = new();
    private readonly string _baseUrl;
    private readonly string _extension;
    private readonly string _cacheDir;
    private readonly string _etagFile;
    private readonly ConcurrentDictionary<string, BitmapImage> _memCache = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly object _lruLock = new();
    private readonly ConcurrentDictionary<string, string> _etags = new();

    public BitmapImage Placeholder { get; }

    /// <summary>创建缓存服务实例</summary>
    /// <param name="size">图片尺寸: "tiles" 或 "256x"</param>
    public ImageCacheService(string size = "tiles")
    {
        _baseUrl = $"https://art.hearthstonejson.com/v1/{size}";
        _extension = size == "tiles" ? "png" : "jpg";
        
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HearthstoneBattlegroundTracker", "Images", size);
        Directory.CreateDirectory(_cacheDir);

        _etagFile = Path.Combine(_cacheDir, "_etags.json");
        LoadEtags();

        Placeholder = CreatePlaceholder();
    }

    /// <summary>创建缓存服务实例（自定义 URL 和缓存目录）</summary>
    public ImageCacheService(string baseUrl, string cacheKey, string extension = "png")
    {
        _baseUrl = baseUrl;
        _extension = extension;
        
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HearthstoneBattlegroundTracker", "Images", cacheKey);
        Directory.CreateDirectory(_cacheDir);

        _etagFile = Path.Combine(_cacheDir, "_etags.json");
        LoadEtags();

        Placeholder = CreatePlaceholder();
    }

    /// <summary>同步获取已缓存图片，未缓存返回占位符并触发异步下载</summary>
    public BitmapImage GetTileOrPlaceholder(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return Placeholder;

        if (_memCache.TryGetValue(cardId, out var cached))
        {
            TouchLru(cardId);
            return cached;
        }

        var localPath = GetLocalPath(cardId);
        if (File.Exists(localPath))
        {
            var img = LoadFromFile(localPath);
            if (img != null)
            {
                AddToCache(cardId, img);
                return img;
            }
        }

        return Placeholder;
    }

    /// <summary>异步获取（ETag 验证 + 下载）</summary>
    public async Task<BitmapImage> GetTileAsync(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return Placeholder;

        // 内存缓存
        if (_memCache.TryGetValue(cardId, out var cached))
        {
            TouchLru(cardId);
            return cached;
        }

        // 本地文件 + ETag 验证
        var localPath = GetLocalPath(cardId);
        if (File.Exists(localPath))
        {
            var needsRefresh = await CheckAndDownload(cardId, localPath);
            if (!needsRefresh)
            {
                var img = LoadFromFile(localPath);
                if (img != null)
                {
                    AddToCache(cardId, img);
                    return img;
                }
            }
        }
        else
        {
            await DownloadImage(cardId, localPath);
        }

        // 加载下载后的文件
        if (File.Exists(localPath))
        {
            var img = LoadFromFile(localPath);
            if (img != null)
            {
                AddToCache(cardId, img);
                return img;
            }
        }

        return Placeholder;
    }

    private async Task<bool> CheckAndDownload(string cardId, string localPath)
    {
        var url = $"{_baseUrl}/{cardId}.{_extension}";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (_etags.TryGetValue(cardId, out var etag))
                req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));

            var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                return false;

            if (resp.IsSuccessStatusCode)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(localPath, bytes);

                var newEtag = resp.Headers.ETag?.Tag;
                if (newEtag != null)
                {
                    _etags[cardId] = newEtag;
                    SaveEtags();
                }
                return true;
            }
        }
        catch { }
        return false;
    }

    private async Task DownloadImage(string cardId, string localPath)
    {
        var url = $"{_baseUrl}/{cardId}.{_extension}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(localPath, bytes);

                var etag = resp.Headers.ETag?.Tag;
                if (etag != null)
                {
                    _etags[cardId] = etag;
                    SaveEtags();
                }
            }
        }
        catch { }
    }

    private string GetLocalPath(string cardId) => Path.Combine(_cacheDir, $"{cardId}.{_extension}");

    private void TouchLru(string cardId)
    {
        lock (_lruLock)
        {
            _lruOrder.Remove(cardId);
            _lruOrder.AddFirst(cardId);
        }
    }

    private void AddToCache(string cardId, BitmapImage img)
    {
        _memCache[cardId] = img;
        lock (_lruLock)
        {
            _lruOrder.Remove(cardId);
            _lruOrder.AddFirst(cardId);
            while (_lruOrder.Count > MaxMemoryCache)
            {
                var oldest = _lruOrder.Last?.Value;
                if (oldest == null) break;
                _lruOrder.RemoveLast();
                _memCache.TryRemove(oldest, out _);
            }
        }
    }

    private void LoadEtags()
    {
        try
        {
            if (File.Exists(_etagFile))
            {
                var json = File.ReadAllText(_etagFile);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (dict != null)
                    foreach (var kv in dict) _etags[kv.Key] = kv.Value;
            }
        }
        catch { }
    }

    private void SaveEtags()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_etags.ToDictionary(kv => kv.Key, kv => kv.Value));
            File.WriteAllText(_etagFile, json);
        }
        catch { }
    }

    private static BitmapImage LoadFromFile(string path)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 128;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private static BitmapImage CreatePlaceholder()
    {
        var img = new BitmapImage();
        using (var ms = new MemoryStream())
        {
            var png = new byte[] {
                0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
                0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
                0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
                0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,
                0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41,
                0x54,0x08,0xD7,0x63,0x60,0x60,0x60,0x00,
                0x00,0x00,0x04,0x00,0x01,0x27,0x36,0x04,
                0x28,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,
                0x44,0xAE,0x42,0x60,0x82
            };
            ms.Write(png, 0, png.Length);
            ms.Position = 0;
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
        }
        return img;
    }
}
}
