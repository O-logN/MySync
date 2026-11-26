namespace MySync.Utilities;

public static class TaskEx
{
    public static async Task<bool> TryWaitAsync(this Task task, CancellationToken cToken)
    {
        try
        {
            await task.WaitAsync(cToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> TryDelay(TimeSpan timeSpan, CancellationToken cToken)
    {
        return await Task.Delay(timeSpan, cToken).TryWaitAsync(default);
    }
}