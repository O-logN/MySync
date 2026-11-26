using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Dapper;
using MySync.Data.Operations;
using MySync.Utilities;

namespace MySync.Data;

class Interactor(ChannelReader<DbOperation> operationReader)
{
	readonly string _connectionString = $"Data Source={FileUtils.DbPath};Default Timeout=0"; // [0]
	readonly int _maxRetries = 5;
	readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);
	bool _isDead = false; // [1]
	
	public async Task RunAsync()
	{
		await foreach (var operation in operationReader.ReadAllAsync())
		{
			if (!_isDead)
			{
				int i;
				for (i = 0; i < _maxRetries; ++i)
				{
					try
					{
						using var connection = new SqliteConnection(_connectionString); // [2]
						connection.Open();
						connection.Execute("PRAGMA synchronous = EXTRA;"); // [3]
						operation.Execute(connection);
						Console.WriteLine($"Executed: {operation}");
						break;
					}
					catch (Exception e) // [4]
					{
						this.PrintError(ex);
					}
					await Task.Delay(_retryDelay);
				}
				_isDead = i == _maxRetries;
			}
			if (_isDead && operation is GetPaths op)
				op.Cancel();
		}
	}
}

/*
	[0]
		The default timeout is 30 seconds:
			https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings#:~:text=The%20default%20value%20is%2030.
		But I set it to zero because the c# code already handles retries.

    [1]
		It is important that the Interactor can terminate on its own (I avoided a cancellation token for graceful
		shutdown purposes) and that it preserves the order of operations (it cant just try executing the next operation
		if it cant execute the current one (check comment [3] in Evaluator.cs)).

	[2]
		Since Interactor.RunAsync() is asynchronous, it may be executed by different threads during the application life. 
		According to the following, sharing a SQLiteConnection object between thread is undesirable (even though, in the
        current scenario, only one thread at a time would use it):
			https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors#:~:text=Although%20SQLite%20supports
		So, I prefer to bound the lifetime of a connection object to a synchronous portion of the method.

	[3]
		I use the default "rollback journal" mode. I set synchronous = EXTRA to ensure that transactions are durable.
		Check https://sqlite.org/pragma.html#pragma_synchronous

	[4]
		The docs of Dapper dont seem to clarify what exceptions may be thrown. So in doubt, I catch exception.
*/