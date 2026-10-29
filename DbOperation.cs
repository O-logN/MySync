using Microsoft.Data.Sqlite;

namespace DbOperations;

abstract class DbOperation // [1]
{
	internal abstract void Execute(SqliteConnection connection);
}

/*
	[1]
		Any event accepted by the Evaluator is information that should not be lost until the Uploader
		successfully processes it. The amount of time between accepting and successfully processing is indeterminate,
		as no internet connection may be available. If a power loss or computer freeze occurs during this period,
		then the information is lost. Using a db allows to reduce the max amount of time to some milliseconds. 
*/