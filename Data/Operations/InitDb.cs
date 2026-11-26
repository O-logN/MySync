using Dapper;
using Microsoft.Data.Sqlite;

namespace MySync.Data.Operations;

public record InitDb : DbOperation
{
	public override void Execute(SqliteConnection connection)
	{
		connection.Execute(
			"""
			CREATE TABLE IF NOT EXISTS Paths (
				id INTEGER PRIMARY KEY AUTOINCREMENT,
				relPath TEXT NOT NULL
			);
			"""
		); // [1] , [2]
	}
}

/*
	[1]
		"id" possible values are {1, ..., 9223372036854775807}; no value is ever re-used, and at each insertion
		a value bigger than the max in the table is used (unless the max in the table is 9223372036854775807).
		It seems highly unrealistic that the table will ever contain 9223372036854775807. By default, SQLite increments
		the value of "id" by 1; a bigger increment may occur if an error happens. However, suppose that at day x this
		program is used for the first time, and that it is used (every day) until the end of day y, which occurs
		100 years later. Also suppose that within each day, "id" increases of 10 millions. By the end of day y
		the value of "id" would be smaller than 10^7 * 366 * 100 = 366000000000, which is only 3.97 * 10^-6 percent
		of 9223372036854775807. Check: https://www.sqlite.org/autoinc.html
*/