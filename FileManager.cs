using System.IO;
using System.IO.Compression;
using System.Threading;

static class FileManager
{	
	internal static string GetCacheFileName()
		=> "cache.dat";
	
	internal static string GetSyncPath()
		=> GetDirPath("Sync", Environment.SpecialFolder.MyDocuments);

	internal static string GetMySyncPath()
		=> GetDirPath("MySync", Environment.SpecialFolder.LocalApplicationData);

	internal static string GetDbPath()
		=> Path.Combine(GetMySyncPath(), "db.db");
	
	internal static async Task<string?> TryReadWhitelistAsync(string parentDirName, CancellationToken cToken)
	{
		string path = null!;
		try
			{ path = GetSyncPath(); }
		catch (InvalidOperationException ex)
		{
			Logger.PrintError("FileManager.TryReadWhitelistAsync()", ex);
			return null;
		}
		path = Path.Combine(path, parentDirName, "whitelist.txt");
		return await TryReadFileAsync(path, cToken, shouldReportIfMissing:false);
	}

	internal static async Task<string> ReadSignatureAsync(CancellationToken cToken)
	{
		string? content = await TryReadFileAsync(Path.Combine(GetMySyncPath(), "signature.txt"), cToken); // [2]
		if (content is null) throw new InvalidOperationException("Cannot read signature.txt");
		return content.Trim(['\r','\n']);
	}

	internal static async Task<MemoryStream?> TryCreateZipMemoryStreamAsync(string[] relPathsToInclude, CancellationToken cToken)
	{
		string syncPath = null!;
		try
			{ syncPath = GetSyncPath(); }
		catch (InvalidOperationException ex)
		{
			Logger.PrintError("FileManager.TryCreateZipMemoryStreamAsync()", ex);
			return null;
		}
		var ms = new MemoryStream();
		using (var zipArchive = new ZipArchive(ms, ZipArchiveMode.Create, true))
		{
			foreach (var relPath in relPathsToInclude)
			{
				try
				{
					var tmp = Path.Combine(syncPath, relPath);
					using (var fs = await OpenWithRetriesAsync(tmp, FileMode.Open, FileAccess.Read, cToken))
					using (var entryStream = zipArchive.CreateEntry(relPath).Open())
						{ await fs.CopyToAsync(entryStream, cToken); }
				}
				catch (IOException ex)
				{
					Logger.PrintError("FileManager.TryCreateZipMemoryStreamAsync()", ex);
					ms.Dispose();
					return null;
				}
			}
		}
		try
			{ ms.Position = 0; }
		catch (IOException ex)
		{
			Logger.PrintError("FileManager.TryCreateZipMemoryStreamAsync()", ex);
			ms.Dispose();
			return null;
		}
		return ms;
	}

	static string GetDirPath(string dirName, Environment.SpecialFolder parentDir)
	{
		var parentDirPath = Environment.GetFolderPath(parentDir);
		if (parentDirPath == "")
			throw new InvalidOperationException($"Could not determine the path of {parentDir}."); // [1]
		return Path.Combine(parentDirPath, dirName);
	}

	static async Task<string?> TryReadFileAsync(string path, CancellationToken cToken, bool shouldReportIfMissing=true)
	{
		try
		{
			using var reader = new StreamReader(
				await OpenWithRetriesAsync(path, FileMode.Open, FileAccess.Read, cToken)
			);
			return reader.ReadToEnd();
		}
		catch (Exception ex)
		{
			if (ex is not FileNotFoundException || shouldReportIfMissing)
				Logger.PrintError("FileManager.TryReadFileAsync()", ex);
		}
		return null;
	}
	
	static async Task<FileStream> OpenWithRetriesAsync(string path, FileMode fileMode, FileAccess fileAccess, CancellationToken cToken)
	{
		const int maxRetries = 5, numMs = 500;
		for (int i = 0; i < maxRetries; i++)
		{
			try
				{ return File.Open(path, fileMode, fileAccess); }
			catch (IOException ex) when (((ex.HResult & 0x0000FFFF) == 32) || ((ex.HResult & 0x0000FFFF) == 33)) // [3]
				{ await Task.Delay(numMs, cToken); }
		}
		throw new IOException($"Cannot open {path} using {fileMode} fileMode and {fileAccess} fileAccess");
	}
}

/*
	[1]
		This method is called mainly at the start of the program. If we cant resolve the path, we cannot continue.
		The program will then end abruptly (as I dont catch the exception from the Main()), possibly closing the
		console immediately. This is mostly a lazy behaviour for something that should actually never happen
		(why would one not have its Documents and AppData\Local directories?). It is the only scenario
		(that I know of, besides theoretical out of memory errors) in which the program ends due to an uncaught exception.
		Edit: actually, another scenario is when ReadSignature() is canceled.

	[2]
		This method is called only at the start of the program (I suppose this wont change in the future).

	[3]
		Another process is using the file. Useful links:
			https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors
			https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
*/