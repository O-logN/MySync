using System.Threading.Channels;
using DbOperations;
using static DbOperations.EventType;

class Evaluator
{
	internal async Task RunAsync(ChannelReader<Event> eventReader, ChannelWriter<DbOperation> operationWriter, CancellationToken cToken)
	{
		var whitelist = await TryReadWhitelistAsync(cToken) ?? [];
		await foreach (var e in eventReader.ReadAllAsync())
		{
			string? skipCause = "";
			if (cToken.IsCancellationRequested)
				skipCause += $"on {e.RelPath}" + "\n" + "because cancellation was requested";
			if (e.RelPath is null) // [1]
				skipCause += "\nbecause its relPath is null";
			if (e.EventType == Rename && e.OldRelPath is null)
				skipCause += "\nbecause its oldRelPath is null";
			if (skipCause != "")
			{
				this.PrintError($"Skipping the execution of {e.EventType} {skipCause}");
				continue;
			}
		
			if (e.RelPath == "whitelist.txt")
				whitelist = await TryReadWhitelistAsync(cToken) ?? [];

			var tmp = e;
			if (!whitelist.Contains(e.RelPath!))
			{
				if (e.EventType == Rename && whitelist.Contains(e.OldRelPath!))
					tmp = new Event(null, null, e.OldRelPath, Delete);
				else
					continue;
			}
			else
			{
				if (e.EventType == Change)
				{
					if (Directory.Exists(Path.Combine(FileManager.SyncPath, e.RelPath!))) // [2]
						continue;
					else if (!File.Exists(Path.Combine(FileManager.SyncPath, e.RelPath!)))
					{
						this.PrintError($"Could not determine if {e.RelPath} is a directory or a file");
						continue;
					}
				}
			}
			operationWriter.TryWrite(new AddEvent(tmp)); // [3]
		}
	}
	
	async Task<string[]?> TryReadWhitelistAsync(CancellationToken cToken)
	{
		try
		{
			return (await FileManager.ReadFileAsync(Path.Combine(FileManager.SyncPath, "whitelist.txt"), cToken))
					.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		}
		catch (Exception ex)
			{ this.PrintError(ex); }
		return null;
	}		
}

/*
	[1]
		"e" is an Event object built through a FileSystemEventArgs object. The latter might have the "Name" property
		(which represents the path at which the filesystem event occurred, relative to the directory being monitored)
		set to null:
			https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemeventargs.name?view=net-10.0#:~:text=The%20Name%20property%20may%20be%20null%20for%20renamed%20events%20if%20the%20FileSystemWatcher%20does%20not%20get%20matching%20old%20and%20new%20name%20events%20from%20the%20OS.
		and the reasons are not so clear. An inspection of the source code (accessible through the linked page)
		suggests that using the "FullPath" property would not be a solution.
	
	[2]
		A change over a directory is a change in its content (not metadata). I dont think that is a relevant event.
		Note that the existence of another event is implied (since the content of the directory changed), and that one
		may be accepted.

	[3]
		This TryWrite() cannot fail (in the Main, we complete the operationWriter after the Evaluator terminates).
*/