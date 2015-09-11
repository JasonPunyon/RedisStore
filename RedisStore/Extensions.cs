using System.Linq;

namespace RedisStore
{
    static class Extensions
    {
        public static bool In<T>(this T element, params T[] source)
        {
            return source.Contains(element);
        }
    }
}