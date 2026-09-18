using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class GetPendingActions : DbOperation
{
	internal required TaskCompletionSource<PendingAction[]> Tcs { get; init; }

	internal override void Execute(SqliteConnection connection)
	{
		Tcs.SetResult(
			connection.Query<PendingAction>(
				"SELECT relPath, actionType, numAction FROM PendingActions ORDER BY numAction ASC;"
			).ToArray()
		);
	}
}