﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlClient;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
// ReSharper disable RedundantCast

namespace EfLocalDb
{
    public class SqlInstance<TDbContext>
        where TDbContext : DbContext
    {
        internal Wrapper Wrapper { get; }
        ConstructInstance<TDbContext> constructInstance;
        static Storage DefaultStorage;

        static SqlInstance()
        {
            var name = typeof(TDbContext).Name;
            DefaultStorage = new(name, DirectoryFinder.Find(name));
        }

        public string ServerName => Wrapper.ServerName;

        public SqlInstance(
            ConstructInstance<TDbContext> constructInstance,
            TemplateFromContext<TDbContext>? buildTemplate = null,
            Storage? storage = null,
            string? uniqueness = null,
            ushort templateSize = 3,
            ExistingTemplate? existingTemplate = null,
            Callback<TDbContext>? callback = null):
            this(
                constructInstance,
                BuildTemplateConverter.Convert(constructInstance, buildTemplate),
                storage,
                GetUniqueness(uniqueness, buildTemplate),
                templateSize,
                existingTemplate,
                callback)
        {
        }

        public SqlInstance(
            ConstructInstance<TDbContext> constructInstance,
            TemplateFromConnection buildTemplate,
            Storage? storage = null,
            string? uniqueness = null,
            ushort templateSize = 3,
            ExistingTemplate? existingTemplate = null,
            Callback<TDbContext>? callback = null)
        {
            storage ??= DefaultStorage;

            var resultUniqueness = GetUniqueness(uniqueness, buildTemplate);
            Guard.AgainstNull(nameof(constructInstance), constructInstance);
            this.constructInstance = constructInstance;

            var storageValue = storage.Value;
            DirectoryCleaner.CleanInstance(storageValue.Directory);

            Func<DbConnection, Task>? wrapperCallback = null;
            if (callback != null)
            {
                wrapperCallback = async connection =>
                {
                    using var context = constructInstance(connection);
                    await callback(connection, context);
                };
            }
            Wrapper = new(
                s => new SqlConnection(s),
                storageValue.Name,
                storageValue.Directory,
                templateSize,
                existingTemplate,
                wrapperCallback);
            Wrapper.Start(resultUniqueness, connection => buildTemplate(connection));
        }

        static string GetUniqueness(string? uniqueness, Delegate? buildTemplate)
        {
            if (uniqueness != null)
            {
                return uniqueness;
            }

            if (buildTemplate != null)
            {
                return Timestamp.LastModified(buildTemplate).ToUniqueString();
            }

            return Timestamp.LastModified<TDbContext>().ToUniqueString();
        }

        public void Cleanup() => Wrapper.DeleteInstance();

        Task<string> BuildDatabase(string dbName)
        {
            return Wrapper.CreateDatabaseFromTemplate(dbName);
        }

        /// <summary>
        ///   Build DB with a name based on the calling Method.
        /// </summary>
        /// <param name="data">The seed data.</param>
        /// <param name="testFile">The path to the test class. Used to make the db name unique per test type.</param>
        /// <param name="databaseSuffix">For Xunit theories add some text based on the inline data to make the db name unique.</param>
        /// <param name="memberName">Used to make the db name unique per method. Will default to the caller method name is used.</param>
        public Task<SqlDatabase<TDbContext>> Build(
            IEnumerable<object>? data,
            [CallerFilePath] string testFile = "",
            string? databaseSuffix = null,
            [CallerMemberName] string memberName = "")
        {
            Guard.AgainstNullWhiteSpace(nameof(testFile), testFile);
            Guard.AgainstNullWhiteSpace(nameof(memberName), memberName);
            Guard.AgainstWhiteSpace(nameof(databaseSuffix), databaseSuffix);

            var testClass = Path.GetFileNameWithoutExtension(testFile);

            var dbName = DbNamer.DeriveDbName(databaseSuffix, memberName, testClass);
            return Build(dbName, data);
        }

        /// <summary>
        ///   Build DB with a name based on the calling Method.
        /// </summary>
        /// <param name="testFile">The path to the test class. Used to make the db name unique per test type.</param>
        /// <param name="databaseSuffix">For Xunit theories add some text based on the inline data to make the db name unique.</param>
        /// <param name="memberName">Used to make the db name unique per method. Will default to the caller method name is used.</param>
        public Task<SqlDatabase<TDbContext>> Build(
            [CallerFilePath] string testFile = "",
            string? databaseSuffix = null,
            [CallerMemberName] string memberName = "")
        {
            return Build(null, testFile, databaseSuffix, memberName);
        }

        public async Task<SqlDatabase<TDbContext>> Build(
            string dbName,
            IEnumerable<object>? data)
        {
            Guard.AgainstNullWhiteSpace(nameof(dbName), dbName);
            var connection = await BuildDatabase(dbName);
            SqlDatabase<TDbContext> database = new(
                connection,
                dbName,
                constructInstance,
                () => Wrapper.DeleteDatabase(dbName),
                data);
            await database.Start();
            return database;
        }

        public Task<SqlDatabase<TDbContext>> Build(string dbName)
        {
            return Build(dbName, (IEnumerable<object>?) null);
        }

        public string MasterConnectionString => Wrapper.MasterConnectionString;
    }
}