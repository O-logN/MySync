using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class DeleteEvent(Event e) : DbOperation
{
	internal override void Execute(SqliteConnection connection)
		=> connection.Execute("DELETE FROM Events WHERE id=@id;", new {id = e.Id!});

	public override string ToString()
		=> $"DeleteEvent({e})";
}