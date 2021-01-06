// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;

namespace HomeModule.Raspberry
{

    /// <summary>
    /// Helpers for short waits.
    /// </summary>
    internal static class DelayHelper
    {
        ////The way this works it waits for 5 consecutive signals(measured with 50us delay). 
        ////If you get 4 signals and one "false" signal it will start counting again until you get exactly 5 positive signals.
        //// in your case it might be something like:
        ////WaitForSignal(() => (bool) gpio.Read(5)); // or negate to wait for Low
        //void WaitForSignal(Func<bool> condition, int repeatsToAccept = 5, int delayMicroseconds = 100, bool allowThreadYieldDuringDelay = true)
        //{
        //    try
        //    {
        //        int samplesLeft = repeatsToAccept;
        //        while (samplesLeft > 0)
        //        {
        //            if (condition())
        //                samplesLeft--;
        //            else
        //                samplesLeft = repeatsToAccept;
        //            DelayHelper.DelayMicroseconds(delayMicroseconds, allowThreadYield: allowThreadYieldDuringDelay);
        //        }
        //    }
        //    catch (Exception x)
        //    {
        //        Console.WriteLine($"Wait For Signal exception {Program.DateTimeTZ().DateTime}: {x.Message}");
        //    }
        //}
        // GetTimestamp() currently can take ~300ns. We hope to improve this to get better
        // fidelity for very tight spins.
        //
        // SpinWait currently spins to approximately 1μs before it will yield the thread.

        /// <summary>
        /// Delay for at least the specified <paramref name="microseconds"/>.
        /// </summary>
        /// <param name="microseconds">The number of microseconds to delay.</param>
        /// <param name="allowThreadYield">
        /// True to allow yielding the thread. If this is set to false, on single-proc systems
        /// this will prevent all other code from running.
        /// </param>
        public static void DelayMicroseconds(int microseconds, bool allowThreadYield)
        {
            long start = Stopwatch.GetTimestamp();
            ulong minimumTicks = (ulong)(microseconds * Stopwatch.Frequency / 1_000_000);

            if (!allowThreadYield)
            {
                do
                {
                    Thread.SpinWait(1);
                } while ((ulong)(Stopwatch.GetTimestamp() - start) < minimumTicks);
            }
            else
            {
                SpinWait spinWait = new SpinWait();
                do
                {
                    spinWait.SpinOnce();
                } while ((ulong)(Stopwatch.GetTimestamp() - start) < minimumTicks);
            }
        }

        /// <summary>
        /// Delay for at least the specified <paramref name="milliseconds"/>
        /// </summary>
        /// <param name="milliseconds">The number of milliseconds to delay.</param>
        /// <param name="allowThreadYield">
        /// True to allow yielding the thread. If this is set to false, on single-proc systems
        /// this will prevent all other code from running.
        /// </param>
        public static void DelayMilliseconds(int milliseconds, bool allowThreadYield)
        {
            // We have this as a separate method for now to make calling code clearer
            // and to allow us to add additional logic to the millisecond wait in the
            // future. If waiting only 1 millisecond we still have ample room for more
            // complicated logic. For 1 microsecond that isn't the case.

            DelayMicroseconds(milliseconds * 1000, allowThreadYield);
        }
    }
}
