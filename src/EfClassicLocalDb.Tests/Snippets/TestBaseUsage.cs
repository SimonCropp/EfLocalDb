﻿#if(!NETCOREAPP3_1)

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EfLocalDb;
using Xunit;

namespace TestBase
{
    #region EfClassicTestBase

    public class TestBase
    {
        static SqlInstance<TheDbContext> sqlInstance;

        static TestBase()
        {
            sqlInstance = new(
                constructInstance: connection => new(connection));
        }

        public Task<SqlDatabase<TheDbContext>> LocalDb(
            string? databaseSuffix = null,
            [CallerMemberName] string memberName = "")
        {
            return sqlInstance.Build(GetType().Name, databaseSuffix, memberName);
        }
    }

    public class Tests :
        TestBase
    {
        [Fact]
        public async Task Test()
        {
            using var database = await LocalDb();
            TheEntity entity = new()
            {
                Property = "prop"
            };
            await database.AddData(entity);

            Assert.Single(database.Context.TestEntities);
        }
    }

    #endregion
}
#endif