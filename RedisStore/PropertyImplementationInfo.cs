using System;

namespace RedisStore
{
    class PropertyImplementationInfo
    {
        public Type DeclaringType { get; set; }
        public Type PropertyType { get; set; }
        public string Name { get; set; }
        public string RedisKeyFormat { get; set; }
    }
}