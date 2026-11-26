using Dapper;
using Microsoft.Data.Sqlite;

namespace MySync.Data.Operations;

public record GetPaths(TaskCompletionSource<PathEntry[]> Tcs) : DbOperation
{
	public override void Execute(SqliteConnection connection)
		=> Tcs.SetResult(connection.Query<PathEntry>("SELECT id, relPath FROM Paths ORDER BY id;").ToArray());

	internal void Cancel()
		=> Tcs.SetCanceled();
}