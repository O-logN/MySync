namespace MySync.Utilities;

public static class FileUtils
{	// [1]
	public static string SyncPath { get; } = Path.Combine(GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sync");
	public static string MySyncPath { get; } = Path.Combine(GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MySync");
	public static string RulesPath { get; } = Path.Combine(SyncPath, "rules.txt");
	public static string DbPath { get; } = Path.Combine(MySyncPath, "db.db");
	public static string SignaturePath { get; } = Path.Combine(MySyncPath, "signature.txt");
	public static string CacheFileName { get; } = "cache.dat";
	
	public enum TargetKind { File, Directory, None };

	public static TargetKind? TryGetTargetKind(string path) // [3]
	{
        try
        {
            var fileAttributes = File.GetAttributes(path);
			return fileAttributes.HasFlag(FileAttributes.Directory) ? TargetKind.Directory : TargetKind.File;
        }
		catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
		{
			return TargetKind.None;
		}
		catch (Exception e)
		{
			LogUtils.PrintError(e);
			return null;
		}
	}

	public static async Task<string?> TryReadFileAsync(string path)
	{
		using var fs = await TryOpenFileAsync(path, FileMode.Open, FileAccess.Read);
		if (fs is null) return null;
		try
		{
			using var reader = new StreamReader(fs);
			return reader.ReadToEnd();
		}
		catch (IOException e)
		{
			LogUtils.PrintError($"Cannot read \"{Path.GetFileName(path)}\": {e.Message}");
			return null;
		}
	}

	public static async Task<FileStream?> TryOpenFileAsync(string path, FileMode fileMode, FileAccess fileAccess, CancellationToken cToken = default)
	{
		const int maxRetries = 5;
		var retryDelay = TimeSpan.FromMilliseconds(500);
		for (int i = 0; i < maxRetries; i++)
		{
			try
			{
				return File.Open(path, fileMode, fileAccess);
			}
			catch (IOException e) when (((e.HResult & 0x0000FFFF) == 32) || ((e.HResult & 0x0000FFFF) == 33)) // [2]
			{
				if (!await TaskEx.TryDelay(retryDelay, cToken))
					return null;
			}
			catch (Exception e)
			{
				LogUtils.PrintError(e);
				return null;
			}
		}
		LogUtils.PrintError($"Couldn\'t open {path} using {fileMode} fileMode and {fileAccess} fileAccess despite {maxRetries} attempts");
		return null;
	}

	static string GetFolderPath(Environment.SpecialFolder folder)
	{
		var folderPath = Environment.GetFolderPath(folder);
		return folderPath != "" ? folderPath : throw new InvalidOperationException($"{folder} does not physically exist.");
	}
}

/*
	[1]
		These properties get initialized only when access to one is being requested. Some of those initializations
		may throw an InvalidOperationException. If something triggers the initializations, and one throws one such
		exception, then that something will throw a TypeInitializationException encapsulating that exception.

	[2]
		Another process is using the file. Relevant:
			https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors
			https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
	
	[3]
		If File.Exists(path) and Directory.Exists(path) both return false, then either path identified nothing
		on the filesystem, or an error occurred. I dont see how to rapidly distinguish between the two, therefore
		I've moved to the current approach. For consistency with how I use the "TryDoSomething()" pattern, I make
		the method return null if an error happens (rather than introducing a fourth enum value).
*/