using System.Text.Json;
using Windows.Storage;

namespace TodoApp;

internal enum IssueLoadStatus
{
    Loaded,
    FirstRun,
    Migrated,
    RecoveredBackup,
    RecoveredWithSamples
}

internal sealed record IssueLoadResult(
    IReadOnlyList<IssueItem> Issues,
    IssueLoadStatus Status,
    string? PreservedFileName = null,
    Exception? Error = null);

internal sealed class IssueRepository
{
    private const string IssuesFileName = "issues.json";
    private const string BackupFileName = "issues.backup.json";
    private const string TemporaryFileName = "issues.pending.json";
    private const string LegacyTodosFileName = "todos.json";

    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _saveSync = new();
    private Task _saveTail = Task.CompletedTask;

    public async Task<IssueLoadResult> LoadAsync()
    {
        var folder = ApplicationData.Current.LocalFolder;
        if (await folder.TryGetItemAsync(IssuesFileName) is StorageFile issueFile)
        {
            try
            {
                return new IssueLoadResult(await ReadIssuesAsync(issueFile), IssueLoadStatus.Loaded);
            }
            catch (Exception exception)
            {
                var preservedFileName = await PreserveCorruptFileAsync(issueFile);
                var backupIssues = await TryReadBackupAsync();
                if (backupIssues is not null)
                {
                    await TryRepairPrimaryAsync(backupIssues);
                    return new IssueLoadResult(
                        backupIssues,
                        IssueLoadStatus.RecoveredBackup,
                        preservedFileName,
                        exception);
                }

                var sampleIssues = CreateSeedIssues();
                await TryRepairPrimaryAsync(sampleIssues);
                return new IssueLoadResult(
                    sampleIssues,
                    IssueLoadStatus.RecoveredWithSamples,
                    preservedFileName,
                    exception);
            }
        }

        var orphanedBackup = await TryReadBackupAsync();
        if (orphanedBackup is not null)
        {
            await TryRepairPrimaryAsync(orphanedBackup);
            return new IssueLoadResult(orphanedBackup, IssueLoadStatus.RecoveredBackup);
        }

        if (await folder.TryGetItemAsync(LegacyTodosFileName) is StorageFile legacyFile)
        {
            var migratedIssues = await ReadLegacyIssuesAsync(legacyFile);
            await SaveAsync(migratedIssues);
            return new IssueLoadResult(migratedIssues, IssueLoadStatus.Migrated);
        }

        return new IssueLoadResult(CreateSeedIssues(), IssueLoadStatus.FirstRun);
    }

    public Task SaveAsync(IEnumerable<IssueItem> issues)
    {
        var snapshots = issues.Select(IssueSnapshot.FromItem).ToList();
        lock (_saveSync)
        {
            _saveTail = SaveAfterAsync(_saveTail, snapshots);
            return _saveTail;
        }
    }

    private async Task SaveAfterAsync(Task previousSave, IReadOnlyCollection<IssueSnapshot> snapshots)
    {
        try
        {
            await previousSave;
        }
        catch
        {
            // A later save still gets a chance to persist the newest snapshot.
        }

        StorageFile? temporaryFile = null;
        try
        {
            var json = JsonSerializer.Serialize(snapshots, _jsonOptions);
            var folder = ApplicationData.Current.LocalFolder;
            temporaryFile = await folder.CreateFileAsync(
                TemporaryFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(temporaryFile, json);

            if (await folder.TryGetItemAsync(IssuesFileName) is StorageFile existingFile)
            {
                await temporaryFile.MoveAndReplaceAsync(existingFile);
            }
            else
            {
                await temporaryFile.RenameAsync(IssuesFileName, NameCollisionOption.ReplaceExisting);
            }

            temporaryFile = null;
            var savedFile = await folder.GetFileAsync(IssuesFileName);
            try
            {
                await savedFile.CopyAsync(folder, BackupFileName, NameCollisionOption.ReplaceExisting);
            }
            catch
            {
                // The atomic primary commit already succeeded. Keep the previous backup instead of
                // reporting a total save failure that would make the UI roll back committed data.
            }
        }
        finally
        {
            if (temporaryFile is not null)
            {
                try
                {
                    await temporaryFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch
                {
                    // The primary failure is reported to the caller; stale temp cleanup is best effort.
                }
            }

        }
    }

    private static async Task<IReadOnlyList<IssueItem>> ReadIssuesAsync(StorageFile file)
    {
        var json = await FileIO.ReadTextAsync(file);
        var snapshots = JsonSerializer.Deserialize<List<IssueSnapshot>>(json)
            ?? throw new JsonException("The issue data file contained a null document.");
        return snapshots.Select(IssueItem.FromSnapshot).ToList();
    }

    private static async Task<IReadOnlyList<IssueItem>> ReadLegacyIssuesAsync(StorageFile legacyFile)
    {
        var json = await FileIO.ReadTextAsync(legacyFile);
        var todos = JsonSerializer.Deserialize<List<LegacyTodoSnapshot>>(json)
            ?? throw new JsonException("The legacy todo data file contained a null document.");

        var index = 101;
        return todos.Select(todo => new IssueItem(
            string.IsNullOrWhiteSpace(todo.Id) ? Guid.NewGuid().ToString("N") : todo.Id,
            $"TD-{index++}",
            string.IsNullOrWhiteSpace(todo.Title) ? AppResources.Get("UntitledIssue") : todo.Title.Trim(),
            AppResources.Get("MigratedIssueDescription"),
            todo.IsCompleted ? "Done" : "Todo",
            "Medium",
            "Me",
            "Platform",
            string.Empty,
            "migrated")).ToList();
    }

    private static async Task<IReadOnlyList<IssueItem>?> TryReadBackupAsync()
    {
        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(BackupFileName) is StorageFile backupFile)
            {
                return await ReadIssuesAsync(backupFile);
            }
        }
        catch
        {
            // The caller will fall back to preserved data or starter content and surface a warning.
        }

        return null;
    }

    private async Task TryRepairPrimaryAsync(IReadOnlyList<IssueItem> recoveredIssues)
    {
        try
        {
            await SaveAsync(recoveredIssues);
        }
        catch
        {
            // Recovery data remains usable in memory and the preserved corrupt file is untouched.
            // The warning returned to the UI still tells the user that recovery was necessary.
        }
    }

    private static async Task<string?> PreserveCorruptFileAsync(StorageFile issueFile)
    {
        try
        {
            var fileName = $"issues.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            var preservedFile = await issueFile.CopyAsync(
                ApplicationData.Current.LocalFolder,
                fileName,
                NameCollisionOption.GenerateUniqueName);
            return preservedFile.Name;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<IssueItem> CreateSeedIssues()
    {
        return
        [
            new IssueItem(Guid.NewGuid().ToString("N"), "TD-101", AppResources.Get("SeedIssue1Title"), AppResources.Get("SeedIssue1Description"), "InProgress", "High", "Me", "Platform", "2026-06-07", "ux,core"),
            new IssueItem(Guid.NewGuid().ToString("N"), "TD-102", AppResources.Get("SeedIssue2Title"), AppResources.Get("SeedIssue2Description"), "Todo", "Medium", "Design", "Platform", "2026-06-10", "board"),
            new IssueItem(Guid.NewGuid().ToString("N"), "TD-103", AppResources.Get("SeedIssue3Title"), AppResources.Get("SeedIssue3Description"), "Backlog", "Low", "QA", "Ops", "2026-06-14", "release"),
            new IssueItem(Guid.NewGuid().ToString("N"), "TD-104", AppResources.Get("SeedIssue4Title"), AppResources.Get("SeedIssue4Description"), "Review", "High", "Backend", "Platform", "2026-06-06", "data"),
            new IssueItem(Guid.NewGuid().ToString("N"), "TD-105", AppResources.Get("SeedIssue5Title"), AppResources.Get("SeedIssue5Description"), "Done", "Medium", "Me", "Growth", "2026-06-03", "sample")
        ];
    }
}
