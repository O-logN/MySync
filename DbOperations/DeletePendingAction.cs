using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class DeletePendingAction(string relPath, long numAction) : DbOperation
{
	internal override void Execute(SqliteConnection connection) // [1]
	{
		connection.Execute(
			"DELETE FROM PendingActions WHERE relPath=@relPath AND numAction=@numAction;",
			new {relPath=relPath, numAction=numAction}
		);
	}

	public override string ToString()
		=> $"DeletePendingAction(relPath={relPath}, numAction={numAction})";
}
	
/*
	[1]
		Check [1] in InitDb.cs
*/