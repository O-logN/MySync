namespace DbOperations;

class PendingAction
{
	internal required string RelPath { get; init; }
	internal required ActionType ActionType { get; init; }
	internal required long NumAction { get; init; }
}