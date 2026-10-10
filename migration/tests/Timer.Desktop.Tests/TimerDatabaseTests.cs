using Microsoft.Data.Sqlite;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TimerDatabaseTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "timer-db-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Today = new(2026, 10, 10, 9, 0, 0);
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    private TimerDatabase Open(DateTime? now = null) => TimerDatabase.Open(folder, () => now ?? Today);
    private static long Account(TimerDatabase db, string name, string colour = "#91B7D5", int position = 0)
        => db.Insert("INSERT INTO accounts (name, colour, position) VALUES ($n, $c, $p)", ("$n", name), ("$c", colour), ("$p", position));
    private static void Work(TimerDatabase db, long account, string start = "2026-10-10T09:00:00", string end = "2026-10-10T10:00:00", long seconds = 3600)
        => db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('work', $a, '2026-10-10', $s, $e, $sec, '2026-10-10T10:00:00')", ("$a", account), ("$s", start), ("$e", end), ("$sec", seconds));
    private static void Rejects(Action action) => Assert.Throws<SqliteException>(action);

    [Fact]
    public void NewDatabaseHasTheTablesTheVersionStampAndTheRightSettings()
    {
        using var db = Open();
        Assert.Equal(TimerDatabase.SchemaVersion, db.Version); Assert.Equal((long)TimerDatabase.SchemaVersion, db.Scalar("PRAGMA user_version"));
        var tables = db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name", r => r.GetString(0));
        Assert.Equal(new[] { "accounts", "away", "day_starts", "edit_changes", "edits", "entries", "groups", "meta", "running" }, tables);
        Assert.Equal("wal", db.Scalar("PRAGMA journal_mode")); Assert.Equal(1L, db.Scalar("PRAGMA foreign_keys"));
        Assert.True(File.Exists(Path.Combine(folder, TimerDatabase.FileName)));
    }

    [Fact]
    public void EachEntryKindMustHaveExactlyTheColumnsItNeeds()
    {
        using var db = Open(); var a = Account(db, "Account one");
        Work(db, a); // A valid work entry.
        db.Execute("INSERT INTO entries (kind, day, start_at, end_at, seconds, created_at) VALUES ('break', '2026-10-10', '2026-10-10T12:00:00', '2026-10-10T12:30:00', 1800, 'x')");
        db.Execute("INSERT INTO entries (kind, account_id, day, seconds, created_at) VALUES ('adjustment', $a, '2026-10-10', -600, 'x')", ("$a", a));
        Rejects(() => db.Execute("INSERT INTO entries (kind, day, start_at, end_at, seconds, created_at) VALUES ('work', '2026-10-10', 'a', 'b', 60, 'x')")); // Work needs an account.
        Rejects(() => Work(db, a, seconds: 0)); Rejects(() => Work(db, a, seconds: -5)); // And a positive length.
        Rejects(() => db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('break', $a, '2026-10-10', 'a', 'b', 60, 'x')", ("$a", a))); // A break has no account.
        Rejects(() => db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('adjustment', $a, '2026-10-10', 'a', 'b', 60, 'x')", ("$a", a))); // An adjustment has no end.
        Rejects(() => db.Execute("INSERT INTO entries (kind, account_id, day, seconds, created_at) VALUES ('adjustment', $a, '2026-10-10', 0, 'x')", ("$a", a))); // And is never zero.
        Rejects(() => db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('lunch', $a, '2026-10-10', 'a', 'b', 60, 'x')", ("$a", a)));
        Rejects(() => db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, origin, created_at) VALUES ('work', $a, '2026-10-10', 'a', 'b', 60, 'guess', 'x')", ("$a", a)));
        Assert.Equal(3L, db.Scalar("SELECT COUNT(*) FROM entries"));
    }

    [Fact]
    public void AccountsKeepTheirRulesAndEntriesStayLinkedToThem()
    {
        using var db = Open(); var a = Account(db, "Account one");
        Rejects(() => Account(db, "account ONE")); // Names are unique whatever the capitals.
        Rejects(() => Account(db, "")); Rejects(() => Account(db, "Bad colour", "green"));
        Rejects(() => db.Execute("INSERT INTO accounts (name, colour, position, budget_period) VALUES ('B', '#AAAAAA', 1, 'decade')"));
        Rejects(() => db.Execute("INSERT INTO accounts (name, colour, position, budget_seconds) VALUES ('C', '#AAAAAA', 2, 0)"));
        Work(db, a);
        Rejects(() => db.Execute("DELETE FROM accounts WHERE id = $a", ("$a", a))); // An account with entries cannot vanish from under them.
        Rejects(() => Work(db, 9999)); // And an entry cannot point at an account that is not there.
        db.Execute("UPDATE accounts SET name = 'Renamed' WHERE id = $a", ("$a", a)); // A rename touches one row; entries follow by id.
        Assert.Equal("Renamed", db.Scalar("SELECT a.name FROM entries e JOIN accounts a ON a.id = e.account_id"));
    }

    [Fact]
    public void ADeletedGroupLeavesItsEntriesInPlace()
    {
        using var db = Open(); var a = Account(db, "Account one"); Work(db, a);
        var group = db.Insert("INSERT INTO groups (account_id, day) VALUES ($a, '2026-10-10')", ("$a", a));
        db.Execute("UPDATE entries SET group_id = $g", ("$g", group));
        db.Execute("DELETE FROM groups WHERE id = $g", ("$g", group));
        Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM entries")); Assert.Equal(DBNull.Value, db.Scalar("SELECT group_id FROM entries"));
    }

    [Fact]
    public void OnlyOneRecordingCanRunAndABreakRemembersWhereToReturn()
    {
        using var db = Open(); var a = Account(db, "Account one");
        db.Execute("INSERT INTO running (id, kind, account_id, start_at, last_seen) VALUES (1, 'work', $a, 't', 't')", ("$a", a));
        Rejects(() => db.Execute("INSERT INTO running (id, kind, account_id, start_at, last_seen) VALUES (2, 'work', $a, 't', 't')", ("$a", a)));
        db.Execute("DELETE FROM running");
        db.Execute("INSERT INTO running (id, kind, resume_account_id, start_at, last_seen) VALUES (1, 'break', $a, 't', 't')", ("$a", a));
        Assert.Equal(a, Convert.ToInt64(db.Scalar("SELECT resume_account_id FROM running")));
        db.Execute("DELETE FROM running");
        Rejects(() => db.Execute("INSERT INTO running (id, kind, start_at, last_seen) VALUES (1, 'work', 't', 't')")); // Work needs an account.
    }

    [Fact]
    public void AFailedTransactionChangesNothingAndANestedOneJoinsTheOuter()
    {
        using var db = Open();
        Assert.Throws<InvalidOperationException>(() => db.InTransaction(() =>
        {
            Account(db, "Kept?"); Account(db, "Also kept?");
            throw new InvalidOperationException("stop half way");
        }));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM accounts")); // All or nothing.
        Assert.Throws<InvalidOperationException>(() => db.InTransaction(() =>
        {
            Account(db, "Outer"); db.InTransaction(() => Account(db, "Inner", position: 1));
            throw new InvalidOperationException("the outer fails after the inner finished");
        }));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM accounts")); // The inner change was part of the outer one.
        Assert.Equal(1L, db.InTransaction(() => { Account(db, "Fine"); return db.Scalar("SELECT COUNT(*) FROM accounts"); }));
    }

    [Fact]
    public void DataSurvivesClosingAndClosingLeavesOneCompleteFile()
    {
        using (var db = Open()) { var a = Account(db, "Account one"); Work(db, a); }
        Assert.False(File.Exists(Path.Combine(folder, "timer.db-wal"))); Assert.False(File.Exists(Path.Combine(folder, "timer.db-shm")));
        // The one file is complete on its own: copy it elsewhere and read it with nothing else beside it.
        var copy = Path.Combine(Path.GetTempPath(), "timer-copy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            File.Copy(Path.Combine(folder, "timer.db"), copy);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM entries";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(copy); }
        using var again = Open(); Assert.Equal(1L, again.Scalar("SELECT COUNT(*) FROM entries")); Assert.Equal(1L, again.Scalar("SELECT COUNT(*) FROM accounts"));
    }

    [Fact]
    public void ASecondCopyOfTheTimerIsTurnedAwayWithAClearMessage()
    {
        using var first = Open();
        var error = Assert.Throws<DatabaseInUseException>(() => Open());
        Assert.Contains("already open", error.Message);
        first.Dispose();
        using var afterwards = Open(); Assert.Equal(TimerDatabase.SchemaVersion, afterwards.Version); // And the file is free again once the first closes.
    }

    [Fact]
    public void ANewerDataFormatIsRefusedAndLeftUntouched()
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "timer.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE future (x); PRAGMA user_version = 99;"; command.ExecuteNonQuery(); }
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(path);
        var error = Assert.Throws<DatabaseTooNewException>(() => Open());
        Assert.Contains("newer version", error.Message); Assert.Contains("99", error.Message);
        SqliteConnection.ClearAllPools();
        using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        check.Open(); using var tables = check.CreateCommand(); tables.CommandText = "SELECT group_concat(name) FROM sqlite_master WHERE type = 'table'";
        Assert.Equal("future", tables.ExecuteScalar()); // Nothing was added to it.
        Assert.False(Directory.Exists(Path.Combine(folder, "Backups")) && Directory.GetFiles(Path.Combine(folder, "Backups")).Length > 0);
    }

    [Fact]
    public void AnUpgradeRunsOnceAfterACopyIsSavedAndKeepsTheData()
    {
        using (var db = Open(Today)) { Work(db, Account(db, "Account one")); }
        var steps = new[] { "ALTER TABLE accounts ADD COLUMN owner TEXT", "ALTER TABLE entries ADD COLUMN billable INTEGER NOT NULL DEFAULT 1" };
        using (var db = TimerDatabase.Open(folder, () => Today.AddDays(1), steps, targetVersion: 3))
        {
            Assert.Equal(3, db.Version); Assert.Equal(3L, db.Scalar("PRAGMA user_version"));
            Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM entries")); Assert.Equal(1L, db.Scalar("SELECT billable FROM entries"));
            db.Execute("UPDATE accounts SET owner = 'me'");
        }
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-before-format-3-*.db"));
        // The saved copy is the data as it was before: format 1, without the new columns.
        var copy = Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-before-format-3-*.db").Single();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version"; Assert.Equal(1L, command.ExecuteScalar());
        SqliteConnection.ClearAllPools();
        using var reopened = TimerDatabase.Open(folder, () => Today.AddDays(2), steps, targetVersion: 3); // Already at 3: nothing runs again.
        Assert.Equal(3, reopened.Version); Assert.Single(Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-before-format-*.db"));
    }

    [Fact]
    public void AMissingUpgradeStepIsRefusedWithoutHalfUpgrading()
    {
        using (var db = Open()) { Account(db, "Account one"); }
        Assert.Throws<InvalidDataException>(() => TimerDatabase.Open(folder, () => Today, new[] { "ALTER TABLE accounts ADD COLUMN owner TEXT" }, targetVersion: 4));
        SqliteConnection.ClearAllPools();
        using var db2 = Open(); Assert.Equal(1, db2.Version); // The first step was not left applied.
        Assert.Equal(0L, db2.Scalar("SELECT COUNT(*) FROM pragma_table_info('accounts') WHERE name = 'owner'"));
    }

    [Fact]
    public void TheFirstLaunchOfEachDaySavesACopyAndTheNewestFourteenAreKept()
    {
        for (var day = 0; day < 20; day++) { using var db = Open(Today.AddDays(day)); if (day == 3) Account(db, "Added on day three"); }
        var copies = Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-????????.db").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(14, copies.Count); Assert.Equal("timer-20261016.db", copies[0]); Assert.Equal("timer-20261029.db", copies[^1]);
        using (var db = Open(Today.AddDays(19))) Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM accounts")); // A second launch the same day adds no second copy.
        Assert.Equal(14, Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-????????.db").Length);
        // A copy is a complete database, readable on its own.
        var copy = Path.Combine(folder, "Backups", "timer-20261016.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM accounts"; Assert.Equal(1L, command.ExecuteScalar());
    }
}
