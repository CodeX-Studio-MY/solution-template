using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Saaskit.Core.Data;

/// <summary>Runs provider-written SQL on a context's connection and current transaction. Provider packages only.</summary>
internal static class DbCommandRunner
{
    public static async Task<object?> ExecuteScalarAsync(
        DbContext db,
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
        }
        try
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The SQL is written by the provider package from model metadata; values are parameters.
            command.CommandText = sql;
#pragma warning restore CA2100
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var (name, value) in parameters)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (opened)
                await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>The table and column names of <see cref="SequenceCounter"/> in this context, after naming conventions.</summary>
    public static (string Schema, string Table, string Name, string Value) SequenceCounterNames(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(SequenceCounter))
            ?? throw new InvalidOperationException($"{db.GetType().Name} does not map SequenceCounter.");
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema()!;
        var store = StoreObjectIdentifier.Table(table, schema);
        return (
            schema,
            table,
            entity.FindProperty(nameof(SequenceCounter.Name))!.GetColumnName(store)!,
            entity.FindProperty(nameof(SequenceCounter.Value))!.GetColumnName(store)!);
    }
}
