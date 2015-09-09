using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Sigil;
using Sigil.NonGeneric;
using StackExchange.Redis;

namespace RedisStore
{
    public class NoIdPropertyException : Exception
    {
        public Type Type { get; set; }

        public override string Message => $"Add an Id property to {Type.Name} in order to use it with RedisStore.";

        public NoIdPropertyException(Type type)
        {
            Type = type;
        }
    }

    public class NotAnInterfaceException : Exception
    {
        public Type Type { get; set; }

        public override string Message => $"{Type.Name} cannot be used with ";

        public NotAnInterfaceException(Type type)
        {
            Type = type;
        }
    }

    public class InvalidPropertyTypeException : Exception
    {
        private readonly PropertyImplementationInfo _propInfo;

        public override string Message => $"{_propInfo.DeclaringType.Name}.{_propInfo.Name} has an invalid type. {ShittyGlobalClass.ValidTypeList}";

        internal InvalidPropertyTypeException(PropertyImplementationInfo propInfo)
        {
            _propInfo = propInfo;
        }
    }

    static class ShittyGlobalClass
    {
        public static List<Type> ValidPropertyTypes => typeof (RedisValue).GetMethods().Where(o => o.Name.In("op_Explicit", "op_Implicit") && o.GetParameters()[0].ParameterType == typeof (RedisValue)).Select(p => p.ReturnType).ToList();

        public static string ValidTypeList => $"Valid types are: {string.Join(",", ValidPropertyTypes.Select(o => o.IsGenericType && o.GetGenericTypeDefinition() == (typeof(Nullable<>)) ? o.GenericTypeArguments[0].Name + "?" : o.Name).OrderBy(o => o))}";
    }

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    static class TypeImplementer<TInterface>
    {
        public static Func<IDatabase, TInterface> Create;

        static readonly string TypeName;
        static readonly Dictionary<string, PropertyImplementationInfo> Properties;
        static MethodAttributes _methodAttributes;

        static TypeImplementer()
        {
            var type = typeof(TInterface);

            if (!type.IsInterface)
            {
                throw new NotAnInterfaceException(typeof(TInterface));
            }

            if (!type.GetProperties().Any(o => o.Name == "Id"))
            {
                throw new NoIdPropertyException(typeof(TInterface));
            }

            Properties = type
                .GetProperties()
                .Select(p => new PropertyImplementationInfo
                {
                    DeclaringType = typeof(TInterface),
                    Name = p.Name,
                    PropertyType = p.PropertyType,
                    RedisKeyFormat = $"/{typeof(TInterface).Name}/{{0}}/{p.Name}"
                }).ToDictionary(o => o.Name);

            var propsWithInvalidTypes = Properties.Where(p => !ShittyGlobalClass.ValidPropertyTypes.Contains(p.Value.PropertyType)).ToList();
            if (propsWithInvalidTypes.Any())
            {
                throw new AggregateException(propsWithInvalidTypes.Select(p => new InvalidPropertyTypeException(p.Value)));
            }

            TypeName = $"{type.Namespace}.{type.Name}_Implementation";

            ImplementType();
        }

        static void ImplementType()
        {
            var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(TypeName), AssemblyBuilderAccess.RunAndSave);
            var mb = ab.DefineDynamicModule("module", $"{TypeName}.dll");
            var tb = mb.DefineType("Store_User", TypeAttributes.Public);
            tb.AddInterfaceImplementation(typeof(TInterface));

            var _redis = tb.DefineField("_redis", typeof(IDatabase), FieldAttributes.Private);

            var constructor = Emit<Action<IDatabase>>.BuildConstructor(tb, MethodAttributes.Public);
            constructor.LoadArgument(0);
            constructor.Call(typeof(object).GetConstructor(Type.EmptyTypes));
            constructor.LoadArgument(0);
            constructor.LoadArgument(1);
            constructor.StoreField(_redis);
            constructor.Return();

            constructor.CreateConstructor();

            var _id = tb.DefineField("_id", typeof(int), FieldAttributes.Private);

            _methodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.HideBySig;

            {
                var idPropertyInfo = Properties["Id"];
                var idProperty = tb.DefineProperty("Id", PropertyAttributes.None, idPropertyInfo.PropertyType, Type.EmptyTypes);

                var getter = Emit.BuildInstanceMethod(idPropertyInfo.PropertyType, Type.EmptyTypes, tb, "get_Id", _methodAttributes);
                getter.LoadArgument(0);
                getter.LoadField(_id);
                getter.Return();
                var get = getter.CreateMethod();

                idProperty.SetGetMethod(get);

                var setter = Emit.BuildInstanceMethod(typeof(void), new[] { idProperty.PropertyType }, tb, "set_Id", _methodAttributes);
                setter.LoadArgument(0);
                setter.LoadArgument(1);
                setter.StoreField(_id);
                setter.Return();
                var set = setter.CreateMethod();

                idProperty.SetSetMethod(set);
            }            

            //Implement all the other properties. These are the ones we go back and forth to redis to get.
            foreach (var property in Properties.Where(o => o.Key != "Id").Select(o => o.Value))
            {
                var prop = tb.DefineProperty(property.Name, PropertyAttributes.None, property.PropertyType, Type.EmptyTypes);

                var getter = Emit.BuildInstanceMethod(property.PropertyType, Type.EmptyTypes, tb, $"get_{property.Name}", _methodAttributes);

                getter.LoadArgument(0);
                getter.LoadField(_redis);
                getter.LoadConstant(property.RedisKeyFormat);
                getter.LoadArgument(0);
                getter.LoadField(_id);
                getter.Box<int>();
                getter.Call(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }));
                getter.Call(typeof(RedisKey).GetMethod("op_Implicit", new[] { typeof(string) }));
                getter.LoadConstant(0);
                getter.CallVirtual(typeof(IDatabase).GetMethod("StringGet", new[] { typeof(RedisKey), typeof(CommandFlags) }));

                var method = typeof(RedisValue).GetMethods().First(o => o.Name.In("op_Explicit", "op_Implicit") && o.ReturnType == property.PropertyType);

                getter.Call(method);
                getter.Return();

                var get_Name = getter.CreateMethod();
                prop.SetGetMethod(get_Name);

                var setName = Emit.BuildInstanceMethod(typeof(void), new[] { property.PropertyType }, tb, $"set_{property.Name}", _methodAttributes);
                var timeSpan = setName.DeclareLocal<TimeSpan?>();

                setName.LoadArgument(0);
                setName.LoadField(_redis);
                setName.LoadConstant(property.RedisKeyFormat);
                setName.LoadArgument(0);
                setName.LoadField(_id);
                setName.Box<int>();
                setName.Call(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }));
                setName.Call(typeof(RedisKey).GetMethod("op_Implicit", new[] { typeof(string) }));
                setName.LoadArgument(1);
                setName.Call(typeof(RedisValue).GetMethod("op_Implicit", new[] { property.PropertyType }));
                setName.LoadLocalAddress(timeSpan);
                setName.InitializeObject<TimeSpan?>();
                setName.LoadLocal(timeSpan);
                setName.LoadConstant(0);
                setName.LoadConstant(0);
                setName.CallVirtual(typeof(IDatabase).GetMethod("StringSet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(TimeSpan?), typeof(When), typeof(CommandFlags) }));
                setName.Pop();
                setName.Return();

                var set_Name = setName.CreateMethod();
                prop.SetSetMethod(set_Name);
            }

            var result = tb.CreateType();
            ab.Save($"{TypeName}.dll");

            var create = Emit<Func<IDatabase, TInterface>>.NewDynamicMethod();

            create.LoadArgument(0);
            create.NewObject(result, new [] {typeof(IDatabase)});
            create.Return();

            Create = create.CreateDelegate();
        }
    }

    class PropertyImplementationInfo
    {
        public Type DeclaringType { get; set; }
        public Type PropertyType { get; set; }
        public string Name { get; set; }
        public string RedisKeyFormat { get; set; }
    }

    static class Extensions
    {
        public static bool In<T>(this T element, params T[] source)
        {
            return source.Contains(element);
        }

        public static bool NotIn<T>(this T element, params T[] source)
        {
            return !element.In(source);
        }
    }

    public class Store
    {
        readonly ConnectionMultiplexer _redisConn;
        private readonly IDatabase _redisDatabase;

        public Store(ConnectionMultiplexer redisConn)
        {
            _redisConn = redisConn;
            _redisDatabase = redisConn.GetDatabase();
        }

        public T Create<T>()
        {
            try
            {
                return TypeImplementer<T>.Create(_redisDatabase);
            }
            catch (TypeInitializationException ex)
            {
                throw ex.InnerException;
            }
        }
    }
}