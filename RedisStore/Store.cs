using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace RedisStore
{
    public static class Store
    {
        public static ConnectionMultiplexer Connection;
        public static IDatabase Database => Connection.GetDatabase();

        public static T Create<T>()
        {
            try
            {
                return Implementer<T>.Create();
            }
            catch (TypeInitializationException ex)
            {
                throw ex.InnerException;
            }
        }

        public static T Get<T>(int id)
        {
            return Implementer<T>.Get(id);
        }

        public static IEnumerable<T> Enumerate<T>()
        {
            return Implementer<T>.Enumerate();
        }
    }
}