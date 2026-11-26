using System.Threading.Channels;
using MySync.Utilities;

namespace MySync.Monitoring;

class Watcher(ChannelWriter<FileSystemEventArgs> eventWriter) : IDisposable
{
	readonly FileSystemWatcher _fsWatcher = new(FileUtils.SyncPath); // [1]

	internal void Start()
	{
        _fsWatcher.Created += OnNotError;
		_fsWatcher.Changed += OnNotError;
        _fsWatcher.Deleted += OnNotError;
        _fsWatcher.Renamed += OnNotError;
        _fsWatcher.Error += OnError;
		_fsWatcher.InternalBufferSize = 1024*8; // [2]
		_fsWatcher.IncludeSubdirectories = true;
		_fsWatcher.EnableRaisingEvents = true;
	}

	internal void Stop()
		=> _fsWatcher.EnableRaisingEvents = false;

	public void Dispose()
		=> _fsWatcher.Dispose();

	void OnNotError(object sender, FileSystemEventArgs e)
	{
		if (!eventWriter.TryWrite(e)) // [3]
			LogUtils.PrintError($"Failed to TryWrite(): {e.AsString()}");
	}

	void OnError(object sender, ErrorEventArgs e)
	{
		Stop();
		LogUtils.PrintError(e.GetException());
	}
}

/*
	[1]
		Once started, it monitors the referenced folder (and each of its subfolders) for creations, content
		changes, deletions, renames, and no other (type of) event.
	
	[2]
		Microsoft warns that the buffer capacity should be kept as low as possible.
		The smallest possible capacity is 4kb. The capacity should be a multiple of 4. Reportings:
			- 4kb capacity: a "dotnet build" caused a buffer overflow
			- 8kb capacity: current choice
		https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.internalbuffersize?view=net-10.0#system-io-filesystemwatcher-internalbuffersize

	[3]
		The eventWriter may be completed before the TryWrite() starts. Indeed, if all OnNotError() calls finish
		before the eventWriter's completion, then that error is impossible. So, if the user does not cause events
		regarding the whitelisted files within a few (milli)seconds before he requires the application shutdown,
		then by that time every OnNotError() will most likely be over.
*/