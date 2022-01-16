using System;
using System.Collections.Generic;

namespace Assets.SION
{
    public static class ListUtilities
    {
        private static readonly uint[] Primes = new[]
            { 1u, 2u, 3u, 5u, 7u, 11u, 13u, 17u, 19u, 23u, 29u, 31u, 37u, 41u, 43u, 53u, 59u, 61u, 67u, 71u, 73u, 79u, 83u, 89u, 97u };

        private static readonly int[] HighestPrimeIndex = new int[100];

        static ListUtilities()
        {
            var index = 0;
            // Set HighestPrimeIndex[i] = index of largest prime in Primes that less than i
            for (int i = 1; i < HighestPrimeIndex.Length; i++)
            {
                if (i > HighestPrimeIndex[index + 1])
                    // Next prime
                    index++;
                HighestPrimeIndex[i] = index;
            }
        }

        private static readonly Random Rng = new Random();

        public static IEnumerable<T> BadShuffle<T>(this IList<T> list)
        {
            var length = (uint) list.Count;
            if (length == 0)
                yield break;

            // Pick an random starting point and step size
            // Step size needs to be relatively prime to length if we're
            // to hit all the elements, so we always choose a prime number
            // for the step
            
            var position = Rng.Next() % length;

            // Set step = random prime less than length (or 1 if length == 1)
            var maxPrimeIndex = Primes.Length - 1;
            if (length < HighestPrimeIndex.Length)
                maxPrimeIndex = HighestPrimeIndex[length];
            var step = Primes[Rng.Next() % (maxPrimeIndex + 1)];

            for (uint i = 0; i < length; i++)
            {
                yield return list[(int) position];
                position = (position + step) % length;
            }
        }

        public static IEnumerable<T> MaybeShuffle<T>(this IList<T> list, bool shouldShuffle) =>
            shouldShuffle ? list.BadShuffle() : list;
    }
}
