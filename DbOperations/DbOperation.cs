using Microsoft.Data.Sqlite;

namespace DbOperations;

abstract class DbOperation // [1]
{
	internal abstract void Execute(SqliteConnection connection);
}

/*
	[1]
		During the execution of the program, the Evaluator may decide that a subfolder should be uploaded* on
		the drive. If this happens, then it asks the Interactor to store this information (call it X) in the db.
		X becomes useless only in two cases:
			1. the requested action has been successfully executed on the drive
			2. the Evaluator later decides that the subfolder should instead get deleted** from the drive
		Each case verifies an unbound amount of time after the Evaluator produced X; for instance, the user might be
		offline. Until then X should not be lost (but it might, consider a computer freeze or a power loss).
		The use of a db allows to reduce the duration of time windows from "unbounded" to "within a few hundreds ms"
		(assuming we can actually carry out the db write in at most a few retries).
		*  -> or deleted, or renamed
		** -> or uploaded if X is "delete it", and so on
*/