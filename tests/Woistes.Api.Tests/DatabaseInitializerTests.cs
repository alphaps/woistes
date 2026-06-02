using Woistes.Api;

namespace Woistes.Api.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public void Run_Succeeds_CallsMigrateOnce()
    {
        var calls = 0;
        DatabaseInitializer.Run(() => calls++, maxAttempts: 3);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Run_RetriesTransientFailure_ThenSucceeds()
    {
        var calls = 0;
        DatabaseInitializer.Run(() =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient: server not ready");
        }, maxAttempts: 5);

        Assert.Equal(3, calls);
    }

    [Fact]
    public void Run_ToleratesAlreadyExists_DoesNotThrow()
    {
        var calls = 0;
        // Simulates a concurrent instance having already created the database.
        DatabaseInitializer.Run(() =>
        {
            calls++;
            throw new InvalidOperationException("Database 'Woistes' already exists. Choose a different database name.");
        }, maxAttempts: 5);

        Assert.Equal(1, calls); // benign error → returns immediately, no retry
    }

    [Fact]
    public void Run_RethrowsAfterMaxAttempts()
    {
        var calls = 0;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseInitializer.Run(() =>
            {
                calls++;
                throw new InvalidOperationException("persistent failure");
            }, maxAttempts: 3));

        Assert.Equal(3, calls);
        Assert.Equal("persistent failure", ex.Message);
    }

    [Fact]
    public void IsBenign_DetectsAlreadyExistsRegardlessOfCase()
    {
        Assert.True(DatabaseInitializer.IsBenign(new Exception("Database 'X' ALREADY EXISTS.")));
        Assert.True(DatabaseInitializer.IsBenign(new Exception("There is already an object named 'Foo'")));
        Assert.False(DatabaseInitializer.IsBenign(new Exception("connection refused")));
    }
}
