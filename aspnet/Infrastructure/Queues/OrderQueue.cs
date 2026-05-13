using System.Collections.Concurrent;
using DemoAPI.DTOs;

namespace DemoAPI.Infrastructure.Queues
{
    public class OrderQueue
    {
        private readonly ConcurrentQueue<OrderQueueItem> _queue = new();

        public void Enqueue(OrderQueueItem item)
        {
            _queue.Enqueue(item);
        }

        public bool TryDequeue(out OrderQueueItem? item)
        {
            return _queue.TryDequeue(out item);
        }
    }
}
