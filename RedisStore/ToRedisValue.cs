using System;
using System.Linq;
using System.Reflection;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    public static class ToRedisValue<T>
    {
        public static Func<T, RedisValue> Implementation;
        public static FieldInfo ImplField;
        public static MethodInfo Invoke;

        static ToRedisValue()
        {
            ImplField = typeof (ToRedisValue<T>).GetField("Implementation");
            Invoke = typeof(Func<T, RedisValue>).GetMethod("Invoke");

            var il = Emit<Func<T, RedisValue>>.NewDynamicMethod();

            //Case 1: The type we need to convert is implicitly or explicitly convertible to RedisValue;
            //Look for an implicit or explicit conversion that takes the type as a parameter.
            var implicitOrExplicitConversion = typeof(RedisValue).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == typeof(T));

            if (implicitOrExplicitConversion != null)
            {
                il.LoadArgument(0);
                il.Call(implicitOrExplicitConversion);
                il.Return();
                Implementation = il.CreateDelegate();
                return;
            }

            //Case 2: The type is an interface with a Get Only Id Property that is implicitly or explicitly convertible to redis value.
            if (typeof (T).IsInterface && typeof (T).GetProperty("Id") != null)
            {
                var idProp = typeof (T).GetProperty("Id");
                var idConversion = typeof (RedisValue).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == idProp.PropertyType);

                if (idConversion != null)
                {
                    il.LoadArgument(0);
                    il.CallVirtual(typeof(T).GetProperty("Id").GetGetMethod());
                    il.Call(idConversion);
                    il.Return();
                    Implementation = il.CreateDelegate();
                    return;
                }
            }

            if (typeof (T) == typeof (DateTime))
            {
                il.LoadArgumentAddress(0);
                il.Call(typeof (DateTime).GetMethod("ToBinary"));
                il.Call(Methods.LongToRedisValue);
                il.Return();
                Implementation = il.CreateDelegate();
                return;
            }
        }
    }
}