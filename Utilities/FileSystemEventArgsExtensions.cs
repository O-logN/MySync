namespace MySync.Utilities;

public static class FileSystemEventArgsExtentions
{
    public static string AsString(this FileSystemEventArgs e)
        => e is RenamedEventArgs g ?
            $"RenamedEventArgs(OldName={g.OldName}, Name={g.Name}, FullPath={g.FullPath}, ChangeType={g.ChangeType})" :
            $"FileSystemEventArgs(Name={e.Name}, FullPath={e.FullPath}, ChangeType={e.ChangeType})";
}