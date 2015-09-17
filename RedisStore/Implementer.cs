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
    public class InterfaceImplementationInfo<T>
    {
        readonly Type _type;
        public InterfaceImplementationInfo()
        {
            _type = typeof(T);
        }

        public string ImplementedTypeName => _type.Name;
        public Type IdType => _type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance).PropertyType;
        public string HashName => $"/{_type.Name}/{{0}}";
        public List<PropertyImplementationInfo> Properties => _type.GetProperties().Select(o => new PropertyImplementationInfo(o)).ToList();
    }

    public class PropertyImplementationInfo
    {
        PropertyInfo _prop;

        public PropertyImplementationInfo(PropertyInfo prop)
        {
            _prop = prop;
        }

        public Type DeclaringType => _prop.DeclaringType;
        public Type Type => _prop.PropertyType;
        public string Name => _prop.Name;
    }

    public static class Methods
    {
        public static MethodInfo GetDatabase = typeof (Store).GetMethod("get_Database");
        public static MethodInfo StringFormat = typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) });
        public static MethodInfo StringToRedisValue = typeof (RedisValue).GetMethod("op_Implicit", new[] {typeof (string)});
        public static MethodInfo IntToRedisValue = typeof (RedisValue).GetMethod("op_Implicit", new[] {typeof (int)});

        public static MethodInfo StringToRedisKey = typeof (RedisKey).GetMethod("op_Implicit", new[] {typeof (string)});
        public static MethodInfo RedisValueToString = typeof (RedisValue).GetMethods().First(o => o.Name == "op_Implicit" && o.ReturnType == typeof (string));
        public static MethodInfo RedisValueToInt = typeof (RedisValue).GetMethods().First(o => o.Name == "op_Explicit" && o.ReturnType == typeof (int));
        public static MethodInfo LongToRedisValue = typeof (RedisValue).GetMethod("op_Implicit", new[] {typeof (long)});
        public static MethodInfo HashIncrement = typeof (IDatabase).GetMethod("HashIncrement", new[] {typeof (RedisKey), typeof (RedisValue), typeof (long), typeof (CommandFlags)});
        public static MethodInfo HashGet = typeof(IDatabase).GetMethod("HashGet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(CommandFlags) });
        public static MethodInfo HashSet = typeof(IDatabase).GetMethod("HashSet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(RedisValue), typeof(When), typeof(CommandFlags) });
        public static MethodInfo EnumerableRange = typeof (Enumerable).GetMethod("Range", new[] {typeof (int), typeof (int)});
        public static MethodInfo EnumerableSelect = typeof (Enumerable).GetMethods().First(o => o.Name == "Select" && o.GetParameters()[1].ParameterType.GetGenericArguments().Count() == 2);
        public static MethodInfo DateTimeUtcNow = typeof (DateTime).GetMethod("get_UtcNow");
        public static MethodInfo ToEpochTime = typeof (Extensions).GetMethod("ToEpochTime");
        public static MethodInfo ListRange = typeof (IDatabase).GetMethod("ListRange");
    }

    static class Implementer
    {
        public static AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("StoreImplementations"), AssemblyBuilderAccess.RunAndSave);
        public static ModuleBuilder mb = ab.DefineDynamicModule("module", $"StoreImplementations.dll");
        public static readonly MethodAttributes MethodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.HideBySig;

        public static void DumpAssembly()
        {
            ab.Save("StoreImplementations.dll");
        }
    }

    public interface IRedisList<T> : IEnumerable<T>
    {
        void Add(T item);
    }

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class RedisListImplementer<T>
    {
        public static Type RedisListType;
        private static Type _t;

        static RedisListImplementer()
        {
            _t = typeof (T);

            var redisListType = Implementer.mb.DefineType($"{_t.Name}_RedisList", TypeAttributes.Public);
            redisListType.AddInterfaceImplementation(typeof(IRedisList<T>));
            //redisListType.AddInterfaceImplementation(typeof (IEnumerable<T>));
            //redisListType.AddInterfaceImplementation(typeof(IEnumerable));

            var key = redisListType.DefineField("Key", typeof (string), FieldAttributes.Public);

            var add = Emit<Action<T>>.BuildInstanceMethod(redisListType, "Add", Implementer.MethodAttributes);

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

            var typedGetEnumerator = Emit<Func<IEnumerator<T>>>.BuildInstanceMethod(redisListType, "GetEnumerator", Implementer.MethodAttributes);

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

            var getEnumerator = Emit<Func<IEnumerator>>.BuildInstanceMethod(redisListType, "GetEnumerator", Implementer.MethodAttributes);
            getEnumerator.LoadArgument(0);
            getEnumerator.Call(typeof (IEnumerable<T>).GetMethod("GetEnumerator"));
            getEnumerator.Return();

            getEnumerator.CreateMethod();

            RedisListType = redisListType.CreateType();
        }
    }

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class Implementer<TInterface>
    {
        private static readonly Type _tInterface;
        private static InterfaceImplementationInfo<TInterface> _implementationInfo;

        private static TypeBuilder _tb;
        private static FieldInfo _idField;

        public static Type ImplementedType;

        public static Func<TInterface> Create;
        public static Func<int, TInterface> Get;
        public static Func<IEnumerable<TInterface>> Enumerate;
        public static Func<RedisValue, TInterface> FromRedisValue;

        static Implementer()
        {
            _tInterface = typeof (TInterface);

            if (!_tInterface.IsInterface)
            {
                throw new NotAnInterfaceException(_tInterface);
            }

            if (!_tInterface.GetProperties().Any(o => o.Name == "Id" && o.PropertyType == typeof(int)))
            {
                throw new NoIdPropertyException(_tInterface);
            }

            ImplementType();
        }

        static void ImplementFromRedisValue()
        {
            var fromRedisValue = Emit<Func<RedisValue, TInterface>>.NewDynamicMethod();

            fromRedisValue.NewObject(ImplementedType);
            fromRedisValue.Duplicate();
            fromRedisValue.LoadArgument(0);
            fromRedisValue.Call(Methods.RedisValueToInt);
            fromRedisValue.StoreField(ImplementedType.GetField("_id"));
            fromRedisValue.Return();

            FromRedisValue = fromRedisValue.CreateDelegate();
        } 

        static void ImplementType()
        {
            _implementationInfo = new InterfaceImplementationInfo<TInterface>();

            _tb = Implementer.mb.DefineType($"Store_{_tInterface.Name}", TypeAttributes.Public);
            _tb.AddInterfaceImplementation(_tInterface);

            _idField = _tb.DefineField("_id", _implementationInfo.IdType, FieldAttributes.Public);

            ImplementIdProperty();
            ImplementAllOtherProperties();

            ImplementedType = _tb.CreateType();

            ImplementCreate();
            ImplementGet();
            ImplementEnumerate();
            ImplementFromRedisValue();
        }

        static void ImplementIdProperty()
        {
            var idProperty = _tb.DefineProperty("Id", PropertyAttributes.None, CallingConventions.HasThis, typeof (int), Type.EmptyTypes);

            var getIl = Emit<Func<int>>.BuildInstanceMethod(_tb, "get_Id", Implementer.MethodAttributes);
            getIl.LoadArgument(0);
            getIl.LoadField(_idField);
            getIl.Return();

            idProperty.SetGetMethod(getIl.CreateMethod());
        } 

        static void ImplementAllOtherProperties()
        {
            foreach (var prop in _implementationInfo.Properties.Where(o => o.Name != "Id"))
            {
                var p = _tb.DefineProperty(prop.Name, PropertyAttributes.None, CallingConventions.HasThis, prop.Type, Type.EmptyTypes);

                var getIl = Emit.BuildInstanceMethod(prop.Type, Type.EmptyTypes, _tb, $"get_{prop.Name}", Implementer.MethodAttributes);
                getIl.Call(Methods.GetDatabase);
                getIl.LoadConstant(_implementationInfo.HashName);
                getIl.LoadArgument(0);
                getIl.LoadField(_idField);
                getIl.Box<int>();
                getIl.Call(Methods.StringFormat);
                getIl.Call(Methods.StringToRedisKey);
                getIl.LoadConstant(prop.Name);
                getIl.Call(Methods.StringToRedisValue);
                getIl.LoadConstant(0);
                getIl.Call(Methods.HashGet);

                var conversion = typeof (RedisValue).GetMethods().FirstOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == prop.Type);

                if (conversion != null)
                {
                    getIl.Call(conversion);
                }
                else if (prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition() == typeof(IRedisList<>))
                {
                    var generatorType = typeof (RedisListImplementer<>).MakeGenericType(prop.Type.GetGenericArguments()[0]);
                    var listTypeField = generatorType.GetField("RedisListType");

                    getIl.Pop();
                    getIl.NewObject((Type)listTypeField.GetValue(null));
                    getIl.Duplicate();
                    getIl.LoadConstant($"{_implementationInfo.HashName}/{prop.Name}");
                    getIl.LoadArgument(0);
                    getIl.LoadField(_idField);
                    getIl.Box<int>();
                    getIl.Call(Methods.StringFormat);
                    getIl.StoreField(((Type) listTypeField.GetValue(null)).GetField("Key"));
                }

                getIl.Return();

                p.SetGetMethod(getIl.CreateMethod());

                var setIl = Emit.BuildInstanceMethod(typeof (void), new[] {prop.Type}, _tb, $"set_{prop.Name}", Implementer.MethodAttributes);

                var conversion2 = typeof(RedisValue).GetMethods().FirstOrDefault(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == prop.Type);

                if (conversion2 != null)
                {
                    setIl.Call(Methods.GetDatabase);
                    setIl.LoadConstant(_implementationInfo.HashName);
                    setIl.LoadArgument(0);
                    setIl.LoadField(_idField);
                    setIl.Box<int>();
                    setIl.Call(Methods.StringFormat);
                    setIl.Call(Methods.StringToRedisKey);
                    setIl.LoadConstant(prop.Name);
                    setIl.Call(Methods.StringToRedisValue);

                    setIl.LoadArgument(1);

                    var gotOneAgain = typeof(RedisValue).GetMethods().First(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == prop.Type);
                    setIl.Call(gotOneAgain);

                    setIl.LoadConstant(0);
                    setIl.LoadConstant(0);
                    setIl.Call(Methods.HashSet);
                    setIl.Pop();
                    setIl.Return();
                }
                else if (prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition() == typeof (IRedisList<>))
                {
                    setIl.Return(); //NOP.
                }

                p.SetSetMethod(setIl.CreateMethod());
            }
        }

        static void ImplementCreate()
        {
            var il = Emit<Func<TInterface>>.NewDynamicMethod();
            var result = il.DeclareLocal(ImplementedType);
            var db = il.DeclareLocal<IDatabase>();

            il.NewObject(ImplementedType);
            il.StoreLocal(result);
            il.Call(Methods.GetDatabase);
            il.StoreLocal(db);
            il.LoadLocal(result);
            il.LoadLocal(db);
            il.LoadConstant("TypeCounters");
            il.Call(Methods.StringToRedisKey);
            il.LoadConstant(_tInterface.Name);
            il.Call(Methods.StringToRedisValue);
            il.LoadConstant(1L);
            il.LoadConstant(0);
            il.Call(Methods.HashIncrement);
            il.Convert<int>();
            il.StoreField(ImplementedType.GetField("_id"));
            il.LoadLocal(db);
            il.LoadConstant(_implementationInfo.HashName);
            il.LoadLocal(result);
            il.LoadField(ImplementedType.GetField("_id"));
            il.Box<int>();
            il.Call(Methods.StringFormat);
            il.Call(Methods.StringToRedisKey);
            il.LoadConstant("Created");
            il.Call(Methods.StringToRedisValue);
            il.Call(Methods.DateTimeUtcNow);
            il.Call(Methods.ToEpochTime);
            il.Call(Methods.LongToRedisValue);
            il.LoadConstant(0);
            il.LoadConstant(0);
            il.Call(Methods.HashSet);
            il.Pop();
            il.LoadLocal(result);
            il.Return();

            Create = il.CreateDelegate();
        }

        static void ImplementGet()
        {
            var il = Emit<Func<int, TInterface>>.NewDynamicMethod();

            il.NewObject(ImplementedType);
            il.Duplicate();
            il.LoadArgument(0);
            il.StoreField(ImplementedType.GetField("_id"));
            il.Return();

            Get = il.CreateDelegate();
        }

        static void ImplementEnumerate()
        {
            var il = Emit<Func<IEnumerable<TInterface>>>.NewDynamicMethod();

            il.LoadConstant(1);
            il.Call(Methods.GetDatabase);
            il.LoadConstant("TypeCounters");
            il.Call(Methods.StringToRedisKey);
            il.LoadConstant(_tInterface.Name);
            il.Call(Methods.StringToRedisValue);
            il.LoadConstant(0);
            il.Call(Methods.HashGet);
            il.Call(Methods.RedisValueToInt);
            il.Call(Methods.EnumerableRange);
            il.LoadField(typeof (Implementer<TInterface>).GetField("Get"));
            il.Call(Methods.EnumerableSelect.MakeGenericMethod(typeof(int), typeof(TInterface)));
            il.Return();

            Enumerate = il.CreateDelegate();
        }
    }
}