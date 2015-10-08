using System;
using System.Linq;
using System.Reflection;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    public static class FromRedisValue<T>
    {
        public static Func<RedisValue, T> Implementation;
        public static FieldInfo ImplField;
        public static MethodInfo Invoke;

        static FromRedisValue()
        {
            ImplField = typeof(FromRedisValue<T>).GetField("Implementation");
            Invoke = typeof(Func<RedisValue, T>).GetMethod("Invoke");

            var implicitOrExplicitConversion = typeof (RedisValue).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == typeof (T));

            if (implicitOrExplicitConversion != null)
            {
                var il = Emit<Func<RedisValue, T>>.NewDynamicMethod();

                il.LoadArgument(0);
                il.Call(implicitOrExplicitConversion);
                il.Return();

                Implementation = il.CreateDelegate();
                return;
            }

            if (typeof (T).IsInterface && typeof (T).GetProperty("Id") != null)
            {
                var idProp = typeof (T).GetProperty("Id");
                var idConversion = typeof (RedisValue).GetMethods().SingleOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == idProp.PropertyType);

                if (idConversion != null)
                {
                    var il = Emit<Func<RedisValue, T>>.NewDynamicMethod();

                    il.NewObject(Implementer<T>.ImplementedType);
                    il.Duplicate();
                    il.LoadArgument(0);
                    il.Call(idConversion);
                    il.StoreField(Implementer<T>.ImplementedType.GetField("_id"));
                    il.Return();

                    Implementation = il.CreateDelegate();
                    return;
                }
            }
        }
    }
}