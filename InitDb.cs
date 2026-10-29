using Dapper;
using Microsoft.Data.Sqlite;

namespace DbOperations;

class InitDb : DbOperation
{
	internal override void Execute(SqliteConnection connection)
	{
		connection.Execute(
			"""
			CREATE TABLE IF NOT EXISTS Events (
				id INTEGER PRIMARY KEY AUTOINCREMENT,
				oldRelPath TEXT,
				relPath TEXT NOT NULL,
				eventType TEXT NOT NULL
			);
			"""
		); // [1] , [2] , [3]
	}

	public override string ToString()
		=> "InitDb()";
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
	
	[2]
		It is important to preserve the order in which records are inserted in this table. Consider what would happen
		if a file (let it be a.txt) is created and then deleted, but the order in which the Uploader would receive
		(NULL, "a.txt", Deleted) and (NULL, "a.txt", Created) was random. The "id" attribute serves this purpose.
		It is also useful to let the Uploader identify the row that it wants to delete.

	[3]
		For brevity, I skip adding constraints like:
			- eventType must be either Create, Change, Delete or Rename;
			- if eventType is "Rename", then oldRelPath must not be null;
*/