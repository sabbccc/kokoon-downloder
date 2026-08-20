using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kokoon.UI.Settings;

/// <summary>
/// Contract for loading and persisting application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current in-memory settings snapshot.</summary>
    AppSettings Current { get; }

    /// <summary>Loads settings from disk, applying defaults for missing properties.</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the current settings to disk.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Gets a setting value by projecting from the current settings.</summary>
    T Get<T>(Func<AppSettings, T> selector);

    /// <summary>Mutates a setting and immediately persists to disk.</summary>
    Task SetAsync(Action<AppSettings> mutator, CancellationToken ct = default);
}

/// <summary>
/// JSON file-backed implementation of <see cref="ISettingsService"/>.
/// Settings are stored at <c>%APPDATA%\KokoonDownloader\settings.json</c>.
/// Thread-safe: all mutations go through a single-writer lock.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AppSettings _settings = new();

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KokoonDownloader", "settings.json");
    }

    /// <inheritdoc />
    public AppSettings Current => _settings;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Settings file not found at {Path}; using defaults", _filePath);
                _settings = new AppSettings();

                // Ensure the default save path directory exists.
                Directory.CreateDirectory(_settings.DefaultSavePath);
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();

            _logger.LogInformation("Settings loaded from {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", _filePath);
            _settings = new AppSettings();
        }

        // Ensure the default save path directory exists.
        try
        {
            Directory.CreateDirectory(_settings.DefaultSavePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create default save path {Path}", _settings.DefaultSavePath);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);

            _logger.LogDebug("Settings saved to {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public T Get<T>(Func<AppSettings, T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return selector(_settings);
    }

    /// <inheritdoc />
    public async Task SetAsync(Action<AppSettings> mutator, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        mutator(_settings);
        await SaveAsync(ct).ConfigureAwait(false);
    }
}
