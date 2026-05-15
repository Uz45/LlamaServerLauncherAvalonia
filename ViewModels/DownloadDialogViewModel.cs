using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public enum DownloadSource { Official, Experimental }

public class DownloadDialogViewModel : INotifyPropertyChanged
{
    private readonly LlamaCppDownloadService _downloadService;
    private CancellationTokenSource? _cts;
    private Timer? _debounceTimer;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public ObservableCollection<ReleaseInfo> Releases { get; } = new();
    public ObservableCollection<ReleaseAsset> AvailableAssets { get; } = new();
    public ObservableCollection<ReleaseAsset> ExperimentalAssets { get; } = new();

    private ReleaseInfo? _selectedRelease;
    public ReleaseInfo? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (_selectedRelease != value)
            {
                _selectedRelease = value;
                OnPropertyChanged();
                PopulateAssets();
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private ReleaseAsset? _selectedAsset;
    public ReleaseAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (_selectedAsset != value)
            {
                _selectedAsset = value;
                IsReleaseNotFound = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private ReleaseAsset? _selectedExperimentalAsset;
    public ReleaseAsset? SelectedExperimentalAsset
    {
        get => _selectedExperimentalAsset;
        set
        {
            if (_selectedExperimentalAsset != value)
            {
                _selectedExperimentalAsset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private DownloadSource _selectedSource = DownloadSource.Official;
    public int SelectedSourceIndex
    {
        get => (int)_selectedSource;
        set
        {
            var source = (DownloadSource)value;
            if (_selectedSource != source)
            {
                _selectedSource = source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(IsExperimental));
            }
        }
    }

    public bool IsExperimental => _selectedSource == DownloadSource.Experimental;

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); }
    }

    private bool _isLoadingExperimental;
    public bool IsLoadingExperimental
    {
        get => _isLoadingExperimental;
        set { _isLoadingExperimental = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(ShowProgress)); }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _experimentalStatusMessage = "";
    public string ExperimentalStatusMessage
    {
        get => _experimentalStatusMessage;
        set { _experimentalStatusMessage = value; OnPropertyChanged(); }
    }

    private string _manualTagInput = "";
    public string ManualTagInput
    {
        get => _manualTagInput;
        set
        {
            if (_manualTagInput != value)
            {
                _manualTagInput = value;
                OnPropertyChanged();
                RestartDebounce();
            }
        }
    }

    private bool _isReleaseNotFound;
    public bool IsReleaseNotFound
    {
        get => _isReleaseNotFound;
        set { _isReleaseNotFound = value; OnPropertyChanged(); }
    }

    public bool CanDownload
    {
        get
        {
            if (IsDownloading || IsLoading) return false;
            if (_selectedSource == DownloadSource.Official)
                return SelectedAsset != null && !IsReleaseNotFound;
            return SelectedExperimentalAsset != null && !IsLoadingExperimental;
        }
    }

    public bool ShowProgress => IsDownloading;

    public string? DownloadedReleaseTag { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RequestClose;

    public DownloadDialogViewModel(LlamaCppDownloadService downloadService, string? preselectedTag = null)
    {
        _downloadService = downloadService;
        _ = LoadReleasesAsync(preselectedTag);
    }

    private async Task LoadReleasesAsync(string? preselectedTag = null)
    {
        IsLoading = true;
        IsLoadingExperimental = true;
        StatusMessage = LocalizedStrings.GetString("LoadingReleases");
        ExperimentalStatusMessage = LocalizedStrings.GetString("LoadingExperimental");
        IsReleaseNotFound = false;

        var officialTask = LoadOfficialReleasesAsync(preselectedTag);
        var experimentalTask = LoadExperimentalBuildsAsync();

        await Task.WhenAll(officialTask, experimentalTask);
    }

    private async Task LoadOfficialReleasesAsync(string? preselectedTag)
    {
        try
        {
            var releases = await _downloadService.GetLatestReleasesAsync(10);

            Dispatcher.UIThread.Post(() =>
            {
                Releases.Clear();
                foreach (var r in releases)
                    Releases.Add(r);

                if (!string.IsNullOrEmpty(preselectedTag))
                {
                    var match = Releases.Count > 0 ? Releases[0] : null;
                    if (match != null)
                        SelectedRelease = match;
                }
                else if (Releases.Count > 0)
                {
                    SelectedRelease = Releases[0];
                }

                IsLoading = false;
                StatusMessage = "";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                StatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
            });
        }
    }

    private async Task LoadExperimentalBuildsAsync()
    {
        try
        {
            var builds = await _downloadService.GetExperimentalBuildsAsync();

            Dispatcher.UIThread.Post(() =>
            {
                ExperimentalAssets.Clear();
                foreach (var b in builds)
                    ExperimentalAssets.Add(b);

                if (ExperimentalAssets.Count > 0)
                    SelectedExperimentalAsset = ExperimentalAssets[0];

                IsLoadingExperimental = false;
                ExperimentalStatusMessage = ExperimentalAssets.Count == 0
                    ? LocalizedStrings.GetString("NoExperimentalBuilds")
                    : "";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoadingExperimental = false;
                ExperimentalStatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
            });
        }
    }

    private void PopulateAssets()
    {
        AvailableAssets.Clear();
        SelectedAsset = null;
        IsReleaseNotFound = false;

        if (_selectedRelease == null) return;

        var filtered = _downloadService.FilterAssetsForCurrentOS(_selectedRelease.Assets);
        foreach (var a in filtered)
            AvailableAssets.Add(a);

        if (AvailableAssets.Count > 0)
            SelectedAsset = AvailableAssets[0];
        else
            StatusMessage = LocalizedStrings.GetString("NoAssetsForOS");
    }

    private void RestartDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (string.IsNullOrWhiteSpace(_manualTagInput))
            return;

        if (_selectedRelease != null && _manualTagInput == _selectedRelease.ToString())
            return;

        _debounceTimer = new Timer(async _ =>
        {
            try
            {
                var tag = _manualTagInput.Trim();
                var release = await _downloadService.GetReleaseByTagAsync(tag);

                Dispatcher.UIThread.Post(() =>
                {
                    if (release != null)
                    {
                        IsReleaseNotFound = false;
                        Releases.Clear();
                        Releases.Add(release);
                        SelectedRelease = release;
                        StatusMessage = "";
                    }
                    else
                    {
                        IsReleaseNotFound = true;
                        StatusMessage = LocalizedStrings.GetString("ReleaseNotFound");
                    }
                });
            }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsReleaseNotFound = true;
                    StatusMessage = LocalizedStrings.GetString("ReleaseNotFound");
                });
            }
        }, null, TimeSpan.FromSeconds(7), Timeout.InfiniteTimeSpan);
    }

    public async Task DownloadAsync()
    {
        var asset = _selectedSource == DownloadSource.Official ? SelectedAsset : SelectedExperimentalAsset;
        if (asset == null || IsDownloading) return;

        if (_selectedSource == DownloadSource.Experimental)
        {
            var warning = LocalizedStrings.GetString("ExperimentalConfirmDownload");
            var result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlgResult = await MessageBox.ShowAsync(
                    MainWindow.Instance!,
                    warning,
                    LocalizedStrings.GetString("ConfirmTitle"),
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);
                return dlgResult;
            });
            if (result != MessageBoxResult.Yes) return;
        }

        IsDownloading = true;
        StatusMessage = LocalizedStrings.GetString("Downloading");
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            if (p < 0)
            {
                StatusMessage = LocalizedStrings.GetString("Extracting");
                DownloadProgress = 0;
            }
            else
            {
                DownloadProgress = p;
            }
        });

        try
        {
            var allAssets = _selectedSource == DownloadSource.Official
                ? _selectedRelease?.Assets
                : new System.Collections.Generic.List<ReleaseAsset>(ExperimentalAssets);

            await _downloadService.DownloadAndExtractAsync(asset, progress, _cts.Token,
                _downloadService.FindMatchingCudaDllAsset(asset, allAssets));

            if (!_downloadService.IsInPath(_downloadService.InstallDirectory))
            {
                var pathPrompt = LocalizedStrings.GetString("AddToPathPrompt");
                var result = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dlgResult = await MessageBox.ShowAsync(
                        MainWindow.Instance!,
                        pathPrompt,
                        LocalizedStrings.GetString("ConfirmTitle"),
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);
                    return dlgResult;
                });

                if (result == MessageBoxResult.Yes)
                {
                    await _downloadService.AddToPathIfNeededAsync(_downloadService.InstallDirectory);
                }
            }

            if (_selectedSource == DownloadSource.Official)
                DownloadedReleaseTag = _selectedRelease?.Tag;
            else
                DownloadedReleaseTag = "exp:" + asset.Name;

            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = LocalizedStrings.GetString("DownloadComplete");
                IsDownloading = false;
                _ = Task.Delay(800).ContinueWith(_ =>
                    Dispatcher.UIThread.Post(() => RequestClose?.Invoke()));
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = LocalizedStrings.GetString("DownloadFailed").Replace("{0}", "Cancelled");
                IsDownloading = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
                IsDownloading = false;
            });
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void CancelDownload()
    {
        _cts?.Cancel();
    }

    public void Close()
    {
        CancelDownload();
        _debounceTimer?.Dispose();
        RequestClose?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
