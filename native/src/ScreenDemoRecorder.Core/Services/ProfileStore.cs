using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly JsonSerializerOptions ImportJsonOptions = CreateJsonOptions(JsonUnmappedMemberHandling.Disallow);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string? previousStorageDirectory;
    private ProfileDocument document = new();

    public ProfileStore(string? settingsPath = null, string? legacySettingsPath = null)
    {
        var portableDirectory = AppContext.BaseDirectory;
        SettingsPath = settingsPath ?? Path.Combine(portableDirectory, "settings-v2.json");
        LegacySettingsPath = legacySettingsPath ?? Path.Combine(portableDirectory, "settings.json");
        if (settingsPath is null && legacySettingsPath is null)
            previousStorageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Screen Demo Recorder");
    }

    public string SettingsPath { get; }

    public string LegacySettingsPath { get; }

    public string ActiveProfileName => document.ActiveProfile;

    public IReadOnlyList<string> ProfileNames => document.Profiles.Keys.ToArray();

    public IReadOnlyList<string> RecentFiles => document.RecentFiles.ToArray();

    public RecorderProfile GetActiveProfile()
    {
        return Clone(document.Profiles[document.ActiveProfile]);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MigratePreviousStorage();
            if (File.Exists(SettingsPath))
            {
                var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
                document = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions)
                    ?? throw new InvalidDataException("The settings file is empty.");
                document = ProfileValidator.Normalize(document);
                return;
            }

            if (File.Exists(LegacySettingsPath))
            {
                var legacyJson = await File.ReadAllTextAsync(LegacySettingsPath, cancellationToken).ConfigureAwait(false);
                document = LegacySettingsMigrator.Migrate(legacyJson);
                BackupLegacySettings();
                await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            document = new ProfileDocument();
            document = ProfileValidator.Normalize(document);
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!document.Profiles.ContainsKey(name))
            {
                throw new KeyNotFoundException($"Unknown profile: {name}.");
            }

            document.ActiveProfile = name;
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateActiveAsync(RecorderProfile profile, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(document.ActiveProfile, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(string name, RecorderProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var snapshot = ProfileValidator.Normalize(Clone(profile));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!document.Profiles.ContainsKey(name))
            {
                throw new KeyNotFoundException($"Unknown profile: {name}.");
            }
            var previous = document.Profiles[name];
            document.Profiles[name] = snapshot;
            try { await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false); }
            catch { document.Profiles[name] = previous; throw; }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> DuplicateAsync(string requestedName, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var name = UniqueName(requestedName);
            document.Profiles[name] = Clone(document.Profiles[document.ActiveProfile]);
            document.ActiveProfile = name;
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return name;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RenameActiveAsync(string requestedName, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var name = requestedName.Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("Profile name must not be empty.", nameof(requestedName));
            }

            if (document.Profiles.ContainsKey(name) && !string.Equals(name, document.ActiveProfile, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A profile named '{name}' already exists.");
            }

            var profile = document.Profiles[document.ActiveProfile];
            document.Profiles.Remove(document.ActiveProfile);
            document.Profiles[name] = profile;
            document.ActiveProfile = name;
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteActiveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (document.Profiles.Count == 1)
            {
                throw new InvalidOperationException("The last profile cannot be deleted.");
            }

            document.Profiles.Remove(document.ActiveProfile);
            document.ActiveProfile = document.Profiles.Keys.First();
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetActiveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = document.Profiles[document.ActiveProfile];
            document.Profiles[document.ActiveProfile] = new RecorderProfile();
            try { await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false); }
            catch { document.Profiles[document.ActiveProfile] = previous; throw; }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ExportActiveAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, Path.GetFullPath(SettingsPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The profile export cannot replace the application settings file.");

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = new ProfileExportDocument
            {
                Name = document.ActiveProfile,
                Profile = Clone(document.Profiles[document.ActiveProfile]),
            };
            var directory = Path.GetDirectoryName(destination)
                ?? throw new InvalidOperationException("The export path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = destination + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, destination, true);
            }
            catch
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var json = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath), cancellationToken).ConfigureAwait(false);
        var (requestedName, importedProfile) = ReadImportedProfile(json);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousActive = document.ActiveProfile;
            var name = UniqueName(requestedName);
            document.Profiles[name] = importedProfile;
            document.ActiveProfile = name;
            try { await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false); }
            catch
            {
                document.Profiles.Remove(name);
                document.ActiveProfile = previousActive;
                throw;
            }
            return name;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddRecentFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = document.RecentFiles.ToList();
            document.RecentFiles = [fullPath, .. document.RecentFiles.Where(item =>
                !string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase))];
            document.RecentFiles = document.RecentFiles.Take(10).ToList();
            try { await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false); }
            catch { document.RecentFiles = previous; throw; }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, SettingsPath, true);
    }

    private void MigratePreviousStorage()
    {
        if (previousStorageDirectory is null || !Directory.Exists(previousStorageDirectory)) return;
        var destinationDirectory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);
        MoveIfPresent("settings-v2.json", SettingsPath);
        MoveIfPresent("settings.json", LegacySettingsPath);
        MoveIfPresent("settings-v1.backup.json", Path.Combine(destinationDirectory, "settings-v1.backup.json"));
        var previousLogs = Path.Combine(previousStorageDirectory, "logs");
        var portableLogs = Path.Combine(destinationDirectory, "logs");
        if (Directory.Exists(previousLogs) && !Directory.Exists(portableLogs))
            Directory.Move(previousLogs, portableLogs);
        if (!Directory.EnumerateFileSystemEntries(previousStorageDirectory).Any())
            Directory.Delete(previousStorageDirectory);

        void MoveIfPresent(string name, string destination)
        {
            var source = Path.Combine(previousStorageDirectory, name);
            if (File.Exists(source) && !File.Exists(destination)) File.Move(source, destination);
        }
    }

    private void BackupLegacySettings()
    {
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(directory, "settings-v1.backup.json");
        if (!File.Exists(backupPath))
        {
            File.Copy(LegacySettingsPath, backupPath);
        }
    }

    private string UniqueName(string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? $"{document.ActiveProfile} copy" : requestedName.Trim();
        var name = baseName;
        var suffix = 2;
        while (document.Profiles.ContainsKey(name))
        {
            name = $"{baseName} {suffix}";
            suffix++;
        }

        return name;
    }

    private static RecorderProfile Clone(RecorderProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        return JsonSerializer.Deserialize<RecorderProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to clone a profile.");
    }

    private static (string Name, RecorderProfile Profile) ReadImportedProfile(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        JsonElement schema = default;
        var hasSchema = root.ValueKind == JsonValueKind.Object &&
            (root.TryGetProperty("schemaVersion", out schema) || root.TryGetProperty("schema_version", out schema));
        if (!hasSchema || !schema.TryGetInt32(out var version))
            throw new InvalidDataException("The profile file does not contain a numeric schemaVersion.");

        if (version == ProfileDocument.CurrentSchemaVersion)
        {
            var payload = JsonSerializer.Deserialize<ProfileExportDocument>(json, ImportJsonOptions)
                ?? throw new InvalidDataException("The profile file is empty.");
            var name = CleanImportedName(payload.Name);
            var profile = payload.Profile ?? throw new InvalidDataException("The profile file does not contain profile settings.");
            return (name, ProfileValidator.Normalize(Clone(profile)));
        }

        if (version == 1)
        {
            if (!root.TryGetProperty("name", out var legacyName) || legacyName.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("profile", out var legacyProfile) || legacyProfile.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The legacy profile file is incomplete.");
            var name = CleanImportedName(legacyName.GetString());
            var profiles = new JsonObject { [name] = JsonNode.Parse(legacyProfile.GetRawText()) };
            var legacyDocument = new JsonObject
            {
                ["schema_version"] = 1,
                ["active_profile"] = name,
                ["profiles"] = profiles,
                ["recent_files"] = new JsonArray(),
            };
            var migrated = LegacySettingsMigrator.Migrate(legacyDocument.ToJsonString());
            return (name, migrated.Profiles[migrated.ActiveProfile]);
        }

        throw new InvalidDataException($"Unsupported profile schema: {version}.");
    }

    private static string CleanImportedName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrEmpty(name) ? "Imported" : name;
    }

    private static JsonSerializerOptions CreateJsonOptions(JsonUnmappedMemberHandling unmappedMemberHandling = JsonUnmappedMemberHandling.Skip)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = unmappedMemberHandling,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
