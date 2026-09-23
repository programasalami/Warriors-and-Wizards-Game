using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Npgsql;

namespace Common.Database;

// Batches writes for one model type through a single background worker instead
// of hitting Postgres once per call. Each type supplies its own upsert (insert
// on a new row, update on an existing one) via Init - Postgres has no generic
// reflection-based mapper the way LiteDB did, so this can't stay fully generic
// the way it used to be.
public static class DbWriter<T> where T : class {
    private const int MaxBatchSize = 500;
    private static readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
    private static Task _processingTask;
    private static bool _initialized;
    private static Func<NpgsqlConnection, T, Task> _upsertOne;

    public static void Init(Func<NpgsqlConnection, T, Task> upsertOne) {
        _upsertOne = upsertOne;
        _initialized = true;
        _processingTask = Task.Factory.StartNew(
            ProcessAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private static async Task ProcessAsync()
    {
        var reader = _channel.Reader;
        var batch = new List<T>(MaxBatchSize);
        // Reference equality: enqueued items are the same live mutable instances the
        // game holds, so writing one twice in a batch is redundant, not stale.
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);

        // Continuously drain whatever's queued and write it in one transaction instead
        // of one round trip per item - keeps the in-memory backlog (and crash-loss
        // window) small.
        while (await reader.WaitToReadAsync())
        {
            batch.Clear();
            seen.Clear();

            while (batch.Count < MaxBatchSize && reader.TryRead(out var item))
            {
                if (seen.Add(item))
                    batch.Add(item);
            }

            if (batch.Count == 0)
                continue;

            try
            {
                await using var conn = await DbClient.OpenConnectionAsync();
                await using var tx = await conn.BeginTransactionAsync();

                foreach (var item in batch)
                    await _upsertOne(conn, item);

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save batch of {batch.Count} {typeof(T).Name} item(s) to database. {ex.Message}");
            }
        }
    }

    public static async Task WriteAsync(T model) {
        if (!_initialized)
            throw new InvalidOperationException(
                $"DbWriter<{typeof(T).Name}>.Init() was never called - writes for this type would be queued and silently dropped forever.");

        await _channel.Writer.WriteAsync(model);
    }

    public static async Task StopAsync()
    {
        _channel.Writer.Complete();
        await _processingTask;
    }
}
