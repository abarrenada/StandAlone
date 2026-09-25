using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace StandAlone.Integration.Services;

/// <summary>
/// Connection parameters for the Oracle interface database (the "WMSOra"/"dcssfc" schema
/// holder standard plants use, or the SQL-Server-backed equivalent at El Paso — this class
/// covers the Oracle side only). Configurable from Settings rather than hardcoded so the
/// same build can point at a test or production instance without a rebuild.
/// </summary>
public class OracleConnectionSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
    public string ServiceName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(ServiceName) &&
        !string.IsNullOrWhiteSpace(Username);

    public string BuildConnectionString() =>
        $"User Id={Username};Password={Password};Data Source=//{Host}:{Port}/{ServiceName};";
}

/// <summary>
/// One row from a query, keyed by column name — the .NET analogue of a Progress buffer
/// after a "find"/"for each": a flat bag of field values, not a typed record, since the
/// tables this reads (mm_e1maram*, relabel_requests, etc. — see
/// project_oracle_schema_holder_pipeline in the migration notes) aren't modeled as C#
/// classes anywhere in this port.
/// </summary>
public interface IOracleInterfaceService
{
    /// <summary>Read rows — the equivalent of Progress's "for each"/"find" against a live
    /// schema-holder table. Always parameterized; never build <paramref name="sql"/> by
    /// concatenating caller-supplied values.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Insert/update/delete — the equivalent of Progress's "create"/"assign"/
    /// "delete" against a live schema-holder table. Returns rows affected.</summary>
    Task<int> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens and immediately closes a connection — for a Settings "Test Connection"
    /// button, not for anything on the hot path.</summary>
    Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Calls an Oracle stored procedure shaped like the ones Progress's
    /// sendprod-orawms.p uses (p_insert_interface_receipt / p_insert_interface_inv_update):
    /// one input string parameter named DATA_IN, one output string parameter named RESULT.
    /// Returns RESULT as-is — callers parse it the way Progress does (first 3 chars =
    /// SUC/DUP/ERR).</summary>
    Task<string> CallInterfaceProcedureAsync(string procedureName, string dataIn, CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the Oracle interface DB directly via ODP.NET (Oracle.ManagedDataAccess) — the
/// same tables the Progress schema-holder connection (WMSOra + dcssfc) reads/writes as plain
/// buffers, e.g. relabel_requests or the mm_e1maram*/mm_e1afkol* IDoc staging tables. See
/// project_oracle_schema_holder_pipeline in the migration notes for which tables are real and
/// what they're for.
///
/// Deliberately thin: generic parameterized query/execute, not per-table methods, since which
/// tables StandAlone actually needs to touch hasn't been decided yet. Callers own the SQL.
///
/// Known constraints (not handled here — read before pointing this at a shared environment):
///  - No locking/coordination with the real Progress batch daemons (dtbat1005-ora.p,
///    dtbat203.p, dtord003.p, dtlbl040.p) that also poll these tables. Progress uses explicit
///    exclusive-lock/no-wait patterns before touching shared staging rows; concurrent writes
///    from here without equivalent care (e.g. "select ... for update") can race with them.
///  - Status/flag columns (e.g. mm_e1maram_trigger.mmt_rec_status) are meaningful to those
///    daemons' own state machines (' '=new, '9'=processing, '1'=done, 'E'=error) — writing a
///    value outside that set can leave a row permanently invisible to the real loader.
///  - No credential protection beyond whatever Settings already does for other fields
///    (plaintext JSON on disk) — see AppSettings.OraclePassword.
/// </summary>
public class OracleInterfaceService : IOracleInterfaceService
{
    private readonly OracleConnectionSettings _settings;

    public OracleInterfaceService(OracleConnectionSettings settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = new OracleConnection(_settings.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<int> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = new OracleConnection(_settings.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return rowsAffected;
    }

    public async Task<string> CallInterfaceProcedureAsync(string procedureName, string dataIn, CancellationToken cancellationToken = default)
    {
        using var connection = new OracleConnection(_settings.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.CommandText = procedureName;

        command.Parameters.Add(new OracleParameter("DATA_IN", OracleDbType.Varchar2, dataIn, System.Data.ParameterDirection.Input));
        var resultParam = new OracleParameter("RESULT", OracleDbType.Varchar2, 300, null, System.Data.ParameterDirection.Output);
        command.Parameters.Add(resultParam);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return resultParam.Value switch
        {
            OracleString os when !os.IsNull => os.ToString(),
            string s => s,
            _ => string.Empty,
        };
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new OracleConnection(_settings.BuildConnectionString());
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SYSDATE FROM DUAL";
            await command.ExecuteScalarAsync(cancellationToken);

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void AddParameters(OracleCommand command, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
            return;

        foreach (var (name, value) in parameters)
            command.Parameters.Add(new OracleParameter(name, value ?? DBNull.Value));
    }
}
