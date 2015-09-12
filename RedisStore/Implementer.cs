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
        public static MethodInfo StringToRedisKey = typeof (RedisKey).GetMethod("op_Implicit", new[] {typeof (string)});
        public static MethodInfo RedisValueToString = typeof (RedisValue).GetMethods().First(o => o.Name == "op_Implicit" && o.ReturnType == typeof (string));
        public static MethodInfo RedisValueToInt = typeof (RedisValue).GetMethods().First(o => o.Name == "op_Explicit" && o.ReturnType == typeof (int));
        public static MethodInfo LongToRedisValue = typeof (RedisValue).GetMethod("op_Implicit", new[] {typeof (long)});
        public static MethodInfo HashIncrement = typeof (IDatabase).GetMethod("HashIncrement", new[] {typeof (RedisKey), typeof (RedisValue), typeof (long), typeof (CommandFlags)});
        public static MethodInfo HashGet = typeof(IDatabase).GetMethod("HashGet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(CommandFlags) });
        public static MethodInfo HashSet = typeof(IDatabase).GetMethod("HashSet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(RedisValue), typeof(When), typeof(CommandFlags) });
    }

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    static class Implementer<TInterface>
    {
        static readonly MethodAttributes MethodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.HideBySig;

        private static readonly Type _tInterface;
        private static InterfaceImplementationInfo<TInterface> _implementationInfo;

        private static TypeBuilder _tb;
        private static FieldInfo _idField;

        public static Type ImplementedType;

        public static Func<TInterface> Create;
        public static Func<int, TInterface> Get;
        public static Func<IEnumerable<TInterface>> Enumerate;

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

        static void ImplementType()
        {
            _implementationInfo = new InterfaceImplementationInfo<TInterface>();

            var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(_implementationInfo.ImplementedTypeName), AssemblyBuilderAccess.RunAndSave);
            var mb = ab.DefineDynamicModule("module", $"{_implementationInfo.ImplementedTypeName}.dll");
            _tb = mb.DefineType("Store_User", TypeAttributes.Public);
            _tb.AddInterfaceImplementation(_tInterface);

            _idField = _tb.DefineField("_id", _implementationInfo.IdType, FieldAttributes.Public);

            ImplementIdProperty();
            ImplementAllOtherProperties();

            ImplementedType = _tb.CreateType();

            ImplementCreate();
            ImplementGet();
            ImplementEnumerate();
        }

        static void ImplementIdProperty()
        {
            var idProperty = _tb.DefineProperty("Id", PropertyAttributes.None, CallingConventions.HasThis, typeof (int), Type.EmptyTypes);

            var getIl = Emit<Func<int>>.BuildInstanceMethod(_tb, "get_Id", MethodAttributes);
            getIl.LoadArgument(0);
            getIl.LoadField(_idField);
            getIl.Return();

            idProperty.SetGetMethod(getIl.CreateMethod());
        } 

        static void ImplementAllOtherProperties()
        {
            foreach (var prop in _implementationInfo.Properties)
            {
                var p = _tb.DefineProperty(prop.Name, PropertyAttributes.None, CallingConventions.HasThis, prop.Type, Type.EmptyTypes);

                var getIl = Emit.BuildInstanceMethod(prop.Type, Type.EmptyTypes, _tb, $"get_{prop.Name}", MethodAttributes);
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

                var gotOne = typeof (RedisValue).GetMethods().First(o => o.Name.In("op_Implicit", "op_Explicit") && o.ReturnType == prop.Type);
                getIl.Call(gotOne);

                getIl.Return();

                p.SetGetMethod(getIl.CreateMethod());

                var setIl = Emit.BuildInstanceMethod(typeof (void), new[] {prop.Type}, _tb, $"set_{prop.Name}", MethodAttributes);
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

                var gotOneAgain = typeof (RedisValue).GetMethods().First(o => o.Name.In("op_Implicit", "op_Explicit") && o.GetParameters()[0].ParameterType == prop.Type);
                setIl.Call(gotOneAgain);

                setIl.LoadConstant(0);
                setIl.LoadConstant(0);
                setIl.Call(Methods.HashSet);
                setIl.Pop();
                setIl.Return();

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
            il.Call(typeof (DateTime).GetMethod("get_UtcNow"));
            il.Call(typeof (Extensions).GetMethod("ToEpochTime"));
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
            il.Call(typeof (Enumerable).GetMethod("Range", new[] {typeof (int), typeof (int)}));
            il.LoadField(typeof (Implementer<TInterface>).GetField("Get"));
            il.Call(typeof (Enumerable).GetMethods().First(o => o.Name == "Select" && o.GetParameters()[1].ParameterType.GetGenericArguments().Count() == 2).MakeGenericMethod(new[] { typeof(int), typeof(TInterface)}));
            il.Return();

            Enumerate = il.CreateDelegate();
        }
    }
}