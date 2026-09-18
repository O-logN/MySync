using Dapper;
using Microsoft.Data.Sqlite;
using static DbOperations.ActionType;

namespace DbOperations;

class AddPendingAction : DbOperation
{
	readonly string? _oldRelPath;
	readonly string _curRelPath;
	readonly ActionType _actionType;

	internal AddPendingAction(string? oldRelPath, string curRelPath, ActionType actionType)
	{
		if (oldRelPath is null && actionType == Rename)
			throw new ArgumentNullException($"Cannot create an AddPendingAction with oldRelPath=null and actionType=Rename");
		_oldRelPath = oldRelPath;
		_curRelPath = curRelPath;
		_actionType  = actionType;
	}

	internal override void Execute(SqliteConnection connection)
	{
		using var transaction = connection.BeginTransaction(); // [1]
		try
		{
			switch (_actionType)
			{
				case Upload:
					StoreAction(connection, transaction, _curRelPath, Upload);
					break;
				case Delete:
					StoreAction(connection, transaction, _curRelPath, Delete);
					break;
				case Rename:
					StoreAction(connection, transaction, _curRelPath, Upload); // [2]
					StoreAction(connection, transaction, _oldRelPath!, Delete);
					break;
			}
			transaction.Commit();
		}
		catch
		{
			transaction.Rollback(); // [3]
			throw;
		}
	}

	static void StoreAction(SqliteConnection connection, SqliteTransaction transaction, string relPath, ActionType actionType)
	{
		connection.Execute(
			"""
			INSERT INTO PendingActions(relPath, actionType, numAction)
			VALUES(@relPath, @actionType, (SELECT n FROM ActionCounter))
			ON CONFLICT(relPath) DO UPDATE SET actionType=@actionType, numAction=(SELECT n FROM ActionCounter);
			""",
			new {relPath=relPath, actionType=actionType.ToString()}, transaction:transaction
		);
		connection.Execute("UPDATE ActionCounter SET n = n+1;", transaction:transaction); // [4]
	}

	public override string ToString()
		=> $"AddPendingAction(oldPath={_oldRelPath}, curPath={_curRelPath}, actionType={_actionType})";
}

/*
	[1]
		Suppose that we didnt use a transaction. The Interactor starts the execution of an "AddPendingAction";
		the upsert successfully introduces an action X in the db, but before the update starts a power loss occurs.
		The program is restarted. The Uploader starts executing X. Then, the Evaluator decides that an action Y
		on the same path should be stored. It gets stored. Then the Uploader finishes the execution of X, and thus
		requests its removal from the db. Since Y reuses the same actionCounter of X, it gets mistakenly deleted.
		A transaction (and the already present sequential order in accessing the db) ensures that no two actions
		can receive the same actionCounter value.
	
	[2]
		Executing an upload first, and a deletion second, ensures that the drive always contains a copy
		of the content of that folder.

	[3]
		https://dappertutorial.net/transaction#:~:text=%E2%9A%A0%EF%B8%8F-,Warning,-%3A%20Don%E2%80%99t%20assume%20that

	[4]
		According to https://sqlite.org/datatype3.html, an INTEGER is signed and can take up to 8 bytes (64 bits).
		1 bit is reserved for the sign, so the biggest value that the type can represent is 2^63-1 (*).
		We have 2^63-1 = 9223372036854775807. Now suppose that the program is used the first time on day x
		(i.e. the day the db is created). Suppose that on day x the program executes StoreAction() 10 million times.
		If the method were executed 100 times per second, every second of the day, then (on that day) it would be called
		a total of 100*60*60*24 = 8.64*10^6 times. Also suppose that the same amount of calls is performed every day
		from day x to 100 years later. The total number of calls is upper bounded by 10^7*366*100 = 366000000000.
		That is just 366000000000 / 9223372036854775807 * 100 = (3.97 * 10^-6)% of the values that the value in
		ActionCounter can possibly take. So, since this program represents an utility that is supposed to be run on
		your own computer, the risk of ActionCounter's value overflowing is effectively zero.

		* In binary, the biggest value is "1" repeated 63 times. That is 2^0 + ... + 2^62 in decimal.
		  It can be proved that 2^0 + ... + 2^62 = 2^63-1. By induction, considering some natural number n:
		  - base case: 2^0 = 2^1-1 is true
		  - inductive step:
				assuming that:
					2^0 + ... + 2^x = 2^{x+1}-1
				we have that:
					2^0 + ... + 2^{x+1} = 2^{x+2}-1
				in fact:
					2^0 + ... + 2^{x+1} =
					2^0 + ... + 2^{x} + 2^{x+1} =
					(2^{x+1}-1) + 2^{x+1} =
					2*2^{x+1} - 1 =
					2^{x+2} - 1
*/