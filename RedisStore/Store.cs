using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace RedisStore
{
    public static class Store
    {
        public static ConnectionMultiplexer Connection;
        public static IDatabase Database => Connection.GetDatabase();

        public static void DumpAssembly()
        {
            Implementer.ab.Save("StoreImplementations.dll");
        }

        public static T Create<T>(object key = null)
        {
            try
            {
                return Implementer<T>.Create(key);
            }
            catch (TypeInitializationException ex)
            {
                throw ex.InnerException;
            }
        }

        public static T Get<T>(object id)
        {
            return Implementer<T>.Get(id);
        }

        public static IEnumerable<T> Enumerate<T>()
        {
            return Implementer<T>.Enumerate();
        }
    }
}