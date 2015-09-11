using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace RedisStore
{
    static class ShittyGlobalClass
    {
        public static List<Type> ValidPropertyTypes => typeof (RedisValue).GetMethods().Where(o => o.Name.In("op_Explicit", "op_Implicit") && o.GetParameters()[0].ParameterType == typeof (RedisValue)).Select(p => p.ReturnType).ToList();
        public static string ValidTypeList => $"Valid types are: {string.Join(",", ValidPropertyTypes.Select(o => o.IsGenericType && o.GetGenericTypeDefinition() == (typeof(Nullable<>)) ? o.GenericTypeArguments[0].Name + "?" : o.Name).OrderBy(o => o))}";
    }
}