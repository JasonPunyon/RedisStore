using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Sigil;
using Sigil.NonGeneric;
using StackExchange.Redis;

namespace RedisStore
{
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class RedisSetImplementer<T>
    {
        public static Lazy<Type> ImplementedType = new Lazy<Type>(ImplementType);

        private static TypeBuilder _redisSetType;
        private static Type _t;
        private static FieldInfo _key;

        public static Lazy<Func<object, RedisKey>> AuxFunc = new Lazy<Func<object, RedisKey>>(ImplementAuxFunc);

        static Type ImplementType()
        {
            _t = typeof(T);

            _redisSetType = Implementer.mb.DefineType($"{_t.Name}_RedisSet", TypeAttributes.Public);
            _redisSetType.AddInterfaceImplementation(typeof(IRedisSet<T>));
            _redisSetType.AddInterfaceImplementation(typeof(IEnumerable<T>));
            _redisSetType.AddInterfaceImplementation(typeof(IEnumerable));

            _key = _redisSetType.DefineField("Key", typeof(string), FieldAttributes.Public);

            ImplementAdd();
            ImplementCount();
            ImplementUnion();
            ImplementIntersect();
            ImplementDiff();
            ImplementRemove();
            ImplementContains();
            ImplementIEnumerable();

            return _redisSetType.CreateType();
        } 

        static Func<object, RedisKey> ImplementAuxFunc()
        {
            var aux = Emit.NewDynamicMethod(typeof(RedisKey), new[] { typeof(object) });

            aux.LoadArgument(0);
            aux.CastClass(ImplementedType.Value);
            aux.LoadField(ImplementedType.Value.GetField("Key"));
            aux.Call(Methods.StringToRedisKey);
            aux.Return();

            return aux.CreateDelegate<Func<object, RedisKey>>();
        } 

        static void LoadKeyFieldAsRedisKey<Q>(Emit<Q> il)
        {
            il.LoadArgument(0);
            il.LoadField(_key);
            il.Call(Methods.StringToRedisKey);
        }

        static void ImplementAdd()
        {
            var add = Emit<Func<T, bool>>.BuildInstanceMethod(_redisSetType, "Add", Implementer.MethodAttributes);

            add.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(add);

            add.LoadField(ToRedisValue<T>.ImplField);
            add.LoadArgument(1);
            add.Call(ToRedisValue<T>.Invoke);

            add.LoadConstant(0);
            add.CallVirtual(typeof(IDatabase).GetMethod("SetAdd", new[] { typeof(RedisKey), typeof(RedisValue), typeof(CommandFlags) }));
            add.Return();

            add.CreateMethod();
        }

        static void ImplementCount()
        {
            var count = _redisSetType.DefineProperty("Count", PropertyAttributes.None, CallingConventions.HasThis, typeof (int), Type.EmptyTypes);

            var getIl = Emit<Func<int>>.BuildInstanceMethod(_redisSetType, "get_Count", Implementer.MethodAttributes);

            getIl.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(getIl);
            getIl.LoadConstant(0);
            getIl.CallVirtual(typeof(IDatabase).GetMethod("SetLength"));
            getIl.ConvertOverflow<int>();
            getIl.Return();

            count.SetGetMethod(getIl.CreateMethod());
        }

        static void ImplementSetCombine(SetOperation setOperation, string methodName)
        {
            //Now the union...
            var il = Emit<Func<IRedisSet<T>[], IEnumerable<T>>>.BuildInstanceMethod(_redisSetType, methodName, Implementer.MethodAttributes);

            il.Call(Methods.GetDatabase);
            il.LoadConstant((int)setOperation);

            //Map the incoming params to an array of string by 
            il.LoadArgument(1);

            il.Call(Methods.EnumerableCast<object>());
            il.LoadField(typeof(RedisSetImplementer<T>).GetField("AuxFunc"));
            il.Call(typeof (Lazy<Func<object, RedisKey>>).GetMethod("get_Value"));

            il.Call(Methods.EnumerableSelect<object, RedisKey>());

            //Concat the key for this object in here.
            LoadKeyFieldAsRedisKey(il);
            il.Call(typeof(Extensions).GetMethod("Concat").MakeGenericMethod(typeof(RedisKey)));
            il.Call(Methods.EnumerableToArray<RedisKey>());
            il.LoadConstant(0);

            il.CallVirtual(typeof(IDatabase).GetMethod("SetCombine", new[] { typeof(SetOperation), typeof(RedisKey[]), typeof(CommandFlags) }));

            //Now we have a RedisValue[]...
            //We have to map that to an IEnumerable<T>, which is very similar to what we have to do with Lists...
            //Load up the selector function...

            il.LoadField(FromRedisValue<T>.ImplField);
            il.Call(typeof(Lazy<Func<RedisValue, T>>).GetMethod("get_Value"));
            il.Call(Methods.EnumerableSelect<RedisValue, T>());
            il.Return();

            il.CreateMethod();
        }

        static void ImplementUnion()
        {
            ImplementSetCombine(SetOperation.Union, "Union");
        }

        static void ImplementDiff()
        {
            ImplementSetCombine(SetOperation.Difference, "Diff");
        }

        static void ImplementIntersect()
        {
            ImplementSetCombine(SetOperation.Intersect, "Intersect");
        }

        static void ImplementRemove()
        {
            var il = Emit<Action<T>>.BuildInstanceMethod(_redisSetType, "Remove", Implementer.MethodAttributes);

            il.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(il);

            il.LoadField(ToRedisValue<T>.ImplField);
            il.LoadArgument(1);
            il.Call(ToRedisValue<T>.Invoke);

            il.LoadConstant(0);
            il.CallVirtual(typeof (IDatabase).GetMethod("SetRemove", new[] {typeof (RedisKey), typeof (RedisValue), typeof (CommandFlags)}));
            il.Pop();

            il.Return();

            il.CreateMethod();
        }

        static void ImplementContains()
        {
            var il = Emit<Func<T, bool>>.BuildInstanceMethod(_redisSetType, "Contains", Implementer.MethodAttributes);

            il.Call(Methods.GetDatabase);

            LoadKeyFieldAsRedisKey(il);

            il.LoadField(ToRedisValue<T>.ImplField);
            il.LoadArgument(1);
            il.Call(ToRedisValue<T>.Invoke);

            il.LoadConstant(0);
            il.CallVirtual(typeof (IDatabase).GetMethod("SetContains", new[] {typeof (RedisKey), typeof (RedisValue), typeof (CommandFlags)}));
            il.Return();

            il.CreateMethod();
        }

        static void ImplementIEnumerable()
        {
            var typedGetEnumerator = Emit<Func<IEnumerator<T>>>.BuildInstanceMethod(_redisSetType, "GetEnumerator", Implementer.MethodAttributes);

            typedGetEnumerator.Call(Methods.GetDatabase);
            LoadKeyFieldAsRedisKey(typedGetEnumerator);
            typedGetEnumerator.LoadConstant(0);
            typedGetEnumerator.Call(Methods.SetMembers);

            typedGetEnumerator.LoadField(typeof (FromRedisValue<T>).GetField("Implementation"));
            typedGetEnumerator.Call(typeof(Lazy<Func<RedisValue, T>>).GetMethod("get_Value"));
            typedGetEnumerator.Call(Methods.EnumerableSelect<RedisValue, T>());
            typedGetEnumerator.CallVirtual(typeof(IEnumerable<T>).GetMethod("GetEnumerator"));
            typedGetEnumerator.Return();

            typedGetEnumerator.CreateMethod();

            var getEnumerator = Emit<Func<IEnumerator>>.BuildInstanceMethod(_redisSetType, "GetEnumerator", Implementer.MethodAttributes);
            getEnumerator.LoadArgument(0);
            getEnumerator.Call(typeof(IEnumerable<T>).GetMethod("GetEnumerator"));
            getEnumerator.Return();

            getEnumerator.CreateMethod();
        }
    }
}