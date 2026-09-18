using System.IO;
using static System.IO.WatcherChangeTypes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DbOperations;
using static DbOperations.ActionType;

class Evaluator
{
	readonly string _syncPath;
	
	internal Evaluator()
		=> _syncPath = FileManager.GetSyncPath();
	
	internal async Task RunAsync(ChannelReader<FileSystemEventArgs> eventReader,
								 ChannelWriter<DbOperation> operationWriter, CancellationToken cToken)
	{
		await foreach (var e in eventReader.ReadAllAsync())
		{
			if (e.Name is null) // [1]
			{
				Logger.PrintError(
					"Evaluator.RunAsync()",
					$"Got an event with Name set to null (FullPath={e.FullPath}, ChangeType={e.ChangeType})"
				);
				continue;
			}
			var (topLevelName, rest) = Decompose(e.Name); // [2]
			if (rest is null)
				EvaluateTopLevelEvent(e, operationWriter);
			else
			{
				if (rest == "whitelist.txt")
					EvaluateWhitelistEvent(e, topLevelName, operationWriter);
				else
				{
					try
						{ await EvaluateInnerFileEventAsync(e, topLevelName, operationWriter, cToken); }
					catch (OperationCanceledException)
						{}
				}	
			}
		}
	}

	void EvaluateTopLevelEvent(FileSystemEventArgs e, ChannelWriter<DbOperation> operationWriter)
	{
		if (e.ChangeType != Deleted && !Directory.Exists(e.FullPath)) // [3]
		{
			Logger.PrintError(
				"Evaluator.EvaluateTopLevelEvent()",
				$"Either {e.FullPath} does not identify a directory, or an error occurred while trying to determine so."
			);
			return;
		}
		switch (e.ChangeType)
		{
			case Changed:
				Console.WriteLine("Evaluator.EvaluateTopLevelEvent   rejecting " + e.ChangeType + " on " + e.FullPath);
				return; // [4]
			case Created:
				operationWriter.TryWrite(new AddPendingAction(null, e.Name!, Upload)); // [9]
				break;
			case Renamed:
				var oldName = ((RenamedEventArgs)e).OldName;
				operationWriter.TryWrite(new AddPendingAction(oldName, e.Name!, Rename));
				break;
			case Deleted:
				operationWriter.TryWrite(new AddPendingAction(null, e.Name!, Delete)); 
				break;
			default:
				Logger.PrintError("Evaluator.EvaluateTopLevelEvent",
								 $"Unexpected change type \"{e.ChangeType}\" at {e.FullPath}");
				break;
		}
	}

	void EvaluateWhitelistEvent(FileSystemEventArgs e, string topLevelName, ChannelWriter<DbOperation> operationWriter)
	{
		switch (e.ChangeType)
		{
			case Created: // [5]
			case Changed:
			case Renamed: // [6]
			case Deleted:
				operationWriter.TryWrite(new AddPendingAction(null, topLevelName, Upload));
				break;
			default:
				Logger.PrintError("Evaluator.EvaluateWhitelistEvent()",
								 $"Unexpected change type \"{e.ChangeType}\" at {e.FullPath}");
				break;
		}
	}

	async Task EvaluateInnerFileEventAsync(FileSystemEventArgs e, string topLevelName,
										   ChannelWriter<DbOperation> operationWriter, CancellationToken cToken)
	{
		switch (e.ChangeType)
		{
			case Created:
			case Changed:
			case Deleted:
				var content = await FileManager.TryReadWhitelistAsync(topLevelName, cToken) ?? "";
				var whitelist = content.Split(["\r\n", "\n" ], StringSplitOptions.RemoveEmptyEntries);
				if (Array.Exists(whitelist, x => x == e.Name))
					operationWriter.TryWrite(new AddPendingAction(null, topLevelName, Upload));
				else // solo per testing
					Console.WriteLine("Evaluator.EvaluateInnerFileEventAsync   rejecting " + e.ChangeType + " on " + e.FullPath);
				break;
			case Renamed:
				var tmp = (RenamedEventArgs)e;
				if (tmp.OldName is not null && Decompose(tmp.OldName).rest == "whitelist.txt") // [7]
					operationWriter.TryWrite(new AddPendingAction(null, topLevelName, Upload));
				goto case Created; // [8]
			default:
				Logger.PrintError("Evaluator.EvaluateInnerFileEventAsync()",
								 $"Unexpected change type \"{e.ChangeType}\" at {e.FullPath}");
				break;
		}
	}
	
	(string topLevelName, string? rest) Decompose(string relPath)
	{
		int i = relPath.IndexOf("\\"); 
		return (i != -1 ? (relPath.Substring(0,i), relPath.Substring(i+1)) : (relPath, null));
	}
}

/*
	[1]
		The Name property contains the path at which the event occurred, relative to the path of the directory
		that is being watched. It is nullable:
			https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemeventargs.name?view=net-10.0#:~:text=The%20Name%20property%20may%20be%20null%20for%20renamed%20events%20if%20the%20FileSystemWatcher%20does%20not%20get%20matching%20old%20and%20new%20name%20events%20from%20the%20OS.
		and the reasons are not so clear. Anyway, if it is null then we cant do anything. Using FullPath would not solve
		the issue as its unreliable (as inspecting the source code linked by the above page suggests).
	
	[2]
		If Name contains something like "A\B\C", then ("A", "B\C") is obtained.
		if Name contains something like "A", then ("A", null) is obtained.

	[3]
		I dont expect events regarding files in the top level of Sync, but they may erroneously occur.
		So, I try to filter them out. However, idk how to understand whether a "delete" event concerns a file.
		Checking for the file extension is not sufficient, as not all files have it. Anyway, note that if the Uploader
		attempts the deletion of a file, then it will receive a 404 not found unless something other than this program
		created one such file (with .zip as the extension) on the drive.

	[4]
		A "changed" event on a directory implies that either its metadata or content changed.
		If the metadata changed, then we should ignore the event as we dont care about such changes.
		If the content changed, then a create/change/delete/rename must have happened in the top level of the directory.
		This implies that another filesystem event has been generated. So, ignoring the directory event is safe even
		in this case. Not only "safe", it is also "beneficial". Suppose that the "other filesystem event" concerns
		a non-whitelisted file; we would filter that event out, but if we didnt ignore the directory event and thus
		required a directory upload, then we would be requiring a useless upload.

	[5]
		A test shows that using the "mv" command on a non-empty file to transfer it from directory A to directory B
		produces a "Created" event inside B without any further "Changed" event. One more reason not to ignore the
		creation event.

	[6]
		This is a "Renamed" event where the new file name is "whitelist.txt"
	
	[7]
		Basically a deletion of the whitelist file.
	
	[8]
		Now evaluate the new filepath.
	
	[9]
		We know by RunAsync() that this cannot be null.
*/