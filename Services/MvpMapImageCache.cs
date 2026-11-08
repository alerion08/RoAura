using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace RoAura.Services;

/// <summary>Complete, frozen map images shared by editors and hover previews.</summary>
public sealed class MvpMapImageCache
{
    public static MvpMapImageCache Shared { get; } = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<string, BitmapSource> _images = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _pending = new();
    private readonly Func<string, Task<byte[]?>> _download;
    private readonly string _bundle, _cache;
    public MvpMapImageCache(Func<string, Task<byte[]?>>? download = null, string? bundle = null, string? cache = null)
    {
        _download = download ?? Download;
        _bundle = bundle ?? Path.Combine(AppContext.BaseDirectory,"Data","Minimaps");
        _cache = cache ?? Path.Combine(AppEnvironment.LocalDirectory,"Minimaps");
    }
    public static string Normalize(string? code)
    {
        var key=code?.Trim().ToLowerInvariant()??"";
        return key.Length is >0 and <=64 && key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') ? key : "";
    }
    public async Task<BitmapSource?> GetAsync(string? code)
    {
        var key=Normalize(code);
        if(key.Length==0)return null;
        if(_images.TryGetValue(key,out var image))return image;
        var pending=_pending.GetOrAdd(key,k=>new Lazy<Task<BitmapSource?>>(()=>Load(k)));
        try { return await pending.Value.ConfigureAwait(false); }
        finally { ((ICollection<KeyValuePair<string,Lazy<Task<BitmapSource?>>>>)_pending).Remove(new(key,pending)); }
    }
    private async Task<BitmapSource?> Load(string code)
    {
        foreach(var directory in new[]{_bundle,_cache})
        {
            try
            {
                var path=Path.Combine(directory,code+".png");
                if(!File.Exists(path))continue;
                var image=await Task.Run(()=>Decode(File.ReadAllBytes(path))).ConfigureAwait(false);
                _images[code]=image;return image;
            }
            catch(Exception ex) { DebugLog.Write($"MVP MAP CACHE READ {code}: {ex.Message}"); }
        }
        for(var attempt=0;attempt<2;attempt++)
        {
            try
            {
                var bytes=await _download(code).ConfigureAwait(false);
                if(bytes is null)continue;
                var image=await Task.Run(()=>Decode(bytes)).ConfigureAwait(false);
                _images[code]=image;
                try
                {
                    Directory.CreateDirectory(_cache);
                    var path=Path.Combine(_cache,code+".png");var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try { await File.WriteAllBytesAsync(temporary,bytes).ConfigureAwait(false);File.Move(temporary,path,true); }
                    finally { if(File.Exists(temporary))File.Delete(temporary); }
                }
                catch(Exception ex) { DebugLog.Write($"MVP MAP CACHE WRITE {code}: {ex.Message}"); }
                return image;
            }
            catch(Exception ex) { DebugLog.Write($"MVP MAP DOWNLOAD {code}: {ex.Message}"); }
        }
        return null;
    }
    private static async Task<byte[]?> Download(string code)
    {
        foreach(var uri in new[]{$"https://choobs.org/maps/{code}.png",$"https://www.divine-pride.net/img/map/original/{code}"})
        {
            try
            {
                using var response=await Client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)continue;
                if(response.Content.Headers.ContentLength>2*1024*1024)continue;
                var bytes=await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if(bytes.Length>=8 && bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return bytes;
            }
            catch(HttpRequestException ex){DebugLog.Write($"MVP MAP SOURCE {code}: {ex.Message}");}
            catch(TaskCanceledException){ }
        }
        return null;
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        if(bytes.Length==0||bytes.Length>2*1024*1024)throw new InvalidDataException("Invalid map image size.");
        using var stream=new MemoryStream(bytes,false);
        var image=new BitmapImage();
        image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();
        if(image.PixelWidth<=0||image.PixelHeight<=0||image.PixelWidth>4096||image.PixelHeight>4096)
            throw new InvalidDataException("Invalid map dimensions.");
        image.Freeze();return image;
    }
}
