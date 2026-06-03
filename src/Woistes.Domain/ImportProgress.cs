namespace Woistes.Domain;

/// <summary>
/// Reported during catalogue import so the UI can show save progress.
/// Import time is dominated by persisting entries (often 100k+ rows), so
/// progress is tracked per disk.
/// </summary>
public record ImportProgress(int DisksSaved, int DisksTotal, int EntriesSaved, int EntriesTotal);
