using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class RedisListImplementer<T>
    {
        public static Type RedisListType;

        private static TypeBuilder _redisListType;
        private static Type _t;

        static RedisListImplementer()
        {
            _t = typeof (T);

            _redisListType = Implementer.mb.DefineType($"{_t.Name}_RedisList", TypeAttributes.Public);
            _redisListType.AddInterfaceImplementation(typeof (IRedisList<T>));
            _redisListType.AddInterfaceImplementation(typeof (IEnumerable<T>));
            _redisListType.AddInterfaceImplementation(typeof (IEnumerable));

            var key = _redisListType.DefineField("Key", typeof (string), FieldAttributes.Public);

            var add = Emit<Action<T>>.BuildInstanceMethod(_redisListType, "Add", Implementer.MethodAttributes);

            add.Call(Methods.GetDatabase);
            add.LoadArgument(0);
            add.LoadField(key);
            add.Call(Methods.StringToRedisKey);

            //Value
            add.LoadArgument(1);

            //Turn the value into a RedisValue;
            //Either it's implicitly or explicitly convertible to RedisValue...

            var implicitOrExplicitConversion = typeof(RedisValue).GetMethods().FirstOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == _t);
            if (implicitOrExplicitConversion != null)
            {
                add.Call(implicitOrExplicitConversion);
            } else if (_t.GetProperty("Id") != null && _t.GetProperty("Id").PropertyType == typeof (int)) //Or it's got an integer Id Property.
            {
                add.Call(_t.GetProperty("Id").GetGetMethod());
                add.Call(Methods.IntToRedisValue);
            }

            add.LoadConstant(0);
            add.LoadConstant(0);
            add.CallVirtual(typeof(IDatabase).GetMethod("ListLeftPush", new[] { typeof(RedisKey), typeof(RedisValue), typeof(When), typeof(CommandFlags) }));
            add.Pop();
            add.Return();

            add.CreateMethod();

            var typedGetEnumerator = Emit<Func<IEnumerator<T>>>.BuildInstanceMethod(_redisListType, "GetEnumerator", Implementer.MethodAttributes);

            typedGetEnumerator.Call(Methods.GetDatabase);
            typedGetEnumerator.LoadArgument(0);
            typedGetEnumerator.LoadField(key);
            typedGetEnumerator.Call(Methods.StringToRedisKey);
            typedGetEnumerator.LoadConstant(0L);
            typedGetEnumerator.LoadConstant(-1L);
            typedGetEnumerator.LoadConstant(0);
            typedGetEnumerator.Call(Methods.ListRange);

            var implicitOrExplicitConversion2 = typeof (RedisValue).GetMethods().FirstOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == _t);
            if (implicitOrExplicitConversion2 != null)
            {
                typedGetEnumerator.LoadNull();
                typedGetEnumerator.LoadFunctionPointer(implicitOrExplicitConversion2);
                typedGetEnumerator.NewObject(typeof (Func<RedisValue, T>), typeof (object), typeof (IntPtr));
            }
            else if (_t.GetProperty("Id") != null && _t.GetProperty("Id").PropertyType == typeof (int))
            {
                typedGetEnumerator.LoadField(typeof (Implementer<T>).GetField("FromRedisValue"));
            }

            typedGetEnumerator.Call(Methods.EnumerableSelect.MakeGenericMethod(typeof (RedisValue), _t));
            typedGetEnumerator.CallVirtual(typeof (IEnumerable<T>).GetMethod("GetEnumerator"));
            typedGetEnumerator.Return();

            typedGetEnumerator.CreateMethod();

            var getEnumerator = Emit<Func<IEnumerator>>.BuildInstanceMethod(_redisListType, "GetEnumerator", Implementer.MethodAttributes);
            getEnumerator.LoadArgument(0);
            getEnumerator.Call(typeof (IEnumerable<T>).GetMethod("GetEnumerator"));
            getEnumerator.Return();

            getEnumerator.CreateMethod();

            ImplementCount(key);

            RedisListType = _redisListType.CreateType();
        }

        static void ImplementCount(FieldInfo key)
        {
            var count = _redisListType.DefineProperty("Count", PropertyAttributes.None, CallingConventions.HasThis, typeof (int), Type.EmptyTypes);

            var getIl = Emit<Func<int>>.BuildInstanceMethod(_redisListType, "get_Count", Implementer.MethodAttributes);

            getIl.Call(Methods.GetDatabase);
            getIl.LoadArgument(0);
            getIl.LoadField(key);
            getIl.Call(Methods.StringToRedisKey);
            getIl.LoadConstant(0);
            getIl.CallVirtual(typeof (IDatabase).GetMethod("ListLength"));
            getIl.ConvertOverflow<int>();
            getIl.Return();

            count.SetGetMethod(getIl.CreateMethod());
        }
    }
}