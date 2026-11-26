using System.Threading.Channels;
using MySync.Data.Operations;
using MySync.Utilities;
using static System.IO.WatcherChangeTypes;

namespace MySync.Monitoring;

class Evaluator valutare l'uso di TryGetTargetKind
{
	readonly RulesManager _rulesManager;
	readonly ChannelReader<FileSystemEventArgs> _eventReader;
	readonly ChannelWriter<DbOperation> _operationWriter;

	Evaluator(RulesManager rulesManager, ChannelReader<FileSystemEventArgs> eventReader, ChannelWriter<DbOperation> operationWriter)
	{
		_rulesManager = rulesManager;
		_eventReader = eventReader;
		_operationWriter = operationWriter;
	}

	internal async static Task<Evaluator?> TryCreateAsync(ChannelReader<FileSystemEventArgs> eventReader, ChannelWriter<DbOperation> operationWriter)
	{
		var rulesManager = await RulesManager.TryCreateAsync();
		return rulesManager is not null ? new Evaluator(rulesManager, eventReader, operationWriter) : null;
	}

	internal async Task RunAsync()
	{
		bool isDead = false;
        await foreach (var e in _eventReader.ReadAllAsync())
		{
			if (isDead)
				continue;
			if (e.FullPath == FileUtils.RulesPath && !await _rulesManager.TryReloadAsync())
			{
				isDead = true;
				continue;
			}
			if (!IsMalformedEvent(e))
				HandleEvent(e);
		}
    }

	bool IsMalformedEvent(FileSystemEventArgs e)
	{
		string s = "";
		if (e.Name is null)
			s += "\n\t-its Name is null";
		if (e is RenamedEventArgs g && g.OldName is null)
			s += "\n\t-its OldName is null";
		if (e.ChangeType is not (Created or Changed or Deleted or Renamed)) // [1]
			s += "\n\t-its ChangeType is unrecognized";
        if (s != "")
		{
            LogUtils.PrintError($"Discarding the event:\n\t{e.AsString()}\nbecause:{s}");
			return true;
		}
		return false;
	}

	void HandleEvent(FileSystemEventArgs e)
	{
		switch (e.ChangeType)
		{
			case Created:
				HandleCreated(e.Name!, e.FullPath);
				break;
			case Changed:
				HandleChanged(e.Name!, e.FullPath);
				break;
			case Deleted:
				HandleDeleted(e.Name!);
				break;
			case Renamed:
				HandleCreated(e.Name!, e.FullPath); // [3]
				HandleDeleted(((RenamedEventArgs)e).OldName!);
				break;
		}
	}

	void HandleCreated(string relPath, string path)
	{
		if (!_rulesManager.IsAllowed(relPath))
			return;
		if (File.Exists(path))
			_operationWriter.TryWrite(new AddPath(relPath));
		else if (Directory.Exists(path))
			WalkDirectory(path);
		else if (Path.Exists(path))
			LogUtils.PrintError($"Could not determine if {path} is a file or a directory");
	}

	void HandleChanged(string relPath, string path)
	{
		if (!_rulesManager.IsAllowed(relPath))
			return;d
		if (File.Exists(path))
			_operationWriter.TryWrite(new AddPath(relPath));
		else if (!Directory.Exists(path) && Path.Exists(path)) // [2]
			LogUtils.PrintError($"Could not determine if {path} is a file or a directory");
	}

	void HandleDeleted(string relPath)
	{
		if (!_rulesManager.IsAllowed(relPath))
			return;
		_operationWriter.TryWrite(new AddPath(relPath));
	}

	void WalkDirectory(string path)
	{
		bool hasAnAllowedChild = false; // [4]
		foreach (var child in new DirectoryInfo(path).EnumerateFileSystemInfos())
		{
			var relPath = child.FullName[(FileUtils.SyncPath.Length+1)..];
			if (_rulesManager.IsAllowed(relPath))
			{
				hasAnAllowedChild = true;
				if (child is DirectoryInfo)
					WalkDirectory(child.FullName);
				else
					_operationWriter.TryWrite(new AddPath(relPath));
			}
		}
		if (!hasAnAllowedChild)
			_operationWriter.TryWrite(new AddPath(path[(FileUtils.SyncPath.Length+1)..]));
	}
}

/*
	[1]
		https://learn.microsoft.com/en-us/dotnet/api/system.io.watcherchangetypes?view=net-10.0#:~:text=This%20enumeration%20supports%20a%20bitwise%20combination%20of%20its%20member%20values

	[2]
		As a side effect to creating/changing/deleting/renaming a file/directory inside some directory D,
		a Changed event over D may be triggered. Indeed, we do not care of it.

	[3]
		Writing the new path before the old path allows to first upload the new version and then delete the old one.
	
	[4]
		...
*/