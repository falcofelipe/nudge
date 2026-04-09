using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nudge.Config;

/// <summary>
/// Manages loading, saving, and hot-reloading the Nudge configuration file.
/// Watches the config file for changes and raises an event when the config is updated.
/// </summary>
public class ConfigManager : IDisposable
{
    private readonly string _configPath;
    private readonly FileSystemWatcher _watcher;
    private NudgeConfig _config;
    private bool _disposed;

    // Debounce file change events (editors often trigger multiple writes)
    private DateTime _lastReloadTime = DateTime.MinValue;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Raised when the configuration file is reloaded due to external changes.
    /// </summary>
    public event EventHandler<NudgeConfig>? ConfigReloaded;

    /// <summary>
    /// The current loaded configuration.
    /// </summary>
    public NudgeConfig Config => _config;

    public ConfigManager(string configPath)
    {
        _configPath = Path.GetFullPath(configPath);
        _config = new NudgeConfig();

        // Set up file system watcher for hot-reload
        var directory = Path.GetDirectoryName(_configPath)!;
        var fileName = Path.GetFileName(_configPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnConfigFileChanged;
    }

    /// <summary>
    /// Loads the configuration from disk. Creates a default config file if it doesn't exist.
    /// </summary>
    public NudgeConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            _config = CreateDefaultConfig();
            Save();
            return _config;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<NudgeConfig>(json, JsonOptions) ?? new NudgeConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Failed to load config: {ex.Message}");
            // Keep current config if reload fails
            if (_config == null!)
            {
                _config = new NudgeConfig();
            }
        }

        return _config;
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            // Temporarily disable the watcher to avoid triggering a reload from our own save
            _watcher.EnableRaisingEvents = false;

            var json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        finally
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// Returns the path to the configuration file.
    /// </summary>
    public string GetConfigPath() => _configPath;

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: ignore rapid successive events
        var now = DateTime.UtcNow;
        if (now - _lastReloadTime < DebounceInterval)
            return;

        _lastReloadTime = now;

        // Small delay to let the editor finish writing
        Task.Delay(200).ContinueWith(_ =>
        {
            try
            {
                Load();
                ConfigReloaded?.Invoke(this, _config);
                System.Diagnostics.Debug.WriteLine("[Nudge] Config reloaded from disk.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Nudge] Config reload failed: {ex.Message}");
            }
        });
    }

    private static NudgeConfig CreateDefaultConfig()
    {
        return new NudgeConfig
        {
            GlobalSettings = new GlobalSettings
            {
                PollingIntervalMs = 1000,
                DefaultTrackingMode = "foreground",
                LogUsageData = true,
                DayBoundaryHour = 3,
                RequireExitConfirmation = true
            },
            TrackedApps = new List<TrackedApp>
            {
                new()
                {
                    Name = "Example Game",
                    ProcessNames = new List<string> { "example_game" },
                    TrackingMode = "foreground",
                    Enabled = false,
                    Schedule = new AppSchedule
                    {
                        Default = new DaySchedule
                        {
                            WarningMilestones = new List<WarningMilestone>
                            {
                                new() { AfterMinutes = 30, Type = "toast", Message = "You've been playing for 30 minutes." },
                                new() { AfterMinutes = 60, Type = "toast", Message = "1 hour mark reached!" },
                                new() { AfterMinutes = 90, Type = "modal", Message = "90 minutes - consider taking a break." }
                            },
                            AutoClose = new AutoCloseConfig
                            {
                                Enabled = false,
                                AfterMinutes = 120,
                                PreCloseWarningMinutes = 5,
                                GracefulClose = true
                            }
                        }
                    }
                }
            }
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnConfigFileChanged;
            _watcher.Dispose();
            _disposed = true;
        }
    }
}
