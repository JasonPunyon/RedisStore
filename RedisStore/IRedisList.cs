using System.Collections.Generic;

namespace RedisStore
{
    public interface IRedisList<T> : IEnumerable<T>
    {
        int Count { get; }

        void PushHead(T item);
        void PushTail(T item);

        T PopHead();
        T PopTail();
    }
}