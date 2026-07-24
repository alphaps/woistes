namespace Woistes.Api;

/// <summary>
/// Runs the startup database migration resiliently. During a rolling deploy two
/// app instances can briefly run at once and both call Migrate(), racing on
/// CREATE DATABASE / CREATE TABLE. The loser would otherwise crash with
/// "Database already exists". This treats such concurrent-creation errors as
/// benign and retries genuinely transient failures (e.g. SQL Server not yet
/// accepting connections).
/// </summary>
public static class DatabaseInitializer
{
    public static void Run(Action migrate, int maxAttempts = 10, Action<int, Exception>? onRetry = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                migrate();
                return;
            }
            catch (Exception ex) when (IsBenign(ex))
            {
                // Another instance already created the schema — nothing to do.
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempts)
                    throw;
                onRetry?.Invoke(attempt, ex);
            }
        }
    }

    /// <summary>
    /// True for errors that mean the schema already exists (a concurrent
    /// instance won the race), which are safe to ignore. Only matches
    /// database-level and table-level "already exists" — NOT constraint or
    /// index conflicts inside a migration, which indicate a real failure.
    /// </summary>
    public static bool IsBenign(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("Database '", StringComparison.OrdinalIgnoreCase)
               && msg.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
