using static System.IO.WatcherChangeTypes;
using static DbOperations.EventType;

namespace DbOperations;

class Event // [1]
{
	internal long? Id            { get; }
	internal string? OldRelPath  { get; }
	internal string? RelPath     { get; }
	internal EventType EventType { get; }
	
	internal Event(long? id, string? oldRelPath, string? relPath, EventType eventType)
	{
		Id = id;
		OldRelPath = oldRelPath;
		RelPath = relPath;
		EventType = eventType;
	}
	
	internal static Event FromFileSystemEventArgs(FileSystemEventArgs e)
	{
		var eventType = e.ChangeType switch
		{
			Created => Create,
			Changed => Change,
			Deleted => Delete,
			Renamed => Rename,
			_ => throw new InvalidOperationException($"Unrecognized change type {e.ChangeType}") // [2]
		};
		return new(null, e is RenamedEventArgs g ? g.OldName : null, e.Name, eventType);
	}
	
	public override string ToString()
		=> $"Event(Id={Id}, OldRelPath={OldRelPath}, RelPath={RelPath}, EventType={EventType})";
}

/*
	[1]
		This type was introduced to represent a single record within the Events table. Yet, I made the Watcher
		turn a FileSystemEventArgs object into an Event object because that appeared to improve the readability
		and consistency of the code. To this end, Event uses OldRelPath, RelPath and EventType rather than the
		less obvious OldName, Name and ChangeType. So, EventType was introduced for this reason.

	[2]
		https://learn.microsoft.com/en-us/dotnet/api/system.io.watcherchangetypes?view=net-10.0#:~:text=This%20enumeration%20supports%20a%20bitwise%20combination%20of%20its%20member%20values.
*/