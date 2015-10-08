using System;
using System.Collections.Generic;
using System.Linq;

namespace RedisStore
{
    static class InternalExtensions
    {
        public static bool In<T>(this T element, params T[] source)
        {
            return source.Contains(element);
        }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0);
        private static readonly long EpochMin = DateTime.MinValue.ToEpochTime();
        private static readonly long EpochMax = DateTime.MaxValue.ToEpochTime();

        /// <summary>
        /// Returns a unix Epoch time given a Date
        /// </summary>
        public static long ToEpochTime(this DateTime dt)
        {
            return (long)(dt - Epoch).TotalSeconds;
        }

        /// <summary>
        /// Converts to Date given an Epoch time
        /// </summary>
        public static DateTime ToDateTime(this long epoch)
        {
            if (epoch == EpochMin) return DateTime.MinValue;
            if (epoch == EpochMax) return DateTime.MaxValue;
            return Epoch.AddSeconds(epoch);
        }

        public static Func<int, object> ToObject = i => i; 
    }

    public static class Extensions
    {
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> source, T element)
        {
            return source.Concat(new[] { element });
        }
    }
}