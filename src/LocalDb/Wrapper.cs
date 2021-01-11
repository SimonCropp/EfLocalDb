﻿using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MethodTimer;
#if EF
using EfLocalDb;
#else
using LocalDb;
#endif

class Wrapper
{
    public readonly string Directory;
    ushort size;
    Func<DbConnection, Task>? callback;
    SemaphoreSlim semaphoreSlim = new(1, 1);
    public readonly string MasterConnectionString;
    public readonly string WithRollbackConnectionString;
    Func<string, DbConnection> buildConnection;
    string instance;
    public readonly string DataFile;
    public readonly string UniquenessFile;
    string LogFile;
    string TemplateConnectionString;
    public readonly string ServerName;
    Task startupTask = null!;
    bool templateProvided;

    public Wrapper(
        Func<string, DbConnection> buildConnection,
        string instance,
        string directory,
        ushort size = 3,
        ExistingTemplate? existingTemplate = null,
        Func<DbConnection, Task>? callback = null)
    {
        Guard.AgainstDatabaseSize(nameof(size), size);
        Guard.AgainstInvalidFileName(nameof(instance), instance);

        LocalDbLogging.WrapperCreated = true;
        this.buildConnection = buildConnection;
        this.instance = instance;
        MasterConnectionString = LocalDbSettings.connectionBuilder(instance, "master");
        TemplateConnectionString = LocalDbSettings.connectionBuilder(instance, "template");
        WithRollbackConnectionString = LocalDbSettings.connectionBuilder(instance, "withRollback");
        Directory = directory;

        LocalDbLogging.LogIfVerbose($"Directory: {directory}");
        this.size = size;
        this.callback = callback;
        if (existingTemplate == null)
        {
            templateProvided = false;
            DataFile = Path.Combine(directory, "template.mdf");
            LogFile = Path.Combine(directory, "template_log.ldf");
        }
        else
        {
            templateProvided = true;
            DataFile = existingTemplate.Value.DataPath;
            LogFile = existingTemplate.Value.LogPath;
        }
        UniquenessFile = Path.Combine(directory, "uniqueness.txt");

        var directoryInfo = System.IO.Directory.CreateDirectory(directory);
        directoryInfo.ResetAccess();

        ServerName = $@"(LocalDb)\{instance}";
    }

    [Time("Name: '{name}'")]
    public async Task<string> CreateDatabaseFromTemplate(string name, bool withRollback = false)
    {
        if (string.Equals(name, "template", StringComparison.OrdinalIgnoreCase))
        {
            throw new("The database name 'template' is reserved.");
        }

        // Explicitly dont take offline here, since that is done at startup
        var dataFile = Path.Combine(Directory, $"{name}.mdf");
        var logFile = Path.Combine(Directory, $"{name}_log.ldf");

        await startupTask;
        File.Copy(DataFile, dataFile, true);
        File.Copy(LogFile, logFile, true);

        FileExtensions.MarkFileAsWritable(dataFile);
        FileExtensions.MarkFileAsWritable(logFile);

        var commandText = SqlBuilder.GetCreateOrMakeOnlineCommand(name, dataFile, logFile, withRollback);

#if NET5_0
        await using var masterConnection = await OpenMasterConnection();
#else
        using var masterConnection = await OpenMasterConnection();
#endif
        await masterConnection.ExecuteCommandAsync(commandText);

        var connectionString = LocalDbSettings.connectionBuilder(instance,name);
        await RunCallback(connectionString);
        return connectionString;
    }

    async Task RunCallback(string connectionString)
    {
        if (callback == null)
        {
            return;
        }

        try
        {
            await semaphoreSlim.WaitAsync();
            if (callback == null)
            {
                return;
            }

#if NET5_0
            await using var connection = buildConnection(connectionString);
#else
            using var connection = buildConnection(connectionString);
#endif
            await connection.OpenAsync();
            await callback(connection);
            callback = null;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    public void Start(string uniqueness, Func<DbConnection, Task> buildTemplate)
    {
#if RELEASE
        try
        {
#endif
        var stopwatch = Stopwatch.StartNew();
        InnerStart(uniqueness, buildTemplate);
        var message = $"Start `{ServerName}` {stopwatch.ElapsedMilliseconds}ms.";

        LocalDbLogging.Log(message);
#if RELEASE
        }
        catch (Exception exception)
        {
            throw ExceptionBuilder.WrapLocalDbFailure(instance, Directory, exception);
        }
#endif
    }

    public Task AwaitStart()
    {
        return startupTask;
    }

    void InnerStart(string uniqueness, Func<DbConnection, Task> buildTemplate)
    {
        void CleanStart()
        {
            FileExtensions.FlushDirectory(Directory);
            LocalDbApi.CreateInstance(instance);
            LocalDbApi.StartInstance(instance);
            startupTask = CreateAndDetachTemplate(
                uniqueness,
                buildTemplate,
                rebuild: true,
                optimize: true);
            InitRollbackTask();
        }

        var info = LocalDbApi.GetInstance(instance);

        if (!info.Exists)
        {
            CleanStart();
            return;
        }

        if (!info.IsRunning)
        {
            LocalDbApi.DeleteInstance(instance);
            CleanStart();
            return;
        }

        if (!File.Exists(DataFile))
        {
            LocalDbApi.StopAndDelete(instance);
            CleanStart();
            return;
        }

        string? templateUniqueness = null;
        if (File.Exists(UniquenessFile))
        {
            templateUniqueness = File.ReadAllText(UniquenessFile);
        }

        if (uniqueness == templateUniqueness)
        {
            LocalDbLogging.LogIfVerbose("Not modified so skipping rebuild");
            startupTask = CreateAndDetachTemplate(uniqueness, buildTemplate, false, false);
        }
        else
        {
            startupTask = CreateAndDetachTemplate(uniqueness, buildTemplate, true, false);
        }

        InitRollbackTask();
    }

    [Time("Uniqueness: '{uniqueness}', Rebuild: '{rebuild}', Optimize: '{optimize}'")]
    async Task CreateAndDetachTemplate(
        string uniqueness,
        Func<DbConnection, Task> buildTemplate,
        bool rebuild,
        bool optimize)
    {
#if NET5_0
        await using var takeOfflineConnection = await OpenMasterConnection();
#else
        using var takeOfflineConnection = await OpenMasterConnection();
#endif
        var takeDbsOffline = takeOfflineConnection.ExecuteCommandAsync(SqlBuilder.TakeDbsOfflineCommand);
#if NET5_0
        await using var masterConnection = await OpenMasterConnection();
#else
        using var masterConnection = await OpenMasterConnection();
#endif

        LocalDbLogging.LogIfVerbose($"SqlServerVersion: {masterConnection.ServerVersion}");

        if (optimize)
        {
            await masterConnection.ExecuteCommandAsync(SqlBuilder.GetOptimizationCommand(size));
        }

        if (rebuild && !templateProvided)
        {
            await Rebuild(uniqueness, buildTemplate, masterConnection);
        }

        await takeDbsOffline;
    }

    async Task<DbConnection> OpenMasterConnection()
    {
        var connection = buildConnection(MasterConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    async Task Rebuild(string uniqueness, Func<DbConnection, Task> buildTemplate, DbConnection masterConnection)
    {
        DeleteTemplateFiles();
        await masterConnection.ExecuteCommandAsync(SqlBuilder.GetCreateTemplateCommand(DataFile, LogFile));

        FileExtensions.MarkFileAsWritable(DataFile);
        FileExtensions.MarkFileAsWritable(LogFile);

#if NET5_0
        await using (var connection = buildConnection(TemplateConnectionString))
#else
        using (var connection = buildConnection(TemplateConnectionString))
#endif
        {
            await connection.OpenAsync();
            await buildTemplate(connection);
        }

        await masterConnection.ExecuteCommandAsync(SqlBuilder.DetachTemplateCommand);

        File.WriteAllText(UniquenessFile, uniqueness);
    }

    [Time]
    public void DeleteInstance()
    {
        LocalDbApi.StopAndDelete(instance);
        System.IO.Directory.Delete(Directory, true);
    }

    void DeleteTemplateFiles()
    {
        File.Delete(DataFile);
        File.Delete(LogFile);
    }

    [Time("dbName: '{dbName}'")]
    public async Task DeleteDatabase(string dbName)
    {
        var commandText = SqlBuilder.BuildDeleteDbCommand(dbName);
#if NET5_0
        await using var connection = await OpenMasterConnection();
#else
        using var connection = await OpenMasterConnection();
#endif
        await connection.ExecuteCommandAsync(commandText);
        var dataFile = Path.Combine(Directory, $"{dbName}.mdf");
        var logFile = Path.Combine(Directory, $"{dbName}_log.ldf");
        File.Delete(dataFile);
        File.Delete(logFile);
    }

    Lazy<Task> withRollbackTask = null!;

    void InitRollbackTask()
    {
        withRollbackTask = new(() => CreateDatabaseFromTemplate("withRollback", true));
    }

    public async Task CreateWithRollbackDatabase()
    {
        await startupTask;
        await withRollbackTask.Value;
    }
}