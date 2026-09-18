using Dapper;
using Microsoft.Data.Sqlite;
using static DbOperations.ActionType;

namespace DbOperations;

class InitDb : DbOperation
{
	internal override void Execute(SqliteConnection connection)
	{
		CreatePendingActions(connection);
		CreateActionCounter(connection);
		InitializeActionCounter(connection);
	}

	static void CreatePendingActions(SqliteConnection connection)
	{
		connection.Execute(
			$"""
			CREATE TABLE IF NOT EXISTS PendingActions (
				relPath TEXT PRIMARY KEY,
				actionType TEXT NOT NULL CHECK (actionType IN ('{Upload}', '{Delete}')),
				numAction INTEGER NOT NULL
			);
			"""
		); // [1]
	}
	
	static void CreateActionCounter(SqliteConnection connection)
	{
		connection.Execute(
			"""
			CREATE TABLE IF NOT EXISTS ActionCounter (
				n INTEGER NOT NULL
			);
			"""
		);
	}
	
	static void InitializeActionCounter(SqliteConnection connection)
	{
		connection.Execute(
			"""
			INSERT INTO ActionCounter
				SELECT 0 WHERE 0 = (SELECT COUNT(*) FROM ActionCounter);
			"""
		); // [2]
	}
}

/*
	[1]
		numAction serves two purposes:
			- Persisting the order in which inserts/updates are executed.
			  This is useful for successfully implementing the "rename" action as an "upload" action followed by
			  a "delete" action. Check AddPendingAction.cs.
			- Preventing the Uploader from deleting an action that is newer than the one it just completed.
			  Suppose that this deletion attempt didnt consider numAction, then it is possible that:
				- the Uploader starts uploading the zip of folder A
				- the Evaluator decides that a (new) upload action should be executed on A
				- the Uploader completes the upload and deletes the row from the table
			  In this scenario, the choice of the Evaluator is lost.

	[2]
		Inserts 0 in ActionCounter if the table is empty, does nothing otherwise.
*/