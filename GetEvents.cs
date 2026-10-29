using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class GetEvents(TaskCompletionSource<Event[]> tcs) : DbOperation
{
	internal override void Execute(SqliteConnection connection)
		=> tcs.SetResult(connection.Query<Event>("SELECT * FROM Events ORDER BY id;").ToArray());

	internal void Cancel()
		=> tcs.SetCanceled();

	public override string ToString()
		=> "GetEvents()";
}