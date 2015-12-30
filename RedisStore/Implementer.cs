using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        public static MethodInfo RedisValueToLong = typeof(RedisValue).GetMethods().First(o => o.Name == "op_Explicit" && o.ReturnType == typeof(long));

        public static MethodInfo HashIncrement = typeof (IDatabase).GetMethod("HashIncrement", new[] {typeof (RedisKey), typeof (RedisValue), typeof (long), typeof (CommandFlags)});
        public static MethodInfo HashIncrementAsync = typeof(IDatabaseAsync).GetMethod("HashIncrementAsync", new [] { typeof(RedisKey), typeof(RedisValue), typeof(long), typeof(CommandFlags)});
        public static MethodInfo HashGet = typeof(IDatabase).GetMethod("HashGet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(CommandFlags) });
        public static MethodInfo HashSet = typeof(IDatabase).GetMethod("HashSet", new[] { typeof(RedisKey), typeof(RedisValue), typeof(RedisValue), typeof(When), typeof(CommandFlags) });
        public static MethodInfo HashSetAsync = typeof (IDatabaseAsync).GetMethod("HashSetAsync", new[] {typeof (RedisKey), typeof (RedisValue), typeof (RedisValue), typeof (When), typeof (CommandFlags)});
        public static MethodInfo HashGetAsync = typeof(IDatabaseAsync).GetMethod("HashGetAsync", new [] { typeof(RedisKey), typeof(RedisValue), typeof(CommandFlags) });

        public static MethodInfo EnumerableRange = typeof (Enumerable).GetMethod("Range", new[] {typeof (int), typeof (int)});
        public static MethodInfo EnumerableSelectGenericDefinition = typeof (Enumerable).GetMethods().First(o => o.Name == "Select" && o.GetParameters()[1].ParameterType.GetGenericArguments().Count() == 2);

        public static MethodInfo ContinueWith<TIn>(Type TResult)
        {
            return typeof (Task<TIn>)
                .GetMethods()
                .Single(o => o.GetParameters().Count() == 1 && o.IsGenericMethod && o.DeclaringType.IsGenericType)
                .MakeGenericMethod(TResult);
        }

        public static MethodInfo EnumerableSelect<T, TResult>()
        {
            return EnumerableSelectGenericDefinition.MakeGenericMethod(typeof (T), typeof (TResult));
        }

        public static MethodInfo EnumerableToArrayGenericDefinition = typeof (Enumerable).GetMethod("ToArray");

        public static MethodInfo EnumerableToArray<T>()
        {
            return EnumerableToArrayGenericDefinition.MakeGenericMethod(typeof (T));
        }

        public static MethodInfo EnumerableCastGenericDefinition = typeof (Enumerable).GetMethod("Cast");

        public static MethodInfo EnumerableCast<T>()
        {
            return EnumerableCastGenericDefinition.MakeGenericMethod(typeof (T));
        }

        public static MethodInfo EnumerableConcatGenericDefinition = typeof (Extensions).GetMethod("Concat");
        public static MethodInfo EnumerableConcat<T>()
        {
            return EnumerableConcatGenericDefinition.MakeGenericMethod(typeof (T));
        }

        public static MethodInfo DateTimeUtcNow = typeof (DateTime).GetMethod("get_UtcNow");
        public static MethodInfo ToEpochTime = typeof (InternalExtensions).GetMethod("ToEpochTime");
        public static MethodInfo ListRange = typeof (IDatabase).GetMethod("ListRange");
        public static MethodInfo SetMembers = typeof (IDatabase).GetMethod("SetMembers");

        public static Func<Task<T>, U> FromTaskOfRedisValue<T, U>(Func<T, U> fromRedisValue)
        {
            return f => fromRedisValue(f.Result);
        }

        public static MethodInfo TaskFromResult<T>()
        {
            return typeof (Task).GetMethod("FromResult").MakeGenericMethod(typeof (T));
        }
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

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public static class Implementer<TInterface>
    {
        private static readonly Type _tInterface;

        private static InterfaceImplementationInfo<TInterface> _implementationInfo;

        private static TypeBuilder _tb;
        private static FieldInfo _idField;

        private static readonly Lazy<Type> _implementedType = new Lazy<Type>(ImplementType);
        public static Type ImplementedType => _implementedType.Value;

        public static readonly Lazy<Func<object, TInterface>> Create = new Lazy<Func<object, TInterface>>(ImplementCreate);
        public static readonly Lazy<Func<object, Task<TInterface>>> CreateAsync = new Lazy<Func<object, Task<TInterface>>>(ImplementCreateAsync);
        public static readonly Lazy<Func<object, TInterface>> Get = new Lazy<Func<object, TInterface>>(ImplementGet);
        public static readonly Lazy<Func<IEnumerable<TInterface>>> Enumerate = new Lazy<Func<IEnumerable<TInterface>>>(ImplementEnumerate);

        public static Func<RedisValue, TInterface> FromRedisValue;

        static Implementer()
        {
            _tInterface = typeof (TInterface);

            if (!_tInterface.IsInterface)
            {
                throw new NotAnInterfaceException(_tInterface);
            }

            if (!_tInterface.GetProperties().Any(o => o.Name == "Id"))
            {
                throw new NoIdPropertyException(_tInterface);
            }

            _implementationInfo = new InterfaceImplementationInfo<TInterface>();
        }

        static Type ImplementType()
        {
            _tb = Implementer.mb.DefineType($"Store_{_tInterface.Name}", TypeAttributes.Public);
            _tb.AddInterfaceImplementation(_tInterface);

            _idField = _tb.DefineField("_id", _implementationInfo.IdType, FieldAttributes.Public);

            ImplementIdProperty();
            ImplementAllOtherProperties();

            return _tb.CreateType();
        }

        static void ImplementIdProperty()
        {
            var idProperty = _tb.DefineProperty("Id", PropertyAttributes.None, CallingConventions.HasThis, _implementationInfo.IdType, Type.EmptyTypes);

            var getIl = Emit.BuildInstanceMethod(_implementationInfo.IdType, Type.EmptyTypes, _tb, "get_Id", Implementer.MethodAttributes);
            getIl.LoadArgument(0);
            getIl.LoadField(_idField);
            getIl.Return();

            idProperty.SetGetMethod(getIl.CreateMethod());
        }

        static bool IsRedisCollection(PropertyImplementationInfo prop)
        {
            return prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition().In(typeof (IRedisList<>), typeof (IRedisSet<>));
        }

        static bool IsAsync(PropertyImplementationInfo prop)
        {
            return prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition() == typeof (Async<>);
        }

        static void ImplementAllOtherProperties()
        {
            foreach (var prop in _implementationInfo.Properties.Where(o => o.Name != "Id"))
            {
                var p = _tb.DefineProperty(prop.Name, PropertyAttributes.None, CallingConventions.HasThis, prop.Type, Type.EmptyTypes);

                var getIl = Emit.BuildInstanceMethod(prop.Type, Type.EmptyTypes, _tb, $"get_{prop.Name}", Implementer.MethodAttributes);

                if (IsRedisCollection(prop))
                {
                    var generatorType = (prop.Type.GetGenericTypeDefinition() == typeof (IRedisList<>)
                        ? typeof (RedisListImplementer<>)
                        : typeof (RedisSetImplementer<>)).MakeGenericType(prop.Type.GetGenericArguments()[0]);

                    var typeField = generatorType.GetField("ImplementedType");
                    getIl.NewObject(((Lazy<Type>) typeField.GetValue(null)).Value);
                    getIl.Duplicate();
                    getIl.LoadConstant($"{_implementationInfo.HashName}/{prop.Name}");
                    getIl.LoadArgument(0);
                    getIl.LoadField(_idField);

                    if (_idField.FieldType.IsValueType)
                    {
                        getIl.Box(_idField.FieldType);
                    }

                    getIl.Call(Methods.StringFormat);
                    getIl.StoreField(((Lazy<Type>) typeField.GetValue(null)).Value.GetField("Key"));
                    getIl.Return();
                }
                else if (IsAsync(prop))
                {
                    var fromRedisValue = typeof(FromRedisValue<>).MakeGenericType(prop.Type.GetGenericArguments()[0]);

                    getIl.NewObject(typeof(Async<>).MakeGenericType(prop.Type.GetGenericArguments()[0]));
                    getIl.Duplicate();

                    getIl.Call(Methods.GetDatabase);
                    getIl.LoadConstant(_implementationInfo.HashName);

                    //Key
                    getIl.LoadArgument(0);
                    getIl.LoadField(_idField);

                    if (_idField.FieldType.IsValueType)
                    {
                        getIl.Box(_idField.FieldType);
                    }

                    getIl.Call(Methods.StringFormat);
                    getIl.Call(Methods.StringToRedisKey);

                    //Value
                    getIl.LoadConstant(prop.Name.Replace("Async", ""));
                    getIl.Call(Methods.StringToRedisValue);

                    //Command Flags
                    getIl.LoadConstant(0);

                    //Call
                    getIl.Call(Methods.HashGetAsync);

                    getIl.LoadField(fromRedisValue.GetField("Implementation"));
                    getIl.Call(typeof(Lazy<>).MakeGenericType(typeof(Func<,>).MakeGenericType(typeof(RedisValue), prop.Type.GetGenericArguments()[0])).GetMethod("get_Value"));
                    getIl.Call(typeof (Methods).GetMethod("FromTaskOfRedisValue").MakeGenericMethod(typeof (RedisValue), prop.Type.GetGenericArguments()[0]));
                    getIl.Call(Methods.ContinueWith<RedisValue>(prop.Type.GetGenericArguments()[0]));
                    getIl.StoreField(prop.Type.GetField("_task"));

                    getIl.Return();
                }
                else
                {
                    var fromRedisValue = typeof (FromRedisValue<>).MakeGenericType(prop.Type);

                    getIl.LoadField(fromRedisValue.GetField("Implementation"));
                    getIl.Call(typeof (Lazy<>).MakeGenericType(typeof (Func<,>).MakeGenericType(typeof (RedisValue), prop.Type)).GetMethod("get_Value"));

                    getIl.Call(Methods.GetDatabase);
                    getIl.LoadConstant(_implementationInfo.HashName);

                    //Key
                    getIl.LoadArgument(0);
                    getIl.LoadField(_idField);

                    if (_idField.FieldType.IsValueType)
                    {
                        getIl.Box(_idField.FieldType);
                    }

                    getIl.Call(Methods.StringFormat);
                    getIl.Call(Methods.StringToRedisKey);

                    //Value
                    getIl.LoadConstant(prop.Name);
                    getIl.Call(Methods.StringToRedisValue);

                    //Command Flags
                    getIl.LoadConstant(0);

                    //Call
                    getIl.Call(Methods.HashGet);
                    getIl.Call((MethodInfo)fromRedisValue.GetField("Invoke").GetValue(null));
                    getIl.Return();
                }

                p.SetGetMethod(getIl.CreateMethod());

                var setIl = Emit.BuildInstanceMethod(typeof (void), new[] {prop.Type}, _tb, $"set_{prop.Name}", Implementer.MethodAttributes);

                if (IsRedisCollection(prop))
                {
                    setIl.Return(); //NOP.
                }
                else if (IsAsync(prop))
                {
                    setIl.LoadArgument(1);

                    setIl.Call(Methods.GetDatabase);
                    setIl.LoadConstant(_implementationInfo.HashName);
                    setIl.LoadArgument(0);
                    setIl.LoadField(_idField);
                    if (_idField.FieldType.IsValueType)
                    {
                        setIl.Box(_idField.FieldType);
                    }

                    setIl.Call(Methods.StringFormat);
                    setIl.Call(Methods.StringToRedisKey);
                    setIl.LoadConstant(prop.Name.Replace("Async", ""));
                    setIl.Call(Methods.StringToRedisValue);

                    var toRedisValue = typeof(ToRedisValue<>).MakeGenericType(prop.Type.GetGenericArguments()[0]);
                    var impl = toRedisValue.GetField("Implementation");
                    var invoke = (MethodInfo)toRedisValue.GetField("Invoke").GetValue(null);

                    setIl.LoadField(impl);
                    setIl.LoadArgument(1);
                    setIl.LoadField(prop.Type.GetField("_setValue"));
                    setIl.Call(invoke);

                    setIl.LoadConstant(0);
                    setIl.LoadConstant(0);
                    setIl.Call(Methods.HashSetAsync);
                    setIl.StoreField(prop.Type.GetField("_setTask"));

                    setIl.Return();
                }
                else
                {
                    setIl.Call(Methods.GetDatabase);
                    setIl.LoadConstant(_implementationInfo.HashName);
                    setIl.LoadArgument(0);
                    setIl.LoadField(_idField);

                    if (_idField.FieldType.IsValueType)
                    {
                        setIl.Box(_idField.FieldType);
                    }

                    setIl.Call(Methods.StringFormat);
                    setIl.Call(Methods.StringToRedisKey);
                    setIl.LoadConstant(prop.Name);
                    setIl.Call(Methods.StringToRedisValue);

                    var toRedisValue = typeof(ToRedisValue<>).MakeGenericType(prop.Type);
                    var impl = toRedisValue.GetField("Implementation");
                    var invoke = (MethodInfo)toRedisValue.GetField("Invoke").GetValue(null);

                    setIl.LoadField(impl);
                    setIl.LoadArgument(1);
                    setIl.Call(invoke);

                    setIl.LoadConstant(0);
                    setIl.LoadConstant(0);
                    setIl.Call(Methods.HashSet);
                    setIl.Pop();
                    setIl.Return();
                }

                p.SetSetMethod(setIl.CreateMethod());
            }
        }

        static Func<object, TInterface> ImplementCreate()
        {
            var il = Emit<Func<object, TInterface>>.NewDynamicMethod();
            var result = il.DeclareLocal(ImplementedType);
            var db = il.DeclareLocal<IDatabase>();

            var idField = ImplementedType.GetField("_id");

            il.NewObject(ImplementedType);
            il.StoreLocal(result);
            il.Call(Methods.GetDatabase);
            il.StoreLocal(db);
            
            //Only if id is an int.
            if (_implementationInfo.IdType == typeof (int))
            {
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

                il.StoreField(idField);
            }
            else if (idField.FieldType == typeof(string))
            {
                il.LoadLocal(result);
                il.LoadArgument(0);
                il.CastClass<string>();
                il.StoreField(idField);
            }
            else
            {
                il.LoadLocal(result);
                il.LoadArgument(0);
                il.UnboxAny(idField.FieldType);
                il.StoreField(idField);
            }

            il.LoadLocal(result);
            il.Return();

            return il.CreateDelegate();
        }

        public static Func<Task<long>, TInterface> Continuation; 

        static Func<object, Task<TInterface>> ImplementCreateAsync()
        {
            var il = Emit<Func<object, Task<TInterface>>>.NewDynamicMethod();
            var result = il.DeclareLocal(ImplementedType);
            var db = il.DeclareLocal<IDatabase>();

            var idField = ImplementedType.GetField("_id");

            il.Call(Methods.GetDatabase);
            il.StoreLocal(db);

            //Only if id is an int.
            if (_implementationInfo.IdType == typeof(int))
            {
                var doIt = Emit<Func<Task<long>, TInterface>>.NewDynamicMethod();
                doIt.NewObject(ImplementedType);
                doIt.Duplicate();
                doIt.LoadArgument(0);
                doIt.Call(typeof (Task<long>).GetMethod("get_Result"));
                doIt.Convert<int>();
                doIt.StoreField(idField);
                doIt.Return();
                Continuation = doIt.CreateDelegate();

                il.LoadLocal(db);
                il.LoadConstant("TypeCounters");
                il.Call(Methods.StringToRedisKey);
                il.LoadConstant(_tInterface.Name);
                il.Call(Methods.StringToRedisValue);
                il.LoadConstant(1L);
                il.LoadConstant(0);
                il.Call(Methods.HashIncrementAsync);
                il.LoadField(typeof (Implementer<TInterface>).GetField("Continuation"));
                il.Call(Methods.ContinueWith<long>(typeof (TInterface)));

                il.Return();
            }
            else if (idField.FieldType == typeof(string))
            {
                il.NewObject(ImplementedType);
                il.StoreLocal(result);
                
                il.LoadLocal(result);
                il.LoadArgument(0);
                il.CastClass<string>();

                il.StoreField(idField);
                il.LoadLocal(result);
                il.Call(Methods.TaskFromResult<TInterface>());

                il.Return();
            }
            else
            {
                il.NewObject(ImplementedType);
                il.StoreLocal(result);

                il.LoadLocal(result);
                il.LoadArgument(0);
                il.UnboxAny(idField.FieldType);
                il.StoreField(idField);

                il.LoadLocal(result);
                il.Call(Methods.TaskFromResult<TInterface>());

                il.Return();
            }

            return il.CreateDelegate();
        }

        static Func<object, TInterface> ImplementGet()
        {
            var il = Emit<Func<object, TInterface>>.NewDynamicMethod();
            var idField = ImplementedType.GetField("_id");

            il.NewObject(ImplementedType);
            il.Duplicate();
            il.LoadArgument(0);

            if (idField.FieldType == typeof (string))
            {
                il.CastClass<string>();
            }
            else
            {
                il.UnboxAny(idField.FieldType);
            }

            il.StoreField(idField);
            il.Return();

            return il.CreateDelegate();
        }

        static Func<IEnumerable<TInterface>> ImplementEnumerate()
        {
            var il = Emit<Func<IEnumerable<TInterface>>>.NewDynamicMethod();

            if (ImplementedType.GetField("_id").FieldType == typeof(int))
            {
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
                il.LoadField(typeof (InternalExtensions).GetField("ToObject"));
                il.Call(Methods.EnumerableSelect<int, object>());
                il.LoadField(typeof(Implementer<TInterface>).GetField("Get")); //<-- THis has to be a func of int / interface...
                il.Call(typeof (Lazy<Func<object, TInterface>>).GetProperty("Value").GetGetMethod());
                il.Call(Methods.EnumerableSelect<object, TInterface>());
                il.Return();
            }
            else
            {
                il.LoadNull();
                il.Return();
            }

            return il.CreateDelegate();
        }
    }
}