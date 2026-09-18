using System.IO;
using static System.IO.WatcherChangeTypes;
using System.Threading.Channels;

internal class Watcher : IDisposable
{
	readonly FileSystemWatcher _watcher;

	internal Watcher()
		=> _watcher = new FileSystemWatcher(FileManager.GetSyncPath());

	internal void Stop() 
		=> _watcher.EnableRaisingEvents = false;

	public void Dispose()
		=> _watcher.Dispose();

	internal void Start(ChannelWriter<FileSystemEventArgs> eventWriter)
	{
		void Helper(object sender, FileSystemEventArgs e) // [4]
			=> OnNotError(sender, e, eventWriter);
        _watcher.Created += Helper;
		_watcher.Changed += Helper;
        _watcher.Deleted += Helper;
        _watcher.Renamed += Helper; // [1]
        _watcher.Error += OnError;
		_watcher.IncludeSubdirectories = true;
		_watcher.EnableRaisingEvents = true;
	}

	void OnNotError(object sender, FileSystemEventArgs e, ChannelWriter<FileSystemEventArgs> eventWriter)
	{
		if (!eventWriter.TryWrite(e)) // [2]
		{
			if (e is RenamedEventArgs)
				Logger.PrintError("Watcher.OnNotError()",
								 $"TryWrite() of {e.ChangeType} from {((RenamedEventArgs)e).OldFullPath} to {e.FullPath} failed");
			else
				Logger.PrintError("Watcher.OnNotError()", $"TryWrite() of {e.ChangeType} on {e.FullPath} failed");
		}
		
	}

	void OnError(object sender, ErrorEventArgs e) // [3]
	{
		Stop();
		Logger.PrintError("Watcher.OnError()", e.GetException());
		Logger.PrintInfo(null, "Watcher stopped");
	}
}

/*
	[1]
		The (only) monitored filesystem events are renames, creations, deletions and content changes.

	[2]
		It is theoretically possible that OnNotError() is called but does not attempt the TryWrite()
		before eventWriter is completed. This said, note the following. This is a synchronization program,
		so it is legit to expect that the user closes it *after* any program that may change some whitelisted
		file inside Sync. By the time the user will enter "q" on the console, I then suppose that any prior
		event that he caused will have been processed (since the computer is much faster than the human).

	[3]
		The _watcher raises an error when "it is no longer able to monitor for changes",
		or when its internal buffer overflows, so it seems legit to stop it.

	[4]
		This is just to capture eventWriter.
*/