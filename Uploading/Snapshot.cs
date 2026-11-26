using MySync.Data;

namespace MySync.Uploading;

record Snapshot(PathEntry[] PathEntries, TaskCompletionSource Done);