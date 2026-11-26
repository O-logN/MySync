using Dapper;
using Microsoft.Data.Sqlite;

namespace MySync.Data.Operations;

public record AddPath(string RelPath) : DbOperation
{
	public override void Execute(SqliteConnection connection)
	{
		connection.Execute("INSERT INTO Paths(relPath) VALUES(@RelPath);", new {RelPath = RelPath});
	}
}