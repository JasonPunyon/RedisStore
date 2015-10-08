using System.Collections.Generic;

namespace RedisStore
{
    public interface IRedisSet<T> : IEnumerable<T>
    {
        bool Add(T value);
        int Count { get; }
        IEnumerable<T> Diff(params IRedisSet<T>[] sets);
        IEnumerable<T> Intersect(params IRedisSet<T>[] sets);
        IEnumerable<T> Union(params IRedisSet<T>[] sets);
        bool Contains(T value);
        void Remove(T element);

        //void Diff(IRedisSet<T> destination, params IRedisSet<T>[] sets);
        //void Intersect(IRedisSet<T> destination, params IRedisSet<T>[] sets);
        //void Union(IRedisSet<T> destination, params IRedisSet<T>[] sets);
        //void Move(T value, IRedisSet<T> destination);
        //IEnumerable<T> Pop(int count);
        //IEnumerable<T> RandomMember(int count);
    }
}