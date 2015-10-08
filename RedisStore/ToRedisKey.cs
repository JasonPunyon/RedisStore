using System;
using System.Linq;
using System.Reflection;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    public static class ToRedisKey<T>
    {
        public static Func<T, RedisKey> Implementation;
        public static FieldInfo ImplField;
        public static MethodInfo Invoke;

        static ToRedisKey()
        {
            ImplField = typeof(ToRedisKey<T>).GetField("Implementation");
            Invoke = typeof(Func<T, RedisKey>).GetMethod("Invoke");

            var il = Emit<Func<T, RedisKey>>.NewDynamicMethod();

            il.LoadArgument(0);

            var implicitOrExplicitConversion = typeof (RedisKey).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == typeof (T));

            if (implicitOrExplicitConversion != null)
            {
                il.Call(implicitOrExplicitConversion);
            }

            il.Return();

            Implementation = il.CreateDelegate();
        } 
    }
}