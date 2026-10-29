using System.Threading.Channels;
using DbOperations;

class Watcher : IDisposable
{
	readonly FileSystemWatcher _watcher = new(FileManager.SyncPath); // [1]

	internal void Start(ChannelWriter<Event> eventWriter)
	{
		void tmp(object sender, FileSystemEventArgs e) => OnNotError(sender, e, eventWriter); // [2]
        _watcher.Created += tmp;
		_watcher.Changed += tmp;
        _watcher.Deleted += tmp;
        _watcher.Renamed += tmp;
        _watcher.Error += OnError;
		_watcher.IncludeSubdirectories = true;
		_watcher.EnableRaisingEvents = true;
	}

	internal void Stop()
		=> _watcher.EnableRaisingEvents = false;

	public void Dispose()
		=> _watcher.Dispose();

	void OnNotError(object sender, FileSystemEventArgs e, ChannelWriter<Event> eventWriter)
	{
		Event? ev;
		try
			{ ev = Event.FromFileSystemEventArgs(e); }
		catch (InvalidOperationException ex)
			{ this.PrintError(ex); return; }
		if (!eventWriter.TryWrite(ev)) // [3]
			this.PrintError($"TryWrite() of {ev.EventType} from {ev.OldRelPath} to {ev.RelPath} failed");
	}

	void OnError(object sender, ErrorEventArgs e) // [4]
	{
		Stop();
		this.PrintError(e.GetException());
	}
}

/*
	[1]
		After the execution of Start(), _watcher monitors Sync or any of its subfolders for creations, content
		changes, deletions or renames. Any other event is ignored.

	[2]
		_watcher.Created += <something> is allowed only if the param list of <something> is in the form:
			(object sender, FileSystemEventArgs e) 
		Yet I need the invoked method to use the eventWriter; so I use this local method to capture it.

	[3]
		It is theoretically possible that OnNotError() is called but does not attempt the TryWrite()
		before eventWriter is completed. This said, note the following. This is a synchronization program,
		so it is legit to expect that the user closes it *after* any program that may change some whitelisted
		file inside Sync. By the time the user will enter "q" on the console, I then suppose that any prior
		event that he caused will have been processed (since the computer is much faster than the human).

	[4]
		The _watcher raises an error when "it is no longer able to monitor for changes"
		or when its internal buffer overflows. 
*/