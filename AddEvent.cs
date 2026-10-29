using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class AddEvent(Event e) : DbOperation
{
	internal override void Execute(SqliteConnection connection)
		=> connection.Execute("INSERT INTO Events(oldRelPath, relPath, eventType) VALUES(@OldRelPath, @RelPath, @EventType);",
							   new {OldRelPath = e.OldRelPath, RelPath=e.RelPath, EventType=e.EventType});

	public override string ToString()
		=> $"AddEvent({e})";
}