using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using DbOperations;
using static DbOperations.ActionType;

class Uploader
{
	const string UrlPrefix = "https://graph.microsoft.com/v1.0/me/drive/root:/";

	readonly string _syncPath;
	readonly string _driveBaseDirName;
	readonly HttpClient _httpClient;
	CancellationTokenSource _ctsSuspend;
	internal bool HasFinished { set; get; } // get may actually be enough (fix)

	Uploader(string signature)
	{
		HasFinished = false;
		_syncPath = FileManager.GetSyncPath();
		_driveBaseDirName = $"Sync_{signature}";
		_httpClient = new HttpClient(new SocketsHttpHandler{PooledConnectionLifetime=TimeSpan.FromMinutes(10)}); // [1]
		_httpClient.Timeout = TimeSpan.FromSeconds(15); // [2]
		_ctsSuspend = new CancellationTokenSource();
	}
	
	internal static async Task<Uploader> CreateAsync(CancellationToken cToken) // [15]
		=> new Uploader(await FileManager.ReadSignatureAsync(cToken));
	
	internal async Task RunAsync(ChannelWriter<DbOperation> operationWriter, Provider provider, CancellationToken cToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(1), cToken); // [6]
			while (true) // [3]
			{
				if (await TryProcessPendingActionsAsync(operationWriter, provider, cToken)) // [4]
					await Task.Delay(TimeSpan.FromMinutes(1), cToken);
				else
				{
					Logger.PrintInfo(null, "Uploader suspended");
					try
						{ await Task.Delay(Timeout.InfiniteTimeSpan, _ctsSuspend.Token); }
					catch (OperationCanceledException)
						{}
					_ctsSuspend.Dispose(); // [5]
					_ctsSuspend = new();
				}	
			}
		}
		catch (OperationCanceledException) { HasFinished = true; } // volatile?
		finally
		{
			_httpClient.Dispose();
			_ctsSuspend.Dispose();
		}
	}

	internal void StopSuspension()
		=> _ctsSuspend.Cancel();

	async Task<bool> TryProcessPendingActionsAsync(ChannelWriter<DbOperation> operationWriter, Provider provider, CancellationToken cToken)
	{
		foreach (var pAction in await GetPendingActionsAsync(operationWriter))
		{
			var driveZipPath = $"{_driveBaseDirName}/{pAction.RelPath}.zip";
			bool anErrorOccurred = pAction.ActionType switch
			{
				Upload => !(await TryUploadAsync(driveZipPath, pAction.RelPath, provider, cToken)),
				Delete => !(await TryDeleteAsync(driveZipPath, provider, cToken)),
				_ => throw new ArgumentException($"Unexpected action type \"{pAction.ActionType}\"")
			};
			if (anErrorOccurred) return false; // [7]
			Logger.PrintSuccess(null, $"{pAction.ActionType} succeeded on {pAction.RelPath}");

			var operation = new DeletePendingAction(pAction.RelPath, pAction.NumAction); // [8]
			if (!operationWriter.TryWrite(operation))
			{
				Logger.PrintError("Uploader.TryProcessPendingActionsAsync()", $"TryWrite() of {operation} failed");
				throw new OperationCanceledException();
			}
		}
		return true;
	}

	async Task<PendingAction[]> GetPendingActionsAsync(ChannelWriter<DbOperation> operationWriter)
	{
		var tcs = new TaskCompletionSource<PendingAction[]>();
		if (!operationWriter.TryWrite(new GetPendingActions{Tcs=tcs}))
			throw new OperationCanceledException();
		return await tcs.Task; // [9]
	}
	
	async Task<bool> TryUploadAsync(string driveZipPath, string dirToBeZippedName, Provider provider, CancellationToken cToken)
	{ 
		var accessToken = await TryGetAccessTokenAsync(provider, cToken); // [10]
		if (accessToken is null) return false;

		var content = await FileManager.TryReadWhitelistAsync(dirToBeZippedName, cToken);
		if (!File.Exists(Path.Combine(_syncPath, dirToBeZippedName, "whitelist.txt"))) return true; // most likely a temporary workaround
		if (content is null) return false;

		var ms = await FileManager.TryCreateZipMemoryStreamAsync(
			content.Split(["\r\n", "\n" ], StringSplitOptions.RemoveEmptyEntries), cToken
		);
		if (ms is null) return false;

		using var request = new HttpRequestMessage();
		request.Method = HttpMethod.Put;
		request.RequestUri = new Uri($"{UrlPrefix}{driveZipPath}:/content");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		request.Content = new StreamContent(ms);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
		
		using var response = await TrySendAsync(request, cToken);
		if (response is null) return false;
		
		if (response.IsSuccessStatusCode) return true;
		Logger.PrintError("Uploader.TryDeleteAsync()", await response.Content.ReadAsStringAsync()); // [16]
		return false;
	}

	async Task<bool> TryDeleteAsync(string driveZipPath, Provider provider, CancellationToken cToken)
	{
		var accessToken = await TryGetAccessTokenAsync(provider, cToken); // [11]
		if (accessToken is null) return false;

		using var request = new HttpRequestMessage();
		request.Method = HttpMethod.Delete;
		request.RequestUri = new Uri($"{UrlPrefix}{driveZipPath}");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		
		using var response = await TrySendAsync(request, cToken);
		if (response is null) return false;

		if (response.IsSuccessStatusCode) return true;
		Logger.PrintError("Uploader.TryDeleteAsync()", await response.Content.ReadAsStringAsync()); // [16]
		return response.StatusCode == HttpStatusCode.NotFound; // [12]
	}
	
	async Task<string?> TryGetAccessTokenAsync(Provider provider, CancellationToken cToken)
	{
		try
			{ return await provider.GetAccessTokenAsync(cToken); }
		catch (MsalException ex) when (ex.ErrorCode is MsalError.AuthenticationCanceledError)
			{} // [13]
		catch (Exception ex) // [14]
			{ Logger.PrintError("Uploader.TryGetAccessTokenAsync()", ex); }
		return null;
	}

	async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cToken) 
	{
		try
			{ return await _httpClient.SendAsync(request, cToken); }
		catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException)
			{ Logger.PrintError("Uploader.TrySendAsync()", "The request timed out."); }
		catch (HttpRequestException ex)
			{ Logger.PrintError("Uploader.TrySendAsync()", ex); }
		return null;
	}
}

/*
	[1]
		Check:
			https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#:~:text=across%20all%20platforms.-,DNS%20behavior,-HttpClient%20only%20resolves

	[2]
		Based on:
			https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0
		and AI advice.
		
	[3]
		The loop ends only if an OperationCanceledException is thrown inside it.
		
	[4]
		TryProcessPendingActionsAsync() consists of two phases:
			- waiting for a scan of the PendingActions table
			- trying to perfom an upload/delete request for each obtained record
		*Any* error (even a 5xx http response code) occurring during the second phase makes the Uploader suspend. 
		The user can call StopSuspension() to let the Uploader retry. The idea of suspending the Uploader
		comes from the preference of avoiding repeated notifications, for instance if no internet connection is
		available or if the user chose not to authenticate. The fact that *any* error can make the Uploader enter this
		state is due to a tradeoff between code simplicity and the effort of manual restarts, under the assumption
		that the latter will need to be done very rarely. In other words, I assume that with very high probability
		the simplified code will work without requiring manual intervention. The use of this program for ???
		[confirms/disproves] this assumption (x outages reported).
		Related: https://learn.microsoft.com/en-us/graph/errors.
		
	[5]
		If StopSuspension() is called after the execution of this line, but before the creation of a new
		TaskCompletionSource, then the caller gets an ObjectDisposedException. As long as the switch cases
		in Program.cs can be entered only by manually entering the corresponding value, I dont expect this to happen.
		
	[6]
		The program is intended to be automatically executed when I start using the computer.
		Without this delay, the internet connection may not be set up in time (mainly because I manually enable
		the wifi). So, the Uploader might get suspended.
	
	[7]
		We cannot just skip to the next operation. Each rename is converted by an upload followed by a delete.
		If the upload failed, but we continued and the delete succeeded, then the information would get mistakenly
		lost (well possibly not due to the recycle bin).
	
	[8]
		If this operation doesnt get executed, then the Uploader will retry the upload/delete the next time it runs
		(despite the success it got before the TryWrite()). In that case, the upload will just be useless (not harmful,
		unless the target drivePath got modified in the mean time by some other program), while the delete will trigger
		a 404 response (or an actual deletion if the target drivePath...see the previous parenthesis).
		Anyway, what's the likelihood that after a successful request and before the operation gets executed,
		the shutdown is initiated (and so the operation is skipped)? I suppose quite low.
	
	[9]
		The Uploader awaits the task until the Interactor processes it normally, or cancels it.
		This is to say that unless the Interactor explodes, the Uploader should not remain blocked here.
	
	[10]
		The TryUploadAsync() method is based on:
			https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0&tabs=http

	[11]
		The TryDeleteAsync() method is based on:
			https://learn.microsoft.com/en-us/graph/api/driveitem-delete?view=graph-rest-1.0&tabs=http

	[12]
		Suppose that no other program changed the target drive directory. Then, the presence of the 404 response
		code is justified by either [8] in Uploader.cs, or by [3] in Evaluator.cs. I do log the presence of this
		response, but I make TryDeleteAsync() return true. While it is weird to make it return true even in this case,
		it seems good enough as the code will likely never change. Note that if 404 is obtained, then the Uploader
		should ask the deletion of the record from the db (just like it does for successful requests). If it didnt,
		it would keep getting a 404 until that record is appropriately updated.

	[13]
		I dont log the exception because:
			https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.msalerror?view=msal-dotnet-latest#:~:text=The%20user%20had%20canceled%20the%20authentication%2C%20for%20instance%20by%20closing%20the%20authentication%20dialog
		Also note that I dont make Provider expose a "TryGetAccessTokenAsync()" method because the Uploader needs
		to know something regarding the reason of failure (specifically, whether the user canceled the authentication).

	[14]
		I catch "Exception" because this:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/exceptions/#:~:text=No%20other%20exception%20is%20caught%20by%20MSAL.%20Any%20network%20issues%2C%20cancellations%20etc.%20are%20bubbled%20up%20to%20the%20application.
		seems not very precise.
	
	[15]
		Just like comment [2] in Provider.cs

	[16]
		This is actually json, I guess I will just copy-paste from the console instead of making the program visualize
		it nicely.
*/