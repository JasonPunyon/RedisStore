using System;

namespace RedisStore
{
    public class NoIdPropertyException : Exception
    {
        public Type Type { get; set; }

        public override string Message => $"Add an Id property to {Type.Name} in order to use it with RedisStore.";

        public NoIdPropertyException(Type type)
        {
            Type = type;
        }
    }
}