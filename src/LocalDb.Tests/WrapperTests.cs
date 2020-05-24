﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VerifyXunit;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

public class WrapperTests :
    VerifyBase
{
    static Wrapper instance;

    [Fact]
    public Task InvalidInstanceName()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Wrapper(s => new SqlConnection(s), "<", "s"));
        return Verify(exception.Message);
    }

    [Fact(Skip = "no supported")]
    public async Task RecreateWithOpenConnectionAfterStartup()
    {
        /*
could be supported by running the following in wrapper CreateDatabaseFromTemplate
but it is fairly unlikely to happen and not doing the offline saves time in tests

if db_id('{name}') is not null
begin
    alter database [{name}] set single_user with rollback immediate;
    alter database [{name}] set multi_user;
    alter database [{name}] set offline;
end;
         */
        var name = "RecreateWithOpenConnectionAfterStartup";
        LocalDbApi.StopAndDelete(name);
        DirectoryFinder.Delete(name);

        var wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        var connectionString = await wrapper.CreateDatabaseFromTemplate("Simple");
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await wrapper.CreateDatabaseFromTemplate("Simple");

            wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find("RecreateWithOpenConnection"));
            wrapper.Start(timestamp, TestDbBuilder.CreateTable);
            await wrapper.CreateDatabaseFromTemplate("Simple");
        }

        await Verify(await wrapper.ReadDatabaseState("Simple"));
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task RecreateWithOpenConnection()
    {
        var name = "RecreateWithOpenConnection";
        LocalDbApi.StopAndDelete(name);
        DirectoryFinder.Delete(name);

        var wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        var connectionString = await wrapper.CreateDatabaseFromTemplate("Simple");
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
            wrapper.Start(timestamp, TestDbBuilder.CreateTable);
            await wrapper.CreateDatabaseFromTemplate("Simple");
        }

        await Verify(await wrapper.ReadDatabaseState("Simple"));
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task NoFileAndNoInstance()
    {
        var name = "NoFileAndNoInstance";
        LocalDbApi.StopAndDelete(name);
        DirectoryFinder.Delete(name);

        var wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.CreateDatabaseFromTemplate("Simple");
        await Verify(await wrapper.ReadDatabaseState("Simple"));
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task Callback()
    {
        var name = "Callback2";

        var callbackCalled = false;
        var wrapper = new Wrapper(
            s => new SqlConnection(s),
            name,
            DirectoryFinder.Find(name),
            callback: connection =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.CreateDatabaseFromTemplate("Callback_Simple");
        Assert.True(callbackCalled);
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task WithFileAndNoInstance()
    {
        var name = "WithFileAndNoInstance";
        var wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.AwaitStart();
        wrapper.DeleteInstance();
        wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.CreateDatabaseFromTemplate("Simple");
        await Verify(await wrapper.ReadDatabaseState("Simple"));
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task NoFileAndWithInstanceAndNamedDb()
    {
        var instanceName = "NoFileAndWithInstanceAndNamedDb";
        LocalDbApi.StopAndDelete(instanceName);
        LocalDbApi.CreateInstance(instanceName);
        DirectoryFinder.Delete(instanceName);
        var wrapper = new Wrapper(s => new SqlConnection(s), instanceName, DirectoryFinder.Find(instanceName));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.AwaitStart();
        await wrapper.CreateDatabaseFromTemplate("Simple");

        Thread.Sleep(3000);
        DirectoryFinder.Delete(instanceName);

        wrapper = new Wrapper(s => new SqlConnection(s), instanceName, DirectoryFinder.Find(instanceName));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.AwaitStart();
        await wrapper.CreateDatabaseFromTemplate("Simple");

        await Verify(await wrapper.ReadDatabaseState("Simple"));
    }

    [Fact]
    public async Task NoFileAndWithInstance()
    {
        var name = "NoFileAndWithInstance";
        LocalDbApi.StopAndDelete(name);
        LocalDbApi.CreateInstance(name);
        DirectoryFinder.Delete(name);
        var wrapper = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        wrapper.Start(timestamp, TestDbBuilder.CreateTable);
        await wrapper.AwaitStart();
        await wrapper.CreateDatabaseFromTemplate("Simple");
        await Verify(await wrapper.ReadDatabaseState("Simple"));
        LocalDbApi.StopInstance(name);
    }

    [Fact]
    public async Task DeleteDatabase()
    {
        await instance.CreateDatabaseFromTemplate("ToDelete");
        await instance.DeleteDatabase("ToDelete");
        await Verify(await instance.ReadDatabaseState("ToDelete"));
    }

    [Fact]
    public async Task DefinedTimestamp()
    {
        var name = "DefinedTimestamp";
        var instance2 = new Wrapper(s => new SqlConnection(s), name, DirectoryFinder.Find(name));
        var dateTime = DateTime.Now;
        instance2.Start(dateTime, connection => Task.CompletedTask);
        await instance2.AwaitStart();
        Assert.Equal(dateTime, File.GetCreationTime(instance2.DataFile));
    }

    [Fact]
    public Task WithRebuild()
    {
        var instance2 = new Wrapper(s => new SqlConnection(s), "WrapperTests", DirectoryFinder.Find("WrapperTests"));
        instance2.Start(timestamp, connection => throw new Exception());
        return instance2.AwaitStart();
    }

    [Fact]
    public async Task CreateDatabase()
    {
        await instance.CreateDatabaseFromTemplate("CreateDatabase");
        await Verify(await instance.ReadDatabaseState("CreateDatabase"));
    }

    [Fact]
    public async Task DeleteDatabaseWithOpenConnection()
    {
        var name = "ToDelete";
        var connectionString = await instance.CreateDatabaseFromTemplate(name);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await instance.DeleteDatabase(name);
        var deletedState = await instance.ReadDatabaseState(name);
        await instance.CreateDatabaseFromTemplate(name);
        var createdState = await instance.ReadDatabaseState(name);
        await Verify(new
        {
            deletedState,
            createdState
        });
    }

    public WrapperTests(ITestOutputHelper output) :
        base(output)
    {
    }

    static DateTime timestamp = new DateTime(2000, 1, 1);

    static WrapperTests()
    {
        LocalDbApi.StopAndDelete("WrapperTests");
        instance = new Wrapper(s => new SqlConnection(s), "WrapperTests", DirectoryFinder.Find("WrapperTests"));
        instance.Start(timestamp, TestDbBuilder.CreateTable);
        instance.AwaitStart().GetAwaiter().GetResult();
    }
}