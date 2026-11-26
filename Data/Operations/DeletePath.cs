using Dapper;
using Microsoft.Data.Sqlite;

namespace MySync.Data.Operations;

public record DeletePath(long Id) : DbOperation
{
	public override void Execute(SqliteConnection connection)
		=> connection.Execute("DELETE FROM Paths WHERE id=@Id;", new {Id = Id});
}