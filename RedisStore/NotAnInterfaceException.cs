using System;

namespace RedisStore
{
    public class NotAnInterfaceException : Exception
    {
        public Type Type { get; set; }

        public override string Message => $"{Type.Name} cannot be used with RedisStore because it isn't an interface.";

        public NotAnInterfaceException(Type type)
        {
            Type = type;
        }
    }
}