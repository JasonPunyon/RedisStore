using System;
using System.Linq;
using System.Reflection;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    public static class FromRedisKey<T>
    {
        public static Func<RedisKey, T> Implementation;
        public static FieldInfo ImplField;
        public static MethodInfo Invoke;

        static FromRedisKey()
        {
            ImplField = typeof(FromRedisKey<T>).GetField("Implementation");
            Invoke = typeof(Func<RedisKey, T>).GetMethod("Invoke");

            var il = Emit<Func<RedisKey, T>>.NewDynamicMethod();

            il.LoadArgument(0);

            var implicitOrExplicitConversion = typeof (RedisKey).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == typeof (T));

            if (implicitOrExplicitConversion != null)
            {
                il.Call(implicitOrExplicitConversion);
            }

            il.Return();

            Implementation = il.CreateDelegate();
        }
    }
}