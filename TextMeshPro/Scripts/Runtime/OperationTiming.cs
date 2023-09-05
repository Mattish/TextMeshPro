using System.Diagnostics;

namespace TMPro
{
#pragma warning disable 0414
    public ref struct OperationTiming
    {
        private readonly string stringFormatDouble;
        private long start;
        private double timeFilter;
        private bool hasDisposed;
        public OperationTiming(string stringFormatWithDouble)
        {
            stringFormatDouble = stringFormatWithDouble;
            timeFilter = 0;
            start = Stopwatch.GetTimestamp();
            hasDisposed = false;
        }
    
        public OperationTiming WithFilterByTime(double highPassTimeFilter)
        {
            timeFilter = highPassTimeFilter;
            return this;
        }
        
        public void Dispose()
        {
            if (hasDisposed) return;
            hasDisposed = true;
            
            double diff = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
            if (diff > timeFilter)
            {
                UnityEngine.Debug.unityLogger.Log(string.Format(stringFormatDouble, diff));
            }
        }
    }
}