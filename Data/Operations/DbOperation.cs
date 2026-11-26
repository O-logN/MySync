using Microsoft.Data.Sqlite;

namespace MySync.Data.Operations;

public abstract record DbOperation
{
	public abstract void Execute(SqliteConnection connection);
}