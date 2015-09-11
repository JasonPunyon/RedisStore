using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace RedisStore
{
    public static class ConnectionMultiplexerExtensions
    {
        public static T Create<T>(this ConnectionMultiplexer con)
        {
            try
            {
                return Implementer<T>.Create(con.GetDatabase());
            }
            catch (TypeInitializationException ex)
            {
                throw ex.InnerException;
            }
        }

        public static bool Exists<T>(this ConnectionMultiplexer con, object id)
        {
            return Implementer<T>.Exists(con.GetDatabase(), id);
        }

        public static T Get<T>(this ConnectionMultiplexer con, object id)
        {
            return Implementer<T>.Get(con.GetDatabase(), id);
        }

        public static IEnumerable<T> Enumerate<T>(this ConnectionMultiplexer con)
        {
            return Implementer<T>.Enumerate(con.GetDatabase());
        }

        internal static bool Delete<T>(this ConnectionMultiplexer con, T toDelete)
        {
            return Implementer<T>.Delete(con.GetDatabase(), toDelete);
        }
    }
}