using System.Threading.Channels;
using Dapper;
using Microsoft.Data.Sqlite;
using DbOperations;

class Interactor
{
	readonly string _connectionString = $"Data Source={FileManager.DbPath};Default Timeout=1"; // [0]
	
	internal async Task RunAsync(ChannelReader<DbOperation> operationReader, CancellationToken cToken)
	{
		await foreach (var operation in operationReader.ReadAllAsync())
		{
			while (true)
			{
				if (cToken.IsCancellationRequested)
				{
					this.PrintError($"Skipping the execution of:\n\t{operation}\nbecause cancellation was requested");
					if (operation is GetEvents op)
						op.Cancel();
					break;
				}
				try
				{
					using var connection = new SqliteConnection(_connectionString); // [1]
					connection.Open();
					connection.Execute("PRAGMA synchronous = EXTRA;"); // [2]
					operation.Execute(connection);
					break;
				}
				catch (Exception ex) // [3]
					{ this.PrintError(ex); }
				try
					{ await Task.Delay(TimeSpan.FromSeconds(1), cToken); }
				catch (OperationCanceledException)
					{}
			}
		}
	}
}

/*
	[0]
		The default timeout is 30 seconds:
			https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings#:~:text=The%20default%20value%20is%2030.
		Lowering to 1 seconds makes the Interactors more responsive. The retry loop already covers further retries. 

	[1]
		Interactor.RunAsync(), an asynchronous task, may be executed by different threads during the application life. 
		According to the following, sharing a SQLiteConnection object between thread is undesirable (even though,
		in the current scenario, only one thread at a time would use the connection):
			https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors#:~:text=Although%20SQLite%20supports
		So, I prefer to bound the lifetime of a connection object to a synchronous portion of the method.

	[2]
		I use the default "rollback journal" mode. I set synchronous = EXTRA to ensure that transactions are durable.
		Check https://sqlite.org/pragma.html#pragma_synchronous

	[3]
		The docs of Dapper dont seem to clarify what exceptions may be thrown.
*/