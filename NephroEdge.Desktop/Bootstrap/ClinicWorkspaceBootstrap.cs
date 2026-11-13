using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Persistence_Unit.Drivers;
using Persistence_Unit.Interfaces;
using Persistence_Unit.Repositories;
using Services_Unit.DependencyInjection;
using Services_Unit.SyncBridge;

namespace NephroEdge.Desktop.Bootstrap;

/// <summary>
/// Bootstraps the NephroEdge Doctor Workstation DI container, 3NF SQLite + LiteDB stores,
/// optional initial Mendeley real-cohort seeding (only when empty), and embedded LocalSyncHttpHost.
/// </summary>
public sealed class ClinicWorkspaceBootstrap : IDisposable
{
    public const int DefaultSyncPort = 5050;
    public const string DefaultShiftDate = "2026-11-10";
    public const int DefaultShiftNumber = 1;

    private readonly SemaphoreSlim _hostLock = new(1, 1);
    private LocalSyncHostOptions _syncHostOptions;
    private int _disposed;

    public string WorkspaceDirectory { get; }
    public string SqliteDbPath { get; }
    public string LiteDbPath { get; }
    public IServiceProvider ServiceProvider { get; }
    public IServiceScopeFactory ScopeFactory => ServiceProvider.GetRequiredService<IServiceScopeFactory>();
    public LocalSyncHttpHost? SyncHost { get; private set; }
    public MendeleySeedReport? InitialSeedReport { get; }
    public string? StartupWarning { get; private set; }

    public ClinicWorkspaceBootstrap(
        string? workspaceDirectory = null,
        bool startSyncHost = true,
        LocalSyncHostOptions? syncHostOptions = null,
        string? mendeleyZipPathOverride = null)
    {
        WorkspaceDirectory = string.IsNullOrWhiteSpace(workspaceDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NephroEdge")
            : Path.GetFullPath(workspaceDirectory);

        Directory.CreateDirectory(WorkspaceDirectory);

        SqliteDbPath = Path.Combine(WorkspaceDirectory, "clinic_edge.sqlite");
        LiteDbPath = Path.Combine(WorkspaceDirectory, "clinic_edge.litedb");

        string? preResolvedZipPath = ResolveMendeleyZipPath(mendeleyZipPathOverride);
        if (File.Exists(SqliteDbPath) && preResolvedZipPath is not null)
        {
            var probeDriver = new DbConnectionFactory(SqliteDbPath);
            bool needsFreshSeed;
            try
            {
                needsFreshSeed = NeedsFreshMendeleySeed(probeDriver);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }

            if (needsFreshSeed)
            {
                DeleteStaleLocalStoreFiles(SqliteDbPath, LiteDbPath);
            }
        }

        _syncHostOptions = syncHostOptions ?? new LocalSyncHostOptions(
            Port: DefaultSyncPort,
            BindMode: LocalSyncBindMode.LocalhostAdbOnly,
            DefaultSessionDate: DefaultShiftDate,
            DefaultShiftNumber: DefaultShiftNumber);

        var services = new ServiceCollection();
        services.AddNephroEdgeBackend(
            sqliteDbPath: SqliteDbPath,
            liteDbPath: LiteDbPath,
            aiOptions: null,
            syncHostOptions: _syncHostOptions);

        ServiceProvider = services.BuildServiceProvider();

        using (var scope = ServiceProvider.CreateScope())
        {
            var schemaBootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapRepository>();
            schemaBootstrap.InitializeRelationalAndNoSqlSchemas(ResolveSqlScriptsDirectory());

            var patientRepo = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
            if (patientRepo.GetTotalPatientCount() == 0)
            {
                string? zipPath = preResolvedZipPath ?? ResolveMendeleyZipPath(mendeleyZipPathOverride);
                if (zipPath is not null)
                {
                    var mendeleyRepo = scope.ServiceProvider.GetRequiredService<MendeleyDatasetRepository>();
                    InitialSeedReport = mendeleyRepo.SeedFromMendeleyZip(
                        zipPath,
                        maxRowsToScan: 500,
                        targetActiveShiftBeds: 12,
                        activeSessionDate: DefaultShiftDate);
                }
            }
        }

        if (startSyncHost)
        {
            TryStartSyncHost(_syncHostOptions);
        }
    }

    public async Task RestartSyncHostAsync(LocalSyncBindMode bindMode)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _hostLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            LocalSyncHttpHost? previousHost = SyncHost;
            LocalSyncHostOptions previousOptions = previousHost?.Options ?? _syncHostOptions;
            SyncHost = null;

            if (previousHost is not null)
            {
                await Task.Run(previousHost.Dispose).ConfigureAwait(false);
            }

            LocalSyncHostOptions nextOptions = previousOptions with { BindMode = bindMode };
            LocalSyncHttpHost nextHost = ServiceProvider.CreateLocalSyncHttpHost(nextOptions);

            try
            {
                nextHost.Start();
                _syncHostOptions = nextOptions;
                SyncHost = nextHost;
                StartupWarning = null;
            }
            catch (HttpListenerException ex)
            {
                nextHost.Dispose();
                StartupWarning = $"Sync bridge could not bind {bindMode} on port {nextOptions.Port}: {ex.Message}";
                TryRestorePreviousSyncHost(previousOptions, bindMode);
                throw new InvalidOperationException(
                    $"Failed to bind sync host in {bindMode} mode on port {nextOptions.Port}: {ex.Message}",
                    ex);
            }
            catch (SocketException ex)
            {
                nextHost.Dispose();
                StartupWarning = $"Sync bridge socket error on port {nextOptions.Port}: {ex.Message}";
                TryRestorePreviousSyncHost(previousOptions, bindMode);
                throw new InvalidOperationException(
                    $"Failed to bind sync host in {bindMode} mode on port {nextOptions.Port}: {ex.Message}",
                    ex);
            }
        }
        finally
        {
            _hostLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            SyncHost?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed during teardown.
        }
        finally
        {
            SyncHost = null;
        }

        if (ServiceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }

        _hostLock.Dispose();
    }

    private void TryStartSyncHost(LocalSyncHostOptions options)
    {
        LocalSyncHttpHost host = ServiceProvider.CreateLocalSyncHttpHost(options);
        try
        {
            host.Start();
            SyncHost = host;
            StartupWarning = null;
        }
        catch (HttpListenerException ex)
        {
            host.Dispose();
            SyncHost = null;
            StartupWarning = $"Sync bridge standby on port {options.Port}: {ex.Message}";
        }
        catch (SocketException ex)
        {
            host.Dispose();
            SyncHost = null;
            StartupWarning = $"Sync bridge standby on port {options.Port}: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            host.Dispose();
            SyncHost = null;
            StartupWarning = $"Sync bridge standby on port {options.Port}: {ex.Message}";
        }
    }

    private void TryRestorePreviousSyncHost(LocalSyncHostOptions previousOptions, LocalSyncBindMode failedMode)
    {
        if (previousOptions.BindMode == failedMode)
        {
            return;
        }

        LocalSyncHttpHost fallbackHost = ServiceProvider.CreateLocalSyncHttpHost(previousOptions);
        try
        {
            fallbackHost.Start();
            _syncHostOptions = previousOptions;
            SyncHost = fallbackHost;
        }
        catch (HttpListenerException)
        {
            fallbackHost.Dispose();
        }
        catch (SocketException)
        {
            fallbackHost.Dispose();
        }
    }

    private static string? ResolveSqlScriptsDirectory()
    {
        string baseScripts = Path.Combine(AppContext.BaseDirectory, "SqlScripts");
        if (Directory.Exists(baseScripts))
        {
            return baseScripts;
        }

        foreach (string root in EnumerateSearchRoots())
        {
            string candidate = Path.Combine(root, "backend", "Persistence_Unit", "SqlScripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            string directCandidate = Path.Combine(root, "Persistence_Unit", "SqlScripts");
            if (Directory.Exists(directCandidate))
            {
                return directCandidate;
            }
        }

        return null;
    }

    private static string? ResolveMendeleyZipPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        string[] staticCandidates =
        [
            @"D:\Study\Programming\Projects\Grad_project\knowledge-base\datasets\dataset_11.csv.zip",
            @"D:\Study\Programming\Projects\Grad_project\backend\Tests_Unit\Test_Data\7kmtsmsgfw-1\dataset_11.csv.zip"
        ];

        foreach (string candidate in staticCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] relativeCandidates =
        [
            Path.Combine("knowledge-base", "datasets", "dataset_11.csv.zip"),
            Path.Combine("backend", "Tests_Unit", "Test_Data", "7kmtsmsgfw-1", "dataset_11.csv.zip"),
            Path.Combine("Tests_Unit", "Test_Data", "7kmtsmsgfw-1", "dataset_11.csv.zip")
        ];

        foreach (string root in EnumerateSearchRoots())
        {
            foreach (string relative in relativeCandidates)
            {
                string fullPath = Path.Combine(root, relative);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    internal static bool NeedsFreshMendeleySeed(DbConnectionFactory dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);

        const string hashPattern = "%#%";
        const int minExpectedPatients = 12;
        const int minExpectedShiftBeds = 12;

        try
        {
            using var connection = dbFactory.CreateOpenConnection();

            long hashNameCount = connection.ExecuteScalar<long>(
                """
                SELECT COUNT(1)
                FROM Patients
                WHERE COALESCE(FirstName, '') LIKE @HashPattern
                   OR COALESCE(MiddleName, '') LIKE @HashPattern
                   OR COALESCE(LastName, '') LIKE @HashPattern;
                """,
                new { HashPattern = hashPattern });

            if (hashNameCount > 0)
            {
                return true;
            }

            long totalPatients = connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Patients;");

            if (totalPatients < minExpectedPatients)
            {
                return true;
            }

            long distinctShiftPatients = connection.ExecuteScalar<long>(
                """
                SELECT COUNT(DISTINCT PatientId)
                FROM vw_ActiveShiftSessionBoard
                WHERE SessionDate = @SessionDate
                  AND ShiftNumber = @ShiftNumber;
                """,
                new
                {
                    SessionDate = DefaultShiftDate,
                    ShiftNumber = DefaultShiftNumber
                });

            if (distinctShiftPatients < minExpectedShiftBeds)
            {
                return true;
            }

            long shiftHashNameCount = connection.ExecuteScalar<long>(
                """
                SELECT COUNT(1)
                FROM vw_ActiveShiftSessionBoard
                WHERE SessionDate = @SessionDate
                  AND ShiftNumber = @ShiftNumber
                  AND COALESCE(PatientFullName, '') LIKE @HashPattern;
                """,
                new
                {
                    SessionDate = DefaultShiftDate,
                    ShiftNumber = DefaultShiftNumber,
                    HashPattern = hashPattern
                });

            return shiftHashNameCount > 0;
        }
        catch (SqliteException ex) when (
            (ex.SqliteErrorCode == 1 && (
                ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))) ||
            ex.SqliteErrorCode is 11 or 26)
        {
            // Stale/incomplete pre-migration schema or corrupt local SQLite file: trigger safe purge & reseed.
            return true;
        }
    }

    private void DeleteStaleLocalStoreFiles(string sqliteDbPath, string liteDbPath)
    {
        string[] candidateFiles =
        [
            sqliteDbPath,
            sqliteDbPath + "-wal",
            sqliteDbPath + "-shm",
            liteDbPath,
            liteDbPath + "-log"
        ];

        foreach (string path in candidateFiles)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                StartupWarning = $"Unable to purge stale local cache file '{Path.GetFileName(path)}': {ex.Message}";
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string seed in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(seed));
            for (int depth = 0; depth < 8 && current is not null; depth++)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }
}
