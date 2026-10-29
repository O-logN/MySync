using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Identity.Client;
using DbOperations;
using static DbOperations.EventType;

class Uploader
{
	readonly string _signature;
	readonly HttpClient _httpClient;
	volatile CancellationTokenSource? _ctsSuspend;

	Uploader(string signature)
	{
		_signature = signature;
		_httpClient = new HttpClient(new SocketsHttpHandler{PooledConnectionLifetime=TimeSpan.FromMinutes(10)}); // [1]
		_httpClient.Timeout = TimeSpan.FromSeconds(15);
		_ctsSuspend = null;
	}
	
	internal static async Task<Uploader> CreateAsync(CancellationToken cToken)
		=> new Uploader(await FileManager.ReadFileAsync(FileManager.SignaturePath, cToken));

	internal void StopSuspension()
	{
		if (_ctsSuspend is not null)
			_ctsSuspend.Cancel();
	}
	
	internal async Task RunAsync(ChannelWriter<DbOperation> operationWriter, Provider provider, CancellationToken cToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(8), cToken); // [2]
			while (true) // [3]
			{
				var hSet = new HashSet<string>(); // [8]
				foreach (var e in await GetEventsAsync(operationWriter))
				{
					if (!await TrySendAndReceiveAsync(e, provider, hSet, cToken)) // [4]
					{
						Logger.PrintInfo("Uploader suspended");
						_ctsSuspend = new();
						cToken.ThrowIfCancellationRequested();
						try
							{ await Task.Delay(Timeout.InfiniteTimeSpan, _ctsSuspend.Token); }
						catch (OperationCanceledException)
							{ cToken.ThrowIfCancellationRequested(); }
						_ctsSuspend.Dispose();
						_ctsSuspend = null;
						break;
					}
					if (!operationWriter.TryWrite(new DeleteEvent(e))) // [5]
					{
						this.PrintError($"TryWrite() of {new DeleteEvent(e)} failed");
						throw new OperationCanceledException();
					}	
				}
				await Task.Delay(TimeSpan.FromMinutes(1), cToken);
			}
		}
		catch (OperationCanceledException)
			{}
		finally
			{ _httpClient.Dispose(); }
	}
	
	async Task<Event[]> GetEventsAsync(ChannelWriter<DbOperation> operationWriter)
	{
		var tcs = new TaskCompletionSource<Event[]>();
		if (!operationWriter.TryWrite(new GetEvents(tcs)))
			throw new OperationCanceledException();
		return await tcs.Task; // [6]
	}

	async Task<bool> TrySendAndReceiveAsync(Event e, Provider provider, HashSet<string> hSet, CancellationToken cToken)
	{
		cToken.ThrowIfCancellationRequested();
		string? accessToken = await TryGetAccessTokenAsync(provider, cToken);
		if (accessToken is null) return false;
		
		bool b = true;
		switch (e.EventType)
		{
			case Create:
				if (Directory.Exists(Path.Combine(FileManager.SyncPath, e.RelPath!)))
				{
					var j = e.RelPath!.LastIndexOf("\\");
					(string parentRelPath, string dirName) = j > -1 ?
						(e.RelPath.Substring(0,j), e.RelPath.Substring(j+1)) : ("", e.RelPath);
					b = await TryCreateDirectoryAsync(parentRelPath, dirName, accessToken, cToken); // [7]
					if (b)
						Logger.PrintSuccess($"Created directory {e.RelPath}");
				}
				else if (File.Exists(Path.Combine(FileManager.SyncPath, e.RelPath!)))
				{
					if (!hSet.Contains(e.RelPath!)) // [9]
					{
						b = await TryUploadFileAsync(e.RelPath!, accessToken, cToken);
						if (b)
						{
							hSet.Add(e.RelPath!);
							Logger.PrintSuccess($"Uploaded {e.RelPath}");
						}
					}
				}
				else
				{
					this.PrintError($"Could not determine if {e.RelPath} is a file or a directory"); // [10]
					b = false;
				}
				break;
				
			case Change:
				if (!hSet.Contains(e.RelPath!))
				{
					b = await TryUploadFileAsync(e.RelPath!, accessToken, cToken);
					if (b)
					{
						hSet.Add(e.RelPath!);
						Logger.PrintSuccess($"Uploaded {e.RelPath}");
					}	
				}
				break;
				
			case Rename:
				var i = e.RelPath!.LastIndexOf("\\");
				var newName = i > -1 ? e.RelPath!.Substring(i+1) : e.RelPath;
				b = await TryRenameAsync(e.OldRelPath!, newName, accessToken, cToken);
				if (b)
				{
					hSet.Remove(e.OldRelPath!);
					hSet.Add(e.RelPath!);
					Logger.PrintSuccess($"Renamed {e.OldRelPath} to {e.RelPath}");
				}
				break;
				
			case Delete:
				b = await TryDeleteAsync(e.RelPath!, accessToken, cToken);
				if (b)
				{
					hSet.Remove(e.RelPath!); // [11]
					Logger.PrintSuccess($"Deleted {e.RelPath}");
				}
				break;
				
			default:
				throw new InvalidOperationException($"Unrecognized event type {e.EventType}");
		}
		return b;
	}

	async Task<string?> TryGetAccessTokenAsync(Provider provider, CancellationToken cToken)
	{
		try
			{ return await provider.GetAccessTokenAsync(cToken); }
		catch (MsalException ex) when (ex.ErrorCode is MsalError.AuthenticationCanceledError)
			{} // [12]
		catch (Exception ex) // [13]
			{ this.PrintError(ex); }
		return null;
	}

	async Task<bool> TryCreateDirectoryAsync(string parentRelPath, string dirName, string accessToken, CancellationToken cToken)
	{   // [14]
		using var request = new HttpRequestMessage();
		request.Method = HttpMethod.Post;
		request.RequestUri = GetUri(parentRelPath, "Create Directory");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		request.Content = new StringContent(
			JsonSerializer.Serialize(new {name = dirName, folder = new{}})
		);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		return await HandleResponseAsync(await TrySendAsync(request, cToken));
	}

	async Task<bool> TryUploadFileAsync(string relPath, string accessToken, CancellationToken cToken)
	{
		try
		{
			using var fs = await FileManager.OpenFileAsync(Path.Combine(FileManager.SyncPath, relPath), FileMode.Open, FileAccess.Read, cToken);
			using var request = new HttpRequestMessage();
			request.Method = HttpMethod.Put;
			request.RequestUri = GetUri(relPath, "Upload File");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			request.Content = new StreamContent(fs);
			request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			return await HandleResponseAsync(await TrySendAsync(request, cToken));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{ this.PrintError(ex); }
		return false;
	}
	
	async Task<bool> TryRenameAsync(string relPath, string newName, string accessToken, CancellationToken cToken)
	{
		using var request = new HttpRequestMessage();
		request.Method = HttpMethod.Patch;
		request.RequestUri = GetUri(relPath);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		request.Content = new StringContent(
			JsonSerializer.Serialize(new {name = newName})
		);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		return await HandleResponseAsync(await TrySendAsync(request, cToken));
	}

	async Task<bool> TryDeleteAsync(string relPath, string accessToken, CancellationToken cToken)
	{
		using var request = new HttpRequestMessage();
		request.Method = HttpMethod.Delete;
		request.RequestUri = GetUri(relPath);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		return await HandleResponseAsync(await TrySendAsync(request, cToken));
	}

	Uri GetUri(string relPath, string? action = null)
	{
		relPath = relPath.Replace('\\', '/');
		if (relPath != "") relPath = "/" + relPath;
		relPath = Uri.EscapeDataString(relPath);
		var s = $"https://graph.microsoft.com/v1.0/me/drive/root:/Sync_{_signature}{relPath}";
		if (action == "Create Directory")
			s += ":/children";
		if (action == "Upload File")
			s += ":/content";
		return new Uri(s);
	}

	async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cToken) 
	{
		try
			{ return await _httpClient.SendAsync(request, cToken); }
		catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException)
			{ this.PrintError(ex.InnerException); }
		catch (HttpRequestException ex)
			{ this.PrintError(ex); }
		return null;
	}

	async Task<bool> HandleResponseAsync(HttpResponseMessage? response)
	{
		if (response is not null)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
					return true;
				this.PrintError(await response.Content.ReadAsStringAsync());
			}
		}
		return false;
	}
}

/*
	[1]
		Relevant:
			https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#:~:text=across%20all%20platforms.-,DNS%20behavior,-HttpClient%20only%20resolves
			https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0

	[2]
		The program is supposed to be automatically executed when the computer is started.
		Without this delay, an user who needs to manually set up the internet connection may get an undesirable
		Uploader suspension.

	[3]
		Proof that the while loop always ends.
		Consider the moment at which the cToken is canceled:
			If it occurs inside the foreach (var e in ... included):
				If it doesnt happen when the next line to be executed is TrySendAndReceiveAsync():
					If it happens inside the "if block" related to TrySendAndReceiveAsync():
						If it happens after "cToken.ThrowIfCancellationRequested();" is executed:
							If it happens before the Task.Delay() ends:
								The thread that canceled the cToken will call "StopSuspension()".
								It will see that "_ctsSuspend" is not null (because it is volatile), so it will execute
								"_cts.Cancel()". The Task.Delay() will throw an OperationCanceledException. The catch
								will throw an OperationCanceledException. The while loop will end.
							Otherwise:
								Actually, I expect this to be impossible. Here is why. In this scenario, the Uploader's
								suspension has just been interrupted. This implies that StopSuspension() has been called.
								Since the cToken has just been canceled, this StopSuspension() cannot be due to the shutdown
								procedure. Then, it must have been issued manually by the user. However, even the shutdown
								procedure is issued manually by the user. Is it really possible that the computer did not
								execute this few lines yet, within the 1+ seconds between those two manual orders?
						Otherwise:
							The while loop will end
					Otherwise:
						The while loop will end
				Otherwise:
					The while loop will end
			Otherwise:
				The Task.Delay() will make the while loop end (since it throws an OperationCanceledException)
		
	[4]
		If some (unexpected) error occurs while sending the request or receiving the response, or if the response
		contains some error, then the Uploader gets suspended. There is no automatic retry mechanism. This behaviour
		was introduced to handle the absence of internet connection, or the choice of an user not to authenticate
		(in both cases, the Uploader should indeed avoid retries). It was kept for any error out of simplicity.

	[5]
		If the TryWrite() fails, then the Uploader will retry the create/rename/upload/delete during the next execution
		of this program, despite having just obtained a success. This might make ms graph return some error, which
		would put the Uploader in suspension. That error might not be fixable by a retry. For instance, retrying a delete
		on something that doesnt exist will just return 404. So, the idea is to manually delete from the db (or through
		some other script) if that TryWrite() fails. Anyway, I suppose that the likelihood that the TryWrite() fails is
		very low. If it fails, then the channelWriter just got completed. So the cancellation token has ben canceled.
		However, if it was canceled before/during the execution of _httpClient.SendAsync(), then the TryWrite() would
		have not be attempted. So, if the TryWrite() fails then the user has requested the shutdown after the execution
		of _httpClient.SendAsync(). The instructions past the _httpClient.SendAsync() doesnt seem to require more than
		some ms to execute (note that only the case in which TrySendAndReceiveAsync() returns true is relevant here).
		So, the chance that the shutdown procedure is initiated within this period seems very low.
		
	[6]
		The Interactor may cancel the task contained in the operation.
		
	[7]
		Even if the directory contains whitelisted items, those are not uploaded. Albeit limited, this behaviour seems
		good enough. I dont expect directory creation events related to a non-empty directory to happen frequently.
		
	[8]
		The Uploader is about to request a snapshot of the Events table. Suppose it obtains that list, and that
		the hashset is not used. The Uploader will make an upload for each event of type "Create" or "Change"
		that will happen to concern a file. Each upload will consider the current version of that file. So, there is
		the risk to upload the same content more than once. For instance, if the user saved some file through some program,
		then multiple "Change" events on that path may be triggered. Similarly, if the user saved some file multiple times
		before the Uploader obtains the snapshot, then multiple "Change" events will be present in the list.
		This is why the hashset is useful.
		
	[9]
		I added this check just in case a Change event was received before a Create event (if that's even possible).

	[10]
		Directory.Exists() return false if the argument doesnt map to a directory, or if an error happens while determining
		so. File.Exists() has an equivalent behaviour. If both fail, then under the assumption that Sync either contains
		files or directories, an error must have happened.

	[11]
		If right after this "Delete" there is a "Create" or "Change", then the upload of the file must be allowed
		(assuming the event actually concerns a file).
	
	[12]
		The user refused to authenticate.

	[13]
		I catch "exception" because this:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/exceptions/#:~:text=No%20other%20exception%20is%20caught%20by%20MSAL.%20Any%20network%20issues%2C%20cancellations%20etc.%20are%20bubbled%20up%20to%20the%20application.
		seems not very precise.
	
	[14]
		This method is based on:
			https://learn.microsoft.com/en-us/graph/api/driveitem-post-children?view=graph-rest-1.0&tabs=http
*/