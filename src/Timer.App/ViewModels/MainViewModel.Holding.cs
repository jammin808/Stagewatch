using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>One image in the Holding tab.</summary>
public sealed partial class HoldingItem : ObservableObject
{
    public HoldingImage Image { get; }
    public string Id => Image.Id;
    [ObservableProperty] private string _name;
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private bool _isLive;

    public HoldingItem(HoldingImage image)
    {
        Image = image;
        _name = image.Name;
    }
}

// Holding images: a library of stills shown full-screen on every output (and NDI), on browser screens
// (/viewer, /audience, /holding) — over the timer, under blackout. The show thread owns which image is
// on air; decoding happens on the thread pool and the frozen bitmap is handed back, so switching an
// image never stalls a tick.
public partial class MainViewModel
{
    /// <summary>Largest width we decode to: plenty for a 4K screen, and bounds memory per image.</summary>
    private const int HoldingMaxWidth = 3840;

    public ObservableCollection<HoldingItem> HoldingItems { get; } = new();

    [ObservableProperty] private string _holdingLine = "";
    [ObservableProperty] private bool _holdingFill;

    private string _holdingId = "";
    private string _lastHoldingId = "";
    private readonly Dictionary<string, ImageSource> _holdingCache = new();
    private readonly List<string> _holdingCacheOrder = new();

    public bool HasHolding => HoldingItems.Count > 0;
    public bool HoldingOnAir => _holdingId.Length > 0;

    private void InitHolding()
    {
        foreach (var img in _store.Settings.Holding.Images)
        {
            var item = new HoldingItem(img);
            HoldingItems.Add(item);
            LoadThumbnail(item);
        }
        _outputVm.HoldingStretch = _store.Settings.Holding.Fill ? Stretch.UniformToFill : Stretch.Uniform;
        HoldingItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHolding));
    }

    // ------------------------------------------------------------------ adding and removing

    /// <summary>Import files picked in a dialog or dropped on the Holding tab. IO happens off the show thread.</summary>
    public async void AddHoldingFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (!Gate(FeatureGate.AllowsHolding(_license.Tier), "Holding images")) return;
        StatusLine = paths.Count == 1 ? $"Adding {Path.GetFileName(paths[0])}…" : $"Adding {paths.Count} images…";
        var results = await Task.Run(() => paths.Select(p =>
        {
            try { return (Image: HoldingLibrary.Import(p), Error: (string?)null); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { return (Image: (HoldingImage?)null, Error: $"{Path.GetFileName(p)}: {ex.Message}"); }
        }).ToList());
        foreach (var r in results) if (r.Image is { } img) RegisterHolding(img);
        var added = results.Count(r => r.Image is not null);
        var errors = results.Where(r => r.Error is not null).Select(r => r.Error).ToList();
        StatusLine = errors.Count == 0
            ? $"Holding: added {added} image{(added == 1 ? "" : "s")} — click one to put it on every screen"
            : $"Holding: added {added}; " + string.Join("; ", errors);
    }

    /// <summary>IShowControl: an image uploaded from a browser (network thread). Stored here, listed on the show thread.</summary>
    public (string? Id, string? Error) AddHoldingImage(string name, byte[] data)
    {
        if (!FeatureGate.AllowsHolding(_license.Tier)) return (null, "Holding images " + FeatureGate.Needs(FeatureTier.Standard));
        try
        {
            var img = HoldingLibrary.Save(name, data);
            _ui.BeginInvoke(() =>
            {
                RegisterHolding(img);
                StatusLine = $"Holding: \"{img.Name}\" uploaded from a browser";
            });
            return (img.Id, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>IShowControl: image bytes for browser screens (network thread; file reads only).</summary>
    public (byte[] Data, string ContentType)? HoldingImageData(string id) => HoldingLibrary.Read(id);

    private void RegisterHolding(HoldingImage img)
    {
        _store.Update(s => s.Holding.Images.Add(img));
        var item = new HoldingItem(img);
        HoldingItems.Add(item);
        LoadThumbnail(item);
        BumpConfig();
    }

    public void RemoveHolding(HoldingItem item)
    {
        if (item.Id == _holdingId) SetHolding(null);
        _store.Update(s => s.Holding.Images.RemoveAll(i => i.Id == item.Id));
        HoldingItems.Remove(item);
        _holdingCache.Remove(item.Id);
        _holdingCacheOrder.Remove(item.Id);
        if (_lastHoldingId == item.Id) _lastHoldingId = "";
        var id = item.Id;
        _ = Task.Run(() => HoldingLibrary.Delete(id));
        BumpConfig();
        StatusLine = $"Holding: removed \"{item.Name}\"";
    }

    public void RenameHolding(HoldingItem item, string name)
    {
        var clean = HoldingLibrary.CleanName(name);
        _store.Update(_ => item.Image.Name = clean);
        item.Name = clean;
        if (item.Id == _holdingId) { HoldingLine = "🖼  HOLDING · " + clean; MarkDirty(); }
        BumpConfig();
    }

    // ------------------------------------------------------------------ on air

    /// <summary>Click on a thumbnail: put it on air, or take it off if it already is.</summary>
    public void ToggleHoldingItem(HoldingItem item)
    {
        RecordUi(item.Id == _holdingId ? "holding off" : $"holding {Q(item.Name)}");
        SetHolding(item.Id == _holdingId ? null : item.Image);
    }

    /// <summary>The H key and "holding toggle": take it off, or bring back the last one (or the first).</summary>
    public void ToggleHolding()
    {
        RecordUi("holding toggle");
        SetHoldingByKey("toggle");
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ClearHolding()
    {
        if (_holdingId.Length == 0) return;
        RecordUi("holding off");
        SetHolding(null);
    }

    /// <summary>The command grammar's "holding &lt;n|name|id|off|toggle&gt;".</summary>
    private void SetHoldingByKey(string key)
    {
        var images = _store.Settings.Holding.Images;
        if (key.Equals("off", StringComparison.OrdinalIgnoreCase)) { SetHolding(null); return; }
        if (key.Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_holdingId.Length > 0) { SetHolding(null); return; }
            var again = images.FirstOrDefault(i => i.Id == _lastHoldingId) ?? images.FirstOrDefault();
            if (again is null) { StatusLine = "Holding: no images yet — add some on the Holding tab"; return; }
            SetHolding(again);
            return;
        }
        if (HoldingLibrary.Resolve(images, key) is { } img) SetHolding(img);
        else StatusLine = $"Holding: no image called \"{key}\"";
    }

    private void SetHolding(HoldingImage? img)
    {
        if (img is not null && !Gate(FeatureGate.AllowsHolding(_license.Tier), "Holding images")) return;
        var id = img?.Id ?? "";
        if (id == _holdingId) return;
        _holdingId = id;
        if (img is not null) _lastHoldingId = img.Id;
        foreach (var item in HoldingItems) item.IsLive = item.Id == id;
        HoldingLine = img is null ? "" : "🖼  HOLDING · " + img.Name;
        OnPropertyChanged(nameof(HoldingOnAir));
        MarkDirty(); // browser screens switch with the next frame

        if (img is null)
        {
            _outputVm.HoldingImage = null;
            StatusLine = "Holding image off — screens show the timer again";
            return;
        }
        StatusLine = $"Holding image on every screen: {img.Name}";
        if (_holdingCache.TryGetValue(id, out var cached)) { _outputVm.HoldingImage = cached; return; }

        // Decode off the show thread; apply only if it is still the image on air.
        _ = Task.Run(() => DecodeHolding(id, HoldingMaxWidth)).ContinueWith(t =>
        {
            var bmp = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
            _ui.BeginInvoke(() =>
            {
                if (bmp is null)
                {
                    if (_holdingId == id) StatusLine = $"Holding: couldn't open \"{img.Name}\" (unsupported or damaged image)";
                    return;
                }
                CacheHolding(id, bmp);
                if (_holdingId == id) _outputVm.HoldingImage = bmp;
            });
        }, TaskScheduler.Default);
    }

    partial void OnHoldingFillChanged(bool value)
    {
        _store.Update(s => s.Holding.Fill = value);
        _outputVm.HoldingStretch = value ? Stretch.UniformToFill : Stretch.Uniform;
        MarkDirty();
    }

    /// <summary>Keep the last few decoded images, so flipping between two holding slides is instant.</summary>
    private void CacheHolding(string id, ImageSource bmp)
    {
        _holdingCache[id] = bmp;
        _holdingCacheOrder.Remove(id);
        _holdingCacheOrder.Add(id);
        while (_holdingCacheOrder.Count > 3)
        {
            _holdingCache.Remove(_holdingCacheOrder[0]);
            _holdingCacheOrder.RemoveAt(0);
        }
    }

    private void LoadThumbnail(HoldingItem item)
    {
        var id = item.Id;
        _ = Task.Run(() => DecodeHolding(id, 360)).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion && t.Result is { } bmp) _ui.BeginInvoke(() => item.Thumbnail = bmp);
        }, TaskScheduler.Default);
    }

    /// <summary>Decode an image no wider than <paramref name="maxWidth"/>, frozen so any thread may use it. Null if it can't be read.</summary>
    private static BitmapSource? DecodeHolding(string id, int maxWidth)
    {
        var path = HoldingLibrary.PathOf(id);
        if (path is null) return null;
        try
        {
            int width;
            using (var fs = File.OpenRead(path))
                width = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // read the file now and let it go
            bmp.UriSource = new Uri(path);
            if (width > maxWidth) bmp.DecodePixelWidth = maxWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) // a damaged or unsupported file must never take the show down
        {
            return null;
        }
    }
}
