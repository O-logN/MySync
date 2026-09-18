using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using DbOperations;

class Interactor
{
	readonly string _connectionString;

	internal Interactor()
		=> _connectionString = $"Data Source={FileManager.GetDbPath()};Default Timeout=1"; // [0]
	
	internal async Task RunAsync(ChannelReader<DbOperation> operationReader, CancellationToken cToken)
	{
		await foreach (var operation in operationReader.ReadAllAsync())
		{
			while (true)
			{
				if (cToken.IsCancellationRequested)
				{
					Logger.PrintError("Interactor.RunAsync()",
									 $"Cancellation requested, skipping the execution of:\t{operation}");
					if (operation is GetPendingActions)
						((GetPendingActions)operation).Tcs.SetCanceled();
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
				{
					Logger.PrintError("Interactor.RunAsync()", ex);
				}
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
		I change the default timeout so that an operation executed through "connection" cannot block the Interactor
		for much time. The loop allows retries anyway. I dont use ExecuteAsync() or similar because the Dapper docs
		seem incomplete: for instance, I cant find how to pass a cancellationToken to that method.

	[1]
		Interactor.RunAsync(), an asynchronous task, may be executed by different threads during the application life. 
		According to the following, sharing a SQLiteConnection object between thread is undesirable (even though,
		in the current scenario, only one thread at a time would use the connection):
			https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors#:~:text=Although%20SQLite%20supports
		So, I prefer to bound the lifetime of a connection object to a synchronous portion of the method.

	[2]
		I use the default "rollback journal" mode (choosing WAL implies understanding its tradeoffs, which doesnt
		seem to be worth the effort here). In order to ensure that transactions are durable,
		I then set synchronous to Extra. Check https://sqlite.org/pragma.html#pragma_synchronous

	[3]
		The docs of Dapper dont seem to clarify what exceptions may be thrown.
*/