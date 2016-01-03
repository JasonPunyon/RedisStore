using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LINQPad;

namespace RedisStore
{
    public class Async<T> : ICustomMemberProvider
    {
        //So, either it's on it's way in, and it has the value to set...
        internal T _setValue;
        internal Task _setTask;

        //Or it's on it's way out 
        internal Task<T> _task;

        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)
        {
            return (_setTask ?? _task).ConfigureAwait(continueOnCapturedContext);
        }

        public static implicit operator Async<T>(T source)
        {
            return new Async<T> { _setValue = source };
        }

        public static implicit operator Task(Async<T> source)
        {
            return (source._setTask ?? source._task);
        }

        public static explicit operator Task<T>(Async<T> source)
        {
            return source._task;
        }

        public static explicit operator Async<T>(Task<T> source)
        {
            return new Async<T> {_task = source};
        }

        public IEnumerable<string> GetNames()
        {
            yield return "Value";
        }

        public IEnumerable<Type> GetTypes()
        {
            yield return typeof (Task<T>);
        }

        public IEnumerable<object> GetValues()
        {
            yield return _task;
        }
    }
}

namespace LINQPad
{
    public interface ICustomMemberProvider
    {
        // Each of these methods must return a sequence
        // with the same number of elements:
        IEnumerable<string> GetNames();
        IEnumerable<Type> GetTypes();
        IEnumerable<object> GetValues();
    }
}