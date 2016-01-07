using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sigil;
using StackExchange.Redis;

namespace RedisStore
{
    public static class Store
    {
        public static ConnectionMultiplexer Connection;
        public static IDatabase Database => Connection.GetDatabase();

        public static void DumpAssembly()
        {
            Implementer.ab.Save("StoreImplementations.dll");
        }

        public static T Create<T>(object key = null)
        {
            try
            {
                return Implementer<T>.Create.Value(key);
            }
            catch (TypeInitializationException ex)
            {
                throw ex.InnerException;
            }
        }

        public static Task<T> CreateAsync<T>(object key = null)
        {
            return Implementer<T>.CreateAsync.Value(key);
        }

        public static T Get<T>(object id)
        {
            return Implementer<T>.Get.Value(id);
        }

        public static IEnumerable<T> Enumerate<T>()
        {
            return Implementer<T>.Enumerate.Value();
        }


        //Accepts UniqueIndex Queries of the forms:
        //  o => o.UniquelyIndexedField == literal
        //  o => o.UniquelyIndexedField == capturedVariable

        private static ConcurrentDictionary<Type, ConcurrentDictionary<MemberInfo, Func<object, RedisValue>>> funcs = new ConcurrentDictionary<Type, ConcurrentDictionary<MemberInfo, Func<object, RedisValue>>>();
        
        internal static IEnumerable<T> IndexQuery<T>(Expression<Func<T, bool>> predicate)
        {
            var binaryExpression = predicate.Body as BinaryExpression;
            if (binaryExpression == null)
            {
                throw new InvalidOperationException();
            }

            var prop = binaryExpression.Left as MemberExpression;
            if (prop == null)
            {
                throw new InvalidOperationException();
            }

            var attr = prop.Member.GetCustomAttribute<UniqueIndexAttribute>();
            if (attr == null)
            {
                throw new InvalidOperationException();
            }

            if (binaryExpression.NodeType != ExpressionType.Equal)
            {
                throw new InvalidOperationException();
            }

            var constantExpression = binaryExpression.Right as ConstantExpression;
            if (constantExpression != null)
            {
                Func<object, RedisValue> func;
                if (!funcs.ContainsKey(typeof (T)))
                {
                    funcs[typeof (T)] = new ConcurrentDictionary<MemberInfo, Func<object, RedisValue>>();
                }

                if (!funcs[typeof (T)].ContainsKey(prop.Member))
                {
                    var il = Emit<Func<object, RedisValue>>.NewDynamicMethod();

                    var memberType = prop.Member.MemberType == MemberTypes.Property
                        ? prop.Member.DeclaringType.GetProperty(prop.Member.Name).PropertyType
                        : null;

                    var toRedisValue = typeof(ToRedisValue<>).MakeGenericType(memberType);
                    var impl = toRedisValue.GetField("Implementation");
                    var invoke = (MethodInfo)toRedisValue.GetField("Invoke").GetValue(null);

                    il.Call(Methods.GetDatabase);
                    il.LoadConstant($"/{typeof (T).Name}/{prop.Member.Name}_UIx");
                    il.Call(Methods.StringToRedisKey);
                    il.LoadField(impl);
                    il.LoadArgument(0);

                    if (memberType.IsValueType)
                    {
                        il.UnboxAny(memberType);
                    }
                    else
                    {
                        il.CastClass(memberType);
                    }

                    il.Call(invoke);
                    il.LoadConstant(0);
                    il.Call(Methods.HashGet);
                    il.Return();

                    func = funcs[typeof (T)][prop.Member] = il.CreateDelegate();
                }
                else
                {
                    func = funcs[typeof (T)][prop.Member];
                }

                var thing = func(constantExpression.Value);

                //var constantValue = (string)constantExpression.Value;
                //var thing = Database.HashGet($"/{typeof(T).Name}/{prop.Member.Name}_UIx", constantValue);
                if (!thing.IsNull)
                {
                    yield return FromRedisValue<T>.Implementation.Value(thing);
                }
            }

            var memberExp = binaryExpression.Right as MemberExpression;
            if (memberExp != null)
            {
                Func<object, RedisValue> func = null;

                if (!funcs.ContainsKey(typeof (T)))
                {
                    funcs[typeof(T)] = new ConcurrentDictionary<MemberInfo, Func<object, RedisValue>>();
                }

                if (!funcs[typeof (T)].ContainsKey(memberExp.Member))
                {
                    var il = Emit<Func<object, RedisValue>>.NewDynamicMethod();
                    var field = memberExp.Member.DeclaringType.GetField(memberExp.Member.Name);
                    var memberType = field.FieldType;

                    var toRedisValue = typeof(ToRedisValue<>).MakeGenericType(memberType);
                    var impl = toRedisValue.GetField("Implementation");
                    var invoke = (MethodInfo)toRedisValue.GetField("Invoke").GetValue(null);

                    il.Call(Methods.GetDatabase);
                    il.LoadConstant($"/{typeof(T).Name}/{prop.Member.Name}_UIx");
                    il.Call(Methods.StringToRedisKey);
                    il.LoadField(impl);
                    il.LoadArgument(0);

                    il.CastClass(field.DeclaringType);
                    il.LoadField(field);

                    il.Call(invoke);
                    il.LoadConstant(0);
                    il.Call(Methods.HashGet);
                    il.Return();

                    func = funcs[typeof (T)][memberExp.Member] = il.CreateDelegate();
                }

                var constant = memberExp.Expression as ConstantExpression;
                var thing = func(constant.Value);
                if (!thing.IsNull)
                {
                    yield return FromRedisValue<T>.Implementation.Value(thing);
                }
            }
        }
    }
}