using System;
using System.Security.Cryptography;

namespace Wipe
{
    /// <summary>
    /// Provides transparent access to a pseudorandom number generator
    /// and a cryptographically safe random number generator
    /// </summary>
    public class RNG : IDisposable
    {
        private Random R1;
        private RandomNumberGenerator R2;

        /// <summary>
        /// Creates an RNG
        /// </summary>
        /// <param name="UseCrypto">Use cryptographically safe RNG</param>
        public RNG(bool UseCrypto)
        {
            R1 = new Random();
            if (UseCrypto)
            {
                R2 = RandomNumberGenerator.Create();
            }
        }

        /// <summary>
        /// Fill the supplied array with random bytes
        /// </summary>
        /// <param name="Buffer">Data array</param>
        public void NextBytes(byte[] Buffer)
        {
            if (R2 != null)
            {
                R2.GetBytes(Buffer);
            }
            else
            {
                R1.NextBytes(Buffer);
            }
        }

        public void Dispose()
        {
            if (R2 != null)
            {
                R2.Dispose();
                R2 = null;
            }
        }
    }
}
