using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Common.Database.Models;

namespace Common.Database;

public static class DbWriter<T> where T : class {
    private static readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
    private static Task _processingTask;
    private static bool _initialized;

    public static void Init() {
        _initialized = true;
        _processingTask = Task.Factory.StartNew(
            ProcessAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }
    
    private static async Task ProcessAsync()
    {
        // Continuously consume queued database writes off the main thread
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                DbClient.DbCon.GetCollection<T>().Upsert(item);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save item to database. {ex.Message}");
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