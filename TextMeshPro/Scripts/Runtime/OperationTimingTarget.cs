using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Profiling;

namespace TMPro
{
#pragma warning disable 0414
    public ref struct OperationTimingTarget
    {
        public readonly static long NanosecondsPerTick = (1000L * 1000L * 1000L) / Stopwatch.Frequency;

        private long start;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperationTimingTarget Start()
        {
            return new OperationTimingTarget
            {
                start = Stopwatch.GetTimestamp()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Record(ref ProfilerCounterValue<long> toRecord)
        {
            toRecord.Value += (Stopwatch.GetTimestamp() - start) * NanosecondsPerTick;
        }
    }
}
