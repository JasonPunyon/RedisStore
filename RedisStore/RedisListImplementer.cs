using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class RedisListImplementer<T>
    {
        public static Lazy<Type> ImplementedType = new Lazy<Type>(ImplementType);

        private static TypeBuilder _redisListType;
        private static Type _t;
        private static FieldInfo _key;

        static Type ImplementType()
        {
            _t = typeof (T);

            _redisListType = Implementer.mb.DefineType($"{_t.Name}_RedisList", TypeAttributes.Public);
            _redisListType.AddInterfaceImplementation(typeof (IRedisList<T>));
            _redisListType.AddInterfaceImplementation(typeof (IEnumerable<T>));
            _redisListType.AddInterfaceImplementation(typeof (IEnumerable));

            _key = _redisListType.DefineField("Key", typeof (string), FieldAttributes.Public);

            ImplementIEnumerable();
            ImplementCount();
            ImplementPushHead();
            ImplementPushTail();
            ImplementPopHead();
            ImplementPopTail();

            return _redisListType.CreateType();
        }

        static void LoadKeyFieldAsRedisKey<Q>(Emit<Q> il)
        {
            il.LoadArgument(0);
            il.LoadField(_key);
            il.Call(Methods.StringToRedisKey);
        }

        static void ImplementIEnumerable()
        {
            var typedIl = Emit<Func<IEnumerator<T>>>.BuildInstanceMethod(_redisListType, "GetEnumerator", Implementer.MethodAttributes);

            typedIl.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(typedIl);
            typedIl.LoadConstant(0L);
            typedIl.LoadConstant(-1L);
            typedIl.LoadConstant(0);
            typedIl.Call(Methods.ListRange);

            typedIl.LoadField(FromRedisValue<T>.ImplField);
            typedIl.Call(typeof (Lazy<Func<RedisValue, T>>).GetMethod("get_Value"));

            typedIl.Call(Methods.EnumerableSelect<RedisValue, T>());
            typedIl.CallVirtual(typeof(IEnumerable<T>).GetMethod("GetEnumerator"));
            typedIl.Return();

            typedIl.CreateMethod();

            var il = Emit<Func<IEnumerator>>.BuildInstanceMethod(_redisListType, "GetEnumerator", Implementer.MethodAttributes);
            il.LoadArgument(0);
            il.Call(typeof(IEnumerable<T>).GetMethod("GetEnumerator"));
            il.Return();

            il.CreateMethod();
        }

        static void ImplementCount()
        {
            var count = _redisListType.DefineProperty("Count", PropertyAttributes.None, CallingConventions.HasThis, typeof (int), Type.EmptyTypes);

            var getIl = Emit<Func<int>>.BuildInstanceMethod(_redisListType, "get_Count", Implementer.MethodAttributes);

            getIl.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(getIl);
            getIl.LoadConstant(0);
            getIl.CallVirtual(typeof (IDatabase).GetMethod("ListLength"));
            getIl.ConvertOverflow<int>();
            getIl.Return();

            count.SetGetMethod(getIl.CreateMethod());
        }

        static void ImplementPushHead()
        {
            var il = Emit<Action<T>>.BuildInstanceMethod(_redisListType, "PushHead", Implementer.MethodAttributes);

            il.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(il);

            //Value
            il.LoadField(ToRedisValue<T>.ImplField);
            il.LoadArgument(1);
            il.Call(ToRedisValue<T>.Invoke);

            il.LoadConstant(0);
            il.LoadConstant(0);
            il.CallVirtual(typeof(IDatabase).GetMethod("ListLeftPush", new[] { typeof(RedisKey), typeof(RedisValue), typeof(When), typeof(CommandFlags) }));
            il.Pop();
            il.Return();

            il.CreateMethod();

        }

        static void ImplementPushTail()
        {
            var il = Emit<Action<T>>.BuildInstanceMethod(_redisListType, "PushTail", Implementer.MethodAttributes);

            il.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(il);

            //Value
            il.LoadField(ToRedisValue<T>.ImplField);
            il.LoadArgument(1);
            il.Call(ToRedisValue<T>.Invoke);

            il.LoadConstant(0);
            il.LoadConstant(0);
            il.CallVirtual(typeof(IDatabase).GetMethod("ListRightPush", new[] { typeof(RedisKey), typeof(RedisValue), typeof(When), typeof(CommandFlags) }));
            il.Pop();
            il.Return();

            il.CreateMethod();
        }

        static void ImplementPopHead()
        {
            var il = Emit<Func<T>>.BuildInstanceMethod(_redisListType, "PopHead", Implementer.MethodAttributes);

            il.LoadField(FromRedisValue<T>.ImplField);
            il.Call(typeof(Lazy<Func<RedisValue, T>>).GetMethod("get_Value"));

            il.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(il);
            il.LoadConstant(0);
            il.Call(typeof (IDatabase).GetMethod("ListLeftPop"));

            il.Call(FromRedisValue<T>.Invoke);

            il.Return();
            il.CreateMethod();
        }

        static void ImplementPopTail()
        {
            var il = Emit<Func<T>>.BuildInstanceMethod(_redisListType, "PopTail", Implementer.MethodAttributes);

            il.LoadField(FromRedisValue<T>.ImplField);
            il.Call(typeof(Lazy<Func<RedisValue, T>>).GetMethod("get_Value"));

            il.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(il);
            il.LoadConstant(0);
            il.Call(typeof(IDatabase).GetMethod("ListRightPop"));

            il.Call(FromRedisValue<T>.Invoke);

            il.Return();
            il.CreateMethod();
        }
    }
}