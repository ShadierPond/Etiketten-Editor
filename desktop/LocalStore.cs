using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LabelEditor.Desktop;

public sealed class LocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string connectionString;

    public string DatabasePath { get; }

    public bool HasCatalog
    {
        get
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM Catalog LIMIT 1);";
            return Convert.ToInt32(command.ExecuteScalar()) != 0;
        }
    }

    public LocalStore()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadierPond",
            "FuniturenEditor");
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "funituren-desktop.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Catalog (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Json TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS UiSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                PreviewWidthRatio REAL NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public double LoadPreviewWidthRatio()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PreviewWidthRatio FROM UiSettings WHERE Id = 1;";
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0.4 : Convert.ToDouble(value);
    }

    public void SavePreviewWidthRatio(double ratio)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UiSettings (Id, PreviewWidthRatio) VALUES (1, $ratio)
            ON CONFLICT(Id) DO UPDATE SET PreviewWidthRatio = excluded.PreviewWidthRatio;
            """;
        command.Parameters.AddWithValue("$ratio", ratio);
        command.ExecuteNonQuery();
    }

    public CatalogData LoadCatalog()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Json FROM Catalog WHERE Id = 1;";
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
            return CatalogData.CreateDefault();

        var data = JsonSerializer.Deserialize<CatalogData>(json, JsonOptions) ?? CatalogData.CreateDefault();
        data.Normalize();
        return data;
    }

    public void SaveCatalog(CatalogData data)
    {
        data.Normalize();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Catalog (Id, Json, UpdatedAtUtc) VALUES (1, $json, $updated)
            ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json, UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(data, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string BackupAndResetCatalog(string backupJson)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"funituren-backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(backupPath, backupJson);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Catalog WHERE Id = 1;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return backupPath;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
