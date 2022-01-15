using System;
using System.Collections.Generic;

namespace Assets.SION
{
    public static class ListUtilities
    {
        private static readonly uint[] Primes = new[]
            { 1u, 2u, 3u, 5u, 7u, 11u, 13u, 17u, 19u, 23u, 29u, 31u, 37u, 41u, 43u, 53u, 59u, 61u, 67u, 71u, 73u, 79u, 83u, 89u, 97u };

        private static readonly Random Rng = new Random();
        public static IEnumerable<T> BadShuffle<T>(this IList<T> list)
        {
            var length = (uint)list.Count; 
            var position = Rng.Next()%length;
            var step = length;
            while (step >= length)
                step = Primes[Rng.Next() % Primes.Length];
            
            for (uint i = 0; i < length; i++)
            {
                yield return list[(int)position];
                position = (position + step) % length;
            }
        }

        public static IEnumerable<T> MaybeShuffle<T>(this IList<T> list, bool shouldShuffle) =>
            shouldShuffle ? list.BadShuffle() : list;
    }
}
