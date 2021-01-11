﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LocalDb;
using Xunit;

public class Tests
{
    [Fact]
    public async Task Simple()
    {
        SqlInstance instance = new("Name", TestDbBuilder.CreateTable);

        await using var database = await instance.Build();
        var connection = database.Connection;
        var data = await TestDbBuilder.AddData(connection);
        Assert.Contains(data, await TestDbBuilder.GetData(connection));
    }

    [Fact]
    public async Task Callback()
    {
        var callbackCalled = false;
        SqlInstance instance = new(
            "Tests_Callback",
            TestDbBuilder.CreateTable,
            callback: _ =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        await using var database = await instance.Build();
        Assert.True(callbackCalled);
    }

    //[Fact]
    //public async Task SuppliedTemplate()
    //{
    //    // The template has been pre-created with 2 test entities
    //    var templatePath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "suppliedTemplate.mdf");
    //    var logPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "suppliedTemplate_log.ldf");

    //    var myInstance = new SqlInstance("SuppliedTemplate", TestDbBuilder.CreateTable, templatePath: templatePath, logPath: logPath);
    //    await using var database = await myInstance.Build();
    //    var connection = database.Connection;
    //    //var data = await TestDbBuilder.AddData(connection);
    //    //Assert.Contains(data, await TestDbBuilder.GetData(connection));
    //}

    [Fact]
    public async Task Defined_Uniqueness()
    {
        var dateTime = DateTime.Now;
        SqlInstance instance = new(
            name: "Defined_TimeStamp",
            buildTemplate: TestDbBuilder.CreateTable,
            uniqueness: "Defined_TimeStamp");

        await using var database = await instance.Build();
        Assert.Equal("Defined_Uniqueness", await File.ReadAllTextAsync(instance.Wrapper.UniquenessFile));
    }

    [Fact]
    public async Task Delegate_TimeStamp()
    {
        SqlInstance instance = new(
            name: "Delegate_TimeStamp",
            buildTemplate: TestDbBuilder.CreateTable);

        await using var database = await instance.Build();
        Assert.Equal(Timestamp.LastModified<Tests>().ToUniqueString(), await File.ReadAllTextAsync(instance.Wrapper.UniquenessFile));
    }

    [Fact]
    public async Task WithRollback()
    {
        SqlInstance instance = new("Name", TestDbBuilder.CreateTable);

        await using var database1 = await instance.BuildWithRollback();
        await using var database2 = await instance.BuildWithRollback();
        var data = await TestDbBuilder.AddData(database1.Connection);
        Assert.Contains(data, await TestDbBuilder.GetData(database1.Connection));
        Assert.Empty(await TestDbBuilder.GetData(database2.Connection));
    }

    [Fact]
    public async Task WithRollbackPerf()
    {
        SqlInstance instance = new("Name", TestDbBuilder.CreateTable);

        await using (await instance.BuildWithRollback())
        {
        }

        SqlDatabaseWithRollback? database = null;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            database = await instance.BuildWithRollback();
            Trace.WriteLine(stopwatch.ElapsedMilliseconds);
            await TestDbBuilder.AddData(database.Connection);
        }
        finally
        {
            var stopwatch = Stopwatch.StartNew();
            database?.Dispose();
            Trace.WriteLine(stopwatch.ElapsedMilliseconds);
        }
    }

    [Fact]
    public async Task Multiple()
    {
        var stopwatch = Stopwatch.StartNew();
        SqlInstance instance = new("Multiple", TestDbBuilder.CreateTable);

        await using (var database = await instance.Build(databaseSuffix: "one"))
        {
        }

        await using (var database = await instance.Build(databaseSuffix: "two"))
        {
        }

        await using (var database = await instance.Build(databaseSuffix: "three"))
        {
        }

        Trace.WriteLine(stopwatch.ElapsedMilliseconds);
    }
}