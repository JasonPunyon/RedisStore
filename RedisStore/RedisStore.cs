using System;
using System.Collections;
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

        public override string Message => $"{Type.Name} cannot be used with Rapper because it isn't an interface.";

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
        public static Func<IDatabase, IEnumerable<TInterface>> Enumerate;
        public static Func<IDatabase, object, TInterface> Get;
        public static Func<IDatabase, object, bool> Exists;
        public static Func<IDatabase, TInterface, bool> Delete;
         
        static readonly string TypeName;
        static readonly Dictionary<string, PropertyImplementationInfo> Properties;
        private static readonly MethodInfo StringToRedisKey = typeof (RedisKey).GetMethod("op_Implicit", new[] {typeof (string)});
        static MethodAttributes MethodAttributes => MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.HideBySig;

        private static MethodInfo StringDotFormat => typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) });

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

            ImplementConstructor(tb, _redis);

            var _id = tb.DefineField("_id", Properties["Id"].PropertyType, FieldAttributes.Private);

            ImplementIdProperty(tb, _id);

            //Implement all the other properties. These are the ones we go back and forth to redis to get.
            foreach (var property in Properties.Where(o => o.Key != "Id").Select(o => o.Value))
            {
                ImplementNonIdProperty(tb, property, _redis, _id);
            }

            var result = tb.CreateType();

            ImplementCreate(result, _id);

            ImplementGet(result, _id);
            ImplementExists();
            ImplementDelete(_id);

            if (_id.FieldType == typeof(int))
            {
                ImplementEnumerate(mb, result);
            }
        }

        private static void ImplementConstructor(TypeBuilder tb, FieldBuilder _redis)
        {
            var constructor = Emit<Action<IDatabase>>.BuildConstructor(tb, MethodAttributes.Public);
            constructor.LoadArgument(0);
            constructor.Call(typeof (object).GetConstructor(Type.EmptyTypes));
            constructor.LoadArgument(0);
            constructor.LoadArgument(1);
            constructor.StoreField(_redis);
            constructor.Return();

            constructor.CreateConstructor();
        }

        private static void ImplementCreate(Type result, FieldBuilder _id)
        {
            var create = Emit<Func<IDatabase, TInterface>>.NewDynamicMethod();

            var idFieldInfo = result.GetRuntimeFields().First(o => o.Name == "_id");

            var timespanLocal = create.DeclareLocal<TimeSpan?>();
            var createdLocal = create.DeclareLocal(result);

            create.LoadArgument(0);

            create.NewObject(result, typeof (IDatabase));
            create.StoreLocal(createdLocal);

            if (_id.FieldType == typeof (int))
            {
                create.LoadLocal(createdLocal);
                create.LoadArgument(0);
                create.LoadConstant($"/{typeof (TInterface).Name}/ID");
                create.Call(StringToRedisKey);
                create.LoadConstant(1);
                create.Convert<long>();
                create.LoadConstant(0);
                create.CallVirtual(typeof (IDatabase).GetMethod("StringIncrement", new[] {typeof (RedisKey), typeof (long), typeof (CommandFlags)}));
                create.Convert<int>();
                create.StoreField(idFieldInfo);
            }

            create.LoadArgument(0);
            create.LoadConstant($"/{typeof(TInterface).Name}/{{0}}");
            create.LoadLocal(createdLocal);
            create.LoadField(idFieldInfo);

            if (idFieldInfo.FieldType.IsValueType)
            {
                create.Box(idFieldInfo.FieldType);
            }

            create.Call(StringDotFormat);
            create.Call(StringToRedisKey);
            create.LoadConstant(true);

            var method =
                typeof(RedisValue).GetMethods()
                    .First(o => o.Name.In("op_Explicit", "op_Implicit") && o.GetParameters().First().ParameterType == typeof(bool));

            create.Call(method);
            create.LoadLocalAddress(timespanLocal);
            create.InitializeObject<TimeSpan?>();
            create.LoadLocal(timespanLocal);
            create.LoadConstant(0);
            create.LoadConstant(0);
            create.CallVirtual(typeof(IDatabase).GetMethod("StringSet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(TimeSpan?), typeof(When), typeof(CommandFlags) }));
            create.Pop();

            create.LoadLocal(createdLocal);
            create.Return();

            Create = create.CreateDelegate();
        }

        private static void ImplementDelete(FieldBuilder _id)
        {
            var delete = Emit<Func<IDatabase, TInterface, bool>>.NewDynamicMethod();

            delete.LoadArgument(0);
            delete.LoadConstant($"/{typeof (TInterface).Name}/{{0}}");
            delete.LoadArgument(1);
            delete.Call(typeof(TInterface).GetMethod("get_Id"));

            if (_id.FieldType.IsValueType)
            {
                delete.Box(_id.FieldType);
            }

            delete.Call(StringDotFormat);
            delete.Call(StringToRedisKey);
            delete.LoadConstant(0);
            delete.Call(typeof (IDatabase).GetMethod("KeyDelete", new[] {typeof (RedisKey), typeof (CommandFlags)}));
            delete.Return();

            Delete = delete.CreateDelegate();
        }

        private static void ImplementGet(Type result, FieldBuilder _id)
        {
            var get = Emit<Func<IDatabase, object, TInterface>>.NewDynamicMethod();
            get.LoadArgument(0);
            get.NewObject(result, typeof (IDatabase));
            get.Duplicate();
            get.LoadArgument(1);

            if (_id.FieldType == typeof (int))
            {
                get.UnboxAny<int>();
            }
            else if (_id.FieldType == typeof (string))
            {
                get.CastClass<string>();
            }

            get.StoreField(result.GetRuntimeFields().First(o => o.Name == "_id"));
            get.Return();

            Get = get.CreateDelegate();
        }

        private static void ImplementEnumerate(ModuleBuilder mb, Type result)
        {
            var enumeratorTypeBuilder = mb.DefineType($"{typeof (TInterface).Name}_Enumerator");
            enumeratorTypeBuilder.AddInterfaceImplementation(typeof (IEnumerator));
            enumeratorTypeBuilder.AddInterfaceImplementation(typeof (IEnumerator<TInterface>));

            var enumerator_redis = enumeratorTypeBuilder.DefineField("_redis", typeof (IDatabase), FieldAttributes.Private);
            var enum_maxid = enumeratorTypeBuilder.DefineField("_maxId", typeof (int), FieldAttributes.Private);
            var enum_position = enumeratorTypeBuilder.DefineField("_position", typeof (int), FieldAttributes.Private);
            var enum_current = enumeratorTypeBuilder.DefineField("_current", typeof (TInterface), FieldAttributes.Private);

            var enumConstructor = Emit<Action<IDatabase>>.BuildConstructor(enumeratorTypeBuilder, MethodAttributes.Public);
            enumConstructor.LoadArgument(0);
            enumConstructor.Call(typeof (object).GetConstructor(Type.EmptyTypes));
            enumConstructor.LoadArgument(0);
            enumConstructor.LoadArgument(1);
            enumConstructor.StoreField(enumerator_redis);
            enumConstructor.LoadArgument(0);
            enumConstructor.LoadArgument(0);
            enumConstructor.LoadField(enumerator_redis);
            enumConstructor.LoadConstant($"/{typeof (TInterface).Name}/ID");
            enumConstructor.Call(StringToRedisKey);
            enumConstructor.LoadConstant(0);
            enumConstructor.CallVirtual(typeof (IDatabase).GetMethod("StringGet",
                new[] {typeof (RedisKey), typeof (CommandFlags)}));
            enumConstructor.Call(
                typeof (RedisValue).GetMethods().First(o => o.Name == "op_Explicit" && o.ReturnType == typeof (int)));
            enumConstructor.StoreField(enum_maxid);
            enumConstructor.Return();

            enumConstructor.CreateConstructor();

            var enumMoveNext = Emit<Func<bool>>.BuildInstanceMethod(enumeratorTypeBuilder, "MoveNext", MethodAttributes);
            enumMoveNext.LoadArgument(0);
            enumMoveNext.LoadArgument(0);
            enumMoveNext.LoadField(enum_position);
            enumMoveNext.LoadConstant(1);
            enumMoveNext.Add();
            enumMoveNext.StoreField(enum_position);
            enumMoveNext.LoadArgument(0);
            enumMoveNext.LoadNull();
            enumMoveNext.StoreField(enum_current);
            enumMoveNext.LoadArgument(0);
            enumMoveNext.LoadField(enum_position);
            enumMoveNext.LoadArgument(0);
            enumMoveNext.LoadField(enum_maxid);
            enumMoveNext.CompareGreaterThan();
            enumMoveNext.LoadConstant(0);
            enumMoveNext.CompareEqual();
            enumMoveNext.Return();

            enumMoveNext.CreateMethod();

            var enumReset = Emit<Action>.BuildInstanceMethod(enumeratorTypeBuilder, "Reset", MethodAttributes);
            enumReset.LoadArgument(0);
            enumReset.LoadConstant(0);
            enumReset.StoreField(enum_position);
            enumReset.Return();

            enumReset.CreateMethod();

            var enumCurrent = enumeratorTypeBuilder.DefineProperty("Current", PropertyAttributes.None, typeof (object),
                Type.EmptyTypes);

            var typedEnumCurrent = enumeratorTypeBuilder.DefineProperty("Current", PropertyAttributes.None,
                typeof (TInterface), Type.EmptyTypes);
            var typedget_Current = Emit<Func<TInterface>>.BuildInstanceMethod(enumeratorTypeBuilder, "get_Current",
                MethodAttributes);
            var loc_current = typedget_Current.DeclareLocal(typeof (TInterface));

            typedget_Current.LoadArgument(0);
            typedget_Current.LoadField(enum_current);
            typedget_Current.Duplicate();
            var returnLabel = typedget_Current.DefineLabel();

            typedget_Current.BranchIfTrue(returnLabel);
            typedget_Current.Pop();
            typedget_Current.LoadArgument(0);
            typedget_Current.LoadArgument(0);
            typedget_Current.LoadField(enumerator_redis);
            typedget_Current.NewObject(result, typeof (IDatabase));
            typedget_Current.Duplicate();
            typedget_Current.LoadArgument(0);
            typedget_Current.LoadField(enum_position);
            typedget_Current.Call(result.GetMethod("set_Id"));
            typedget_Current.Duplicate();
            typedget_Current.StoreLocal(loc_current);
            typedget_Current.StoreField(enum_current);
            typedget_Current.LoadLocal(loc_current);
            typedget_Current.MarkLabel(returnLabel);
            typedget_Current.Return();

            var typedget_CurrentMethodInfo = typedget_Current.CreateMethod();

            typedEnumCurrent.SetGetMethod(typedget_CurrentMethodInfo);

            var get_Current = Emit<Func<object>>.BuildInstanceMethod(enumeratorTypeBuilder, "get_Current",
                MethodAttributes);
            get_Current.LoadArgument(0);
            get_Current.CallVirtual(typeof (IEnumerator<TInterface>).GetMethod("get_Current"));
            get_Current.Return();

            enumCurrent.SetGetMethod(get_Current.CreateMethod());

            var dispose = Emit<Action>.BuildInstanceMethod(enumeratorTypeBuilder, "Dispose", MethodAttributes);
            dispose.Return();
            dispose.CreateMethod();

            var enumeratorType = enumeratorTypeBuilder.CreateType();

            var enumerableTypeBuilder = mb.DefineType($"{typeof (TInterface).Name}_Enumerable");
            enumerableTypeBuilder.AddInterfaceImplementation(typeof (IEnumerable));
            enumerableTypeBuilder.AddInterfaceImplementation(typeof (IEnumerable<TInterface>));

            var enumerable_redis = enumerableTypeBuilder.DefineField("_redis", typeof (IDatabase),
                FieldAttributes.Private);

            var enumerableConstructor = Emit<Action<IDatabase>>.BuildConstructor(enumerableTypeBuilder,
                MethodAttributes.Public);
            enumerableConstructor.LoadArgument(0);
            enumerableConstructor.Call(typeof (object).GetConstructor(Type.EmptyTypes));
            enumerableConstructor.LoadArgument(0);
            enumerableConstructor.LoadArgument(1);
            enumerableConstructor.StoreField(enumerable_redis);
            enumerableConstructor.Return();

            enumerableConstructor.CreateConstructor();

            var enumerableGetEnumerator = Emit<Func<IEnumerator>>.BuildInstanceMethod(enumerableTypeBuilder,
                "GetEnumerator", MethodAttributes);
            enumerableGetEnumerator.LoadArgument(0);
            enumerableGetEnumerator.LoadField(enumerable_redis);
            enumerableGetEnumerator.NewObject(enumeratorType, typeof (IDatabase));
            enumerableGetEnumerator.Return();

            enumerableGetEnumerator.CreateMethod();

            var enumerableGetTypedEnumerator =
                Emit<Func<IEnumerator<TInterface>>>.BuildInstanceMethod(enumerableTypeBuilder, "GetEnumerator",
                    MethodAttributes);
            enumerableGetTypedEnumerator.LoadArgument(0);
            enumerableGetTypedEnumerator.LoadField(enumerable_redis);
            enumerableGetTypedEnumerator.NewObject(enumeratorType, typeof (IDatabase));
            enumerableGetTypedEnumerator.Return();

            enumerableGetTypedEnumerator.CreateMethod();

            var enumerableType = enumerableTypeBuilder.CreateType();

            var enumerate = Emit<Func<IDatabase, IEnumerable<TInterface>>>.NewDynamicMethod();

            enumerate.LoadArgument(0);
            enumerate.NewObject(enumerableType, typeof (IDatabase));
            enumerate.Return();

            Enumerate = enumerate.CreateDelegate();
        }

        private static void ImplementExists()
        {
            var exists = Emit<Func<IDatabase, object, bool>>.NewDynamicMethod();

            exists.LoadArgument(0);
            exists.LoadConstant($"/{typeof (TInterface).Name}/{{0}}");
            exists.LoadArgument(1);
            exists.Call(StringDotFormat);
            exists.Call(StringToRedisKey);
            exists.LoadConstant(0);
            exists.Call(typeof (IDatabase).GetMethod("KeyExists", new[] {typeof (RedisKey), typeof (CommandFlags)}));
            exists.Return();

            Exists = exists.CreateDelegate();
        }

        private static void ImplementNonIdProperty(TypeBuilder tb, PropertyImplementationInfo property, FieldBuilder _redis, FieldBuilder _id)
        {
            var prop = tb.DefineProperty(property.Name, PropertyAttributes.None, property.PropertyType, Type.EmptyTypes);

            var getter = Emit.BuildInstanceMethod(property.PropertyType, Type.EmptyTypes, tb, $"get_{property.Name}",
                MethodAttributes);

            getter.LoadArgument(0);
            getter.LoadField(_redis);
            getter.LoadConstant(property.RedisKeyFormat);
            getter.LoadArgument(0);
            getter.LoadField(_id);

            if (_id.FieldType.IsValueType)
            {
                getter.Box(_id.FieldType);
            }
            getter.Call(StringDotFormat);
            getter.Call(StringToRedisKey);
            getter.LoadConstant(0);
            getter.CallVirtual(typeof (IDatabase).GetMethod("StringGet", new[] {typeof (RedisKey), typeof (CommandFlags)}));

            var method =
                typeof (RedisValue).GetMethods()
                    .First(o => o.Name.In("op_Explicit", "op_Implicit") && o.ReturnType == property.PropertyType);

            getter.Call(method);
            getter.Return();

            var get_Name = getter.CreateMethod();
            prop.SetGetMethod(get_Name);

            var setName = Emit.BuildInstanceMethod(typeof (void), new[] {property.PropertyType}, tb, $"set_{property.Name}",
                MethodAttributes);
            var timeSpan = setName.DeclareLocal<TimeSpan?>();

            setName.LoadArgument(0);
            setName.LoadField(_redis);
            setName.LoadConstant(property.RedisKeyFormat);
            setName.LoadArgument(0);
            setName.LoadField(_id);
            if (_id.FieldType.IsValueType)
            {
                setName.Box(_id.FieldType);
            }
            setName.Call(StringDotFormat);
            setName.Call(StringToRedisKey);
            setName.LoadArgument(1);
            setName.Call(typeof (RedisValue).GetMethod("op_Implicit", new[] {property.PropertyType}));
            setName.LoadLocalAddress(timeSpan);
            setName.InitializeObject<TimeSpan?>();
            setName.LoadLocal(timeSpan);
            setName.LoadConstant(0);
            setName.LoadConstant(0);
            setName.CallVirtual(typeof (IDatabase).GetMethod("StringSet", new[] {typeof (RedisKey), typeof (RedisValue), typeof (TimeSpan?), typeof (When), typeof (CommandFlags)}));
            setName.Pop();
            setName.Return();

            var set_Name = setName.CreateMethod();
            prop.SetSetMethod(set_Name);
        }

        private static void ImplementIdProperty(TypeBuilder tb, FieldBuilder _id)
        {
            var idPropertyInfo = Properties["Id"];
            var idProperty = tb.DefineProperty("Id", PropertyAttributes.None, idPropertyInfo.PropertyType, Type.EmptyTypes);

            var getter = Emit.BuildInstanceMethod(idPropertyInfo.PropertyType, Type.EmptyTypes, tb, "get_Id", MethodAttributes);
            getter.LoadArgument(0);
            getter.LoadField(_id);
            getter.Return();
            var get = getter.CreateMethod();

            idProperty.SetGetMethod(get);

            var setter = Emit.BuildInstanceMethod(typeof (void), new[] {idProperty.PropertyType}, tb, "set_Id", MethodAttributes);
            setter.LoadArgument(0);
            setter.LoadArgument(1);
            setter.StoreField(_id);
            setter.Return();
            var set = setter.CreateMethod();

            idProperty.SetSetMethod(set);
        }
    }

    class User : IUser
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsAwesome { get; set; }
        public bool? IsAwesomer { get; set; }
        public long Score { get; set; }
        public long? Pointses { get; set; }

        public User(IDatabase redis)
        {
            
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

        public IEnumerable<T> Enumerate<T>()
        {
            return TypeImplementer<T>.Enumerate(_redisDatabase);
        }

        public T Get<T>(object id)
        {
            return TypeImplementer<T>.Get(_redisDatabase, id);
        }

        public bool Exists<T>(object id)
        {
            return TypeImplementer<T>.Exists(_redisDatabase, id);
        }

        internal bool Delete<T>(T obj)
        {
            //return TypeImplementer<T>.Delete(_redisDatabase, obj);
            throw new NotImplementedException();
        }
    }
}