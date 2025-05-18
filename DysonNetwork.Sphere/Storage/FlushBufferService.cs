using System.Collections.Concurrent;

namespace DysonNetwork.Sphere.Storage;

public interface IFlushHandler<T>
{
    Task FlushAsync(IReadOnlyList<T> items);
}

public class FlushBufferService
{
    private readonly Dictionary<Type, object> _buffers = new();
    private readonly Lock _lockObject = new();

    private ConcurrentQueue<T> _GetOrCreateBuffer<T>()
    {
        var type = typeof(T);
        lock (_lockObject)
        {
            if (!_buffers.TryGetValue(type, out var buffer))
            {
                buffer = new ConcurrentQueue<T>();
                _buffers[type] = buffer;
            }
            return (ConcurrentQueue<T>)buffer;
        }
    }

    public void Enqueue<T>(T item)
    {
        var buffer = _GetOrCreateBuffer<T>();
        buffer.Enqueue(item);
    }

    public async Task FlushAsync<T>(IFlushHandler<T> handler)
    {
        var buffer = _GetOrCreateBuffer<T>();
        var workingQueue = new List<T>();

        while (buffer.TryDequeue(out var item))
        {
            workingQueue.Add(item);
        }

        if (workingQueue.Count == 0)
            return;

        try
        {
            await handler.FlushAsync(workingQueue);
        }
        catch (Exception)
        {
            // If flush fails, re-queue the items
            foreach (var item in workingQueue)
                buffer.Enqueue(item);
            throw;
        }
    }

    public int GetPendingCount<T>()
    {
        var buffer = _GetOrCreateBuffer<T>();
        return buffer.Count;
    }
}