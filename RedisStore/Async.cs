using System.Threading.Tasks;

namespace RedisStore
{
    public class Async<T>
    {
        //So, either it's on it's way in, and it has the value to set...
        public T _setValue;
        public Task _setTask;

        //Or it's on it's way out 
        public Task<T> _task;

        public static implicit operator Async<T>(T source)
        {
            return new Async<T> { _setValue = source };
        }

        public static implicit operator Task(Async<T> source)
        {
            return source._setTask ?? source._task;
        }

        public static explicit operator Task<T>(Async<T> source)
        {
            return source._task;
        }

        public static explicit operator Async<T>(Task<T> source)
        {
            return new Async<T> {_task = source};
        }
    }
}