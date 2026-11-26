using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using MySync.Data;
using MySync.Data.Operations;
using MySync.Utilities;

namespace MySync.Uploading;

class Requestor : IDisposable
{
    readonly string _signature; // [1]
    readonly ChannelReader<Snapshot> _snapshotReader;
    readonly ChannelWriter<DbOperation> _operationWriter;
    readonly TokenProvider _tokenProvider;
    readonly HttpClient _httpClient;
    CancellationTokenSource _ctsSuspend;

    Requestor(string signature, ChannelReader<Snapshot> snapshotReader, ChannelWriter<DbOperation> operationWriter, TokenProvider tokenProvider)
    {
        _signature = signature;
        _snapshotReader = snapshotReader;
        _operationWriter = operationWriter;
        _tokenProvider = tokenProvider;
        _httpClient = new HttpClient(new SocketsHttpHandler{PooledConnectionLifetime=TimeSpan.FromMinutes(10)}); // [2]
		_httpClient.Timeout = TimeSpan.FromSeconds(15);
        _ctsSuspend = new CancellationTokenSource();
    }

    internal static async Task<Requestor?> TryCreateAsync(ChannelReader<Snapshot> snapshotReader, ChannelWriter<DbOperation> operationWriter)
    {
        var signature = await FileUtils.TryReadFileAsync(FileUtils.SignaturePath);
        if (signature is not null)
            return new Requestor(signature, snapshotReader, operationWriter, await TokenProvider.CreateAsync());
        return null;
    }

    internal async Task RunAsync(CancellationToken cToken)
    {
        await foreach (var snapshot in _snapshotReader.ReadAllAsync())
        {
            var seen = new HashSet<string>();
            foreach (var pathEntry in snapshot.PathEntries)
            {
                if (seen.Add(pathEntry.Path)) // [3]
                {
                    while (true)
                    {
                        if (await TryDispatchAsync(pathEntry, cToken))
                            break;
                        else // [4]
                        {
                            await TaskEx.TryDelay(Timeout.InfiniteTimeSpan, _ctsSuspend.Token);
                            _ctsSuspend.Dispose();
                            _ctsSuspend = new();
                        }
                    }
                }
                _operationWriter.TryWrite(new DeletePath(pathEntry.Id));
            }
            snapshot.Done.SetResult();
        }
    }

	internal void StopSuspension()
        => _ctsSuspend.Cancel();

    public void Dispose()
        => _ctsSuspend.Dispose();

    async Task<bool> TryDispatchAsync(PathEntry pathEntry, CancellationToken cToken)
    {
        var accessToken = await _tokenProvider.TryGetAccessTokenAsync(cToken);
        if (accessToken is null) return false;
        var pathKind = FileUtils.TryGetTargetKind(pathEntry.Path);
        if (pathKind is null) return false;
        return pathKind switch
        {
            FileUtils.TargetKind.File => await TryUploadFileAsync(pathEntry, accessToken, cToken),
            FileUtils.TargetKind.Directory => await TryCreateDirectoryAsync(pathEntry, accessToken, cToken),
            FileUtils.TargetKind.None => await TryDeleteAsync(pathEntry, accessToken, cToken),
            _ => throw new ArgumentException($"Unknnown PathKind: {pathKind}")
        };
    }

    async Task<bool> TryUploadFileAsync(PathEntry pathEntry, string accessToken, CancellationToken cToken)
    {
        var copyPath = Path.Combine(FileUtils.MySyncPath, pathEntry.Id.ToString());
        try
        {
            File.Copy(pathEntry.Path, copyPath); // [5]
        }
        catch (Exception e)
        {
            LogUtils.PrintError($"Got {e.GetType()} with message:\n\t{e.Message}\non {pathEntry}");
            return false;
        }
        bool result = false;
        using var fs = await FileUtils.TryOpenFileAsync(copyPath, FileMode.Open, FileAccess.Read, cToken);
        if (fs is not null)
        {
            using var request = new HttpRequestMessage();
            request.Method = HttpMethod.Put;
            request.RequestUri = GetUri(pathEntry.Path, "Upload File");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StreamContent(fs);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await TrySendAsync(request, cToken);
            if (response is not null)
            {
                if (response.IsSuccessStatusCode)
                    result = true;
                else
                    LogUtils.PrintError(await response.Content.ReadAsStringAsync());
            }
            fs.Close();
        }
        File.SetAttributes(copyPath, FileAttributes.Normal); // [6], [7]
        File.Delete(copyPath);
        return result;
    }

    async Task<bool> TryCreateDirectoryAsync(PathEntry pathEntry, string accessToken, CancellationToken cToken)
    {
        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        request.RequestUri = GetUri(pathEntry.Path, "Create Directory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		request.Content = new StringContent(
			JsonSerializer.Serialize(new {name = Path.GetFileName(pathEntry.Path), folder = new{}})
		);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await TrySendAsync(request, cToken);
        if (response is not null)
        {
            if (response.IsSuccessStatusCode)
                return true;
            LogUtils.PrintError(await response.Content.ReadAsStringAsync());
        }
        return false;
    }

    async Task<bool> TryDeleteAsync(PathEntry pathEntry, string accessToken, CancellationToken cToken)
    {
        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Delete;
        request.RequestUri = GetUri(pathEntry.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await TrySendAsync(request, cToken);
        if (response is not null)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) // [8]
                return true;
            LogUtils.PrintError(await response.Content.ReadAsStringAsync());
        }
        return false;
    }

	Uri GetUri(string path, string action = "")
	{
        var s = Path.GetRelativePath(FileUtils.SyncPath, path);
        s = Uri.EscapeDataString(s.Replace('\\', '/'));
        s += action switch
        {
            "Upload File" => ":/content",
            "Create Directory" => ":/children",
            _ => ""
        };
        return new Uri($"https://graph.microsoft.com/v1.0/me/drive/root:/Sync_{_signature}/{s}");
	}

	async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cToken) 
	{
		try
		{
            return await _httpClient.SendAsync(request, cToken);
        }
        catch (TimeoutException e)
        {
            if (e.InnerException is not null)
                LogUtils.PrintError(e.InnerException);
            else
                LogUtils.PrintError("Request timed out");
        }
		catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException e)
        {
            LogUtils.PrintError(e);
        }
		return null;
	}
}

/*
    [1]
        The instance of this program, which runs on a certain my computer, should write inside a specific OneDrive folder.

    [2]
		Relevant:
			https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#:~:text=across%20all%20platforms.-,DNS%20behavior,-HttpClient%20only%20resolves
			https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0

    [3]
        Suppose that p.RelPath is not contained in the set. At moment t1, the program looks at what p.RelPath identifies
        on the disk (a file, a folder or nothing) and sends a request R to OneDrive. It resumes scanning and encounters
        p.RelPath again. If this "if check" was omitted, then at moment t2 it would send some request R'.
        If R' asked to OneDrive the same thing as R did (delete this thing, upload this content, create this directory),
        then R' would not alter OneDrive's state. If it asked for something different, then it would indeed be useful.
        However, in this scenario a filesystem event on p.RelPath would have happened after t1 (if after t1 no filesystem
        change affected p.RelPath, how would R' ask something different than R?). If this event is written in the db,
        then what R' would have asked (or even something newer) will be asked when the next snapshot will be processed.
        Note that the case in which R' asks the same as R is relevant. Some programs emit even 10+ events on the same
        filepath when you request a file save. Suppose I didnt use this "seen" set and that, while running this program,
        I save a file once. I may be triggering 10+ uploads, all but one being useless.

    [4]
        Rather than implementing a retry mechanism, I find more convenient to suspend the Requestor if an unexpected
        error occurrs. The user (i.e. me) will (possibly) manually restart the program after reading the issue
        (maybe OneDrive is off or the program made too many requests).

    [5]
        Some programs are like this:
            "as long as you (the user) keep the file at some path P open in my window,
             any call to MySync's FileUtils.OpenFileAsync() on P will fail."
        Copying the file and uploading the copy is a workaround.

    [6]
        If the source file is read-only, then the copy is read-only. If the copy is read-only, then File.Delete()
        throws an UnauthorizedAccessException.
    
    [7]
        From the docs of File.SetAttributes() and File.Delete() I dont see valid reasons for either to throw an exception here.

    [8]
        The user may create a file and, before it gets uploaded on OneDrive, delete it.
*/


// mettere l'hashset sui percorsi
// se una richiesta va male, si sospende.
    // ricordarsi di fare una copia! chissà forse possiamo evitare di materializzare su disco
// se cancellazione richiesta, allora annulla il tcs
// far runnare il requestor


// uno potrebbe rinominare A in B, aggiungere file X e poi B in A. se A sta su drive, e si tratta di cartelle,
// ma finchè la A su drive non contiene piu roba della A in locale, non cè problema i guess; anche se i create
// di empty directory potrebbero fallire