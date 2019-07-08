# Design

There is a tiered approach to the API.

For SQL:

SqlInstance > SqlDatabase > Connection

For EF:

SqlInstance > SqlDatabase > EfContext

SqlInstance represents a [SQL Sever instance](https://docs.microsoft.com/en-us/sql/database-engine/configure-windows/database-engine-instances-sql-server?#instances) (in this case hosted in LocalDB) and SqlDatabase represents a [SQL Sever Database](https://docs.microsoft.com/en-us/sql/relational-databases/databases/databases?view=sql-server-2017) running inside that SqlInstance.

From a API perspective:

For SQL:

`SqlInstance` > `SqlDatabase` > `SqlConnection`

For EF:

`SqlInstance<TDbContext>` > `SqlDatabase<TDbContext>` > `TDbContext`


Multiple SqlDatabases can exist inside each SqlInstance. Multiple DbContexts/SqlConnections can be created to talk to a SqlDatabase.

On the file system, each SqlInstance has corresponding directory and each SqlDatabase has a uniquely named mdf file within that directory.

When a SqlInstance is defined, a template database is created. All subsequent SqlDatabases created from that SqlInstance will be based on this template. The template allows schema and data to be created once, instead of every time a SqlDatabase is required. This results in improved performance by not requiring to re-create/re-migrate the SqlDatabase schema/data on each use.

The usual approach for consuming the API in a test project is as follows.

 * Single SqlInstance per test project.
 * Single SqlDatabase per test (or instance of a parameterized test).
 * One or more DbContexts/SqlConnections used within a test.

This assumes that there is a schema and data (and DbContext in the EF context) used for all tests. If those caveats are not correct then multiple SqlInstances can be used.

As the most common usage scenario is "Single SqlInstance per test project" there is a simplified static API to support it. To take this approach use `EFLocalDb.SqlInstanceService<TDbContext>` or `LocalDb.SqlInstanceService`.


## Template database size

When doing a [create database](https://docs.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql) that new database is created based on the [model database](https://docs.microsoft.com/en-us/sql/relational-databases/databases/model-database). See [The model Database and Creating New Databases](https://docs.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql#the-model-database-and-creating-new-databases). When [defining a size](https://docs.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql#arguments) for a new database, that size is ignored if it is smaller than the model database size:

> The size specified for the primary file must be at least as large as the primary file of the model database.

Since the model database is 8MB, the default (and smallest) size for any new database is also 8MB. This is not ideal when using LocalDB for unit tests, since a database is created for each test, is means an 8MB file needs to be created for each test, with the resulting cost in IO time and disk usage.

To have a smaller file size [DBCC SHRINKFILE](https://docs.microsoft.com/en-us/sql/t-sql/database-console-commands/dbcc-shrinkfile-transact-sql) is performed on the model database at the time a new LocalDB instance is created. The smallest size allowed is 3MB.

snippet: ShrinkModelDb