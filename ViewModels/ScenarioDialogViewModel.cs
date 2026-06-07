using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using Avalonia.Controls;

namespace LlamaServerLauncher.ViewModels;

public class ProfileItem
{
    public string Name { get; }
    public string Summary { get; }

    public ProfileItem(string name, string summary)
    {
        Name = name;
        Summary = summary;
    }
}

public class ScenarioDialogViewModel : INotifyPropertyChanged
{
    private ProfileItem? _selectedAvailableProfile;
    private int _selectedAvailableProfileIndex = -1;
    private ProfileItem? _selectedScenarioProfile;
    private int _selectedScenarioProfileIndex = -1;
    private string _scenarioName = "";
    private int _intervalSeconds;
    private bool _autoStart;
    private bool _isEditMode;
    private string? _originalName;
    private readonly HashSet<string> _existingNames;
    private readonly Dictionary<string, ServerConfiguration> _profileConfigs;
    private readonly Func<string, ServerConfiguration, Task>? _saveProfileCallback;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public string ScenarioName
    {
        get => _scenarioName;
        set { _scenarioName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set { _intervalSeconds = value; OnPropertyChanged(); }
    }

    public bool AutoStart
    {
        get => _autoStart;
        set { _autoStart = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ProfileItem> AvailableProfiles { get; } = new();
    public ObservableCollection<ProfileItem> ScenarioProfiles { get; } = new();

    public ProfileItem? SelectedAvailableProfile
    {
        get => _selectedAvailableProfile;
        set { _selectedAvailableProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAdd)); OnPropertyChanged(nameof(CanCloneToScenario)); }
    }

    public int SelectedAvailableProfileIndex
    {
        get => _selectedAvailableProfileIndex;
        set { _selectedAvailableProfileIndex = value; }
    }

    public ProfileItem? SelectedScenarioProfile
    {
        get => _selectedScenarioProfile;
        set { _selectedScenarioProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRemove)); OnPropertyChanged(nameof(CanMoveUp)); OnPropertyChanged(nameof(CanMoveDown)); }
    }

    public int SelectedScenarioProfileIndex
    {
        get => _selectedScenarioProfileIndex;
        set { _selectedScenarioProfileIndex = value; OnPropertyChanged(nameof(CanMoveUp)); OnPropertyChanged(nameof(CanMoveDown)); }
    }

    public bool IsEditMode => _isEditMode;

    public string DialogTitle => _isEditMode
        ? LocalizedStrings.GetString("ScenarioDialogTitleEdit")
        : LocalizedStrings.GetString("ScenarioDialogTitleCreate");

    public bool CanSave => !string.IsNullOrWhiteSpace(ScenarioName) && ScenarioProfiles.Count > 0;
    public bool CanAdd => _selectedAvailableProfile != null;
    public bool CanRemove => _selectedScenarioProfile != null;
    public bool CanCloneToScenario => _selectedAvailableProfile != null && _saveProfileCallback != null;
    public bool CanMoveUp => _selectedScenarioProfile != null && _selectedScenarioProfileIndex > 0;
    public bool CanMoveDown => _selectedScenarioProfile != null && _selectedScenarioProfileIndex >= 0 && _selectedScenarioProfileIndex < ScenarioProfiles.Count - 1;

    public bool DialogResult { get; private set; }

    public ScenarioDialogViewModel(
        List<string> allProfileNames,
        Dictionary<string, ServerConfiguration> profileConfigs,
        HashSet<string> existingNames,
        ScenarioInfo? existingScenario = null,
        Func<string, ServerConfiguration, Task>? saveProfileCallback = null)
    {
        _profileConfigs = profileConfigs;
        _existingNames = existingNames;
        _saveProfileCallback = saveProfileCallback;

        if (existingScenario != null)
        {
            _isEditMode = true;
            _originalName = existingScenario.Name;
            _scenarioName = existingScenario.Name;
            _intervalSeconds = existingScenario.IntervalSeconds;
            _autoStart = existingScenario.AutoStart;

            var existingProfileSet = new HashSet<string>(allProfileNames, StringComparer.OrdinalIgnoreCase);

            foreach (var p in existingScenario.ProfileNames)
            {
                if (existingProfileSet.Contains(p))
                    ScenarioProfiles.Add(CreateProfileItem(p));
            }

            var scenarioProfileSet = new HashSet<string>(existingScenario.ProfileNames, StringComparer.OrdinalIgnoreCase);
            foreach (var p in allProfileNames)
            {
                if (!scenarioProfileSet.Contains(p))
                    AvailableProfiles.Add(CreateProfileItem(p));
            }
        }
        else
        {
            _isEditMode = false;
            foreach (var p in allProfileNames)
                AvailableProfiles.Add(CreateProfileItem(p));
        }
    }

    private ProfileItem CreateProfileItem(string name)
    {
        var summary = BuildProfileSummary(name);
        return new ProfileItem(name, summary);
    }

    private string BuildProfileSummary(string profileName)
    {
        if (!_profileConfigs.TryGetValue(profileName, out var config) || config == null)
            return "";

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(config.ModelsDir))
            parts.Add($"Models dir: {config.ModelsDir}");
        else if (!string.IsNullOrWhiteSpace(config.ModelPath))
            parts.Add($"Model: {Path.GetFileName(config.ModelPath)}");
        else if (!string.IsNullOrWhiteSpace(config.HfRepo))
        {
            var hf = config.HfRepo;
            if (!string.IsNullOrWhiteSpace(config.HfFile))
                hf += $"/{config.HfFile}";
            parts.Add($"HuggingFace: {hf}");
        }

        parts.Add($"{config.Host}:{config.Port}");

        if (config.EnableWebUI == true)
            parts.Add("WebUI: on");

        if (config.RunInDocker)
            parts.Add("Docker");

        if (config.ContextSize.HasValue && config.ContextSize.Value > 0)
            parts.Add($"Context: {config.ContextSize.Value}");

        if (config.GpuLayers.HasValue)
            parts.Add($"GPU layers: {config.GpuLayers.Value}");

        return string.Join("\n", parts);
    }

    public void AddProfile()
    {
        if (SelectedAvailableProfile == null) return;
        var profile = SelectedAvailableProfile;
        AvailableProfiles.Remove(profile);
        ScenarioProfiles.Add(profile);
        OnPropertyChanged(nameof(CanSave));
    }

    public async Task CloneToScenarioAsync(Window? owner)
    {
        if (SelectedAvailableProfile == null || _saveProfileCallback == null) return;
        if (!_profileConfigs.TryGetValue(SelectedAvailableProfile.Name, out var sourceConfig) || sourceConfig == null) return;

        var allNames = new HashSet<string>(
            AvailableProfiles.Select(p => p.Name)
                .Concat(ScenarioProfiles.Select(p => p.Name)),
            StringComparer.OrdinalIgnoreCase);

        var cloneResult = await MainViewModel.ShowCloneDialogAsync(
            SelectedAvailableProfile.Name, sourceConfig, allNames, owner);

        if (cloneResult == null) return;

        await _saveProfileCallback(cloneResult.Name, cloneResult.Config);

        _profileConfigs[cloneResult.Name] = cloneResult.Config;

        var newItem = new ProfileItem(cloneResult.Name, BuildProfileSummary(cloneResult.Name));
        ScenarioProfiles.Add(newItem);

        OnPropertyChanged(nameof(CanSave));
    }

    public void RemoveProfile()
    {
        if (SelectedScenarioProfile == null) return;
        var profile = SelectedScenarioProfile;
        var idx = ScenarioProfiles.IndexOf(profile);
        ScenarioProfiles.Remove(profile);
        AvailableProfiles.Add(profile);

        var sorted = AvailableProfiles.OrderBy(p => p.Name).ToList();
        AvailableProfiles.Clear();
        foreach (var p in sorted)
            AvailableProfiles.Add(p);

        if (ScenarioProfiles.Count > 0)
        {
            var newIdx = Math.Min(idx, ScenarioProfiles.Count - 1);
            SelectedScenarioProfile = ScenarioProfiles[newIdx];
            SelectedScenarioProfileIndex = newIdx;
        }
        else
        {
            SelectedScenarioProfile = null;
            SelectedScenarioProfileIndex = -1;
        }
        OnPropertyChanged(nameof(CanSave));
    }

    public void MoveUp()
    {
        if (SelectedScenarioProfile == null || SelectedScenarioProfileIndex <= 0) return;
        var idx = SelectedScenarioProfileIndex;
        var item = ScenarioProfiles[idx];
        ScenarioProfiles.RemoveAt(idx);
        ScenarioProfiles.Insert(idx - 1, item);
        SelectedScenarioProfileIndex = idx - 1;
        SelectedScenarioProfile = item;
    }

    public void MoveDown()
    {
        if (SelectedScenarioProfile == null || SelectedScenarioProfileIndex < 0 || SelectedScenarioProfileIndex >= ScenarioProfiles.Count - 1) return;
        var idx = SelectedScenarioProfileIndex;
        var item = ScenarioProfiles[idx];
        ScenarioProfiles.RemoveAt(idx);
        ScenarioProfiles.Insert(idx + 1, item);
        SelectedScenarioProfileIndex = idx + 1;
        SelectedScenarioProfile = item;
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ScenarioName))
            return false;
        if (ScenarioProfiles.Count == 0)
            return false;
        if (!_isEditMode || ScenarioName != _originalName)
        {
            if (_existingNames.Contains(ScenarioName))
                return false;
        }
        return true;
    }

    public ScenarioInfo GetResult()
    {
        return new ScenarioInfo
        {
            Name = ScenarioName.Trim(),
            AutoStart = AutoStart,
            IntervalSeconds = IntervalSeconds,
            ProfileNames = ScenarioProfiles.Select(p => p.Name).ToList()
        };
    }

    public event Action? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Save()
    {
        if (!Validate()) return;
        DialogResult = true;
        RequestClose?.Invoke();
    }

    public void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
