using System;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace TMPro.Collections
{
    public class SpeedictPointerLess<TValue>
    {
        private (long Key, long Hash, TValue Value)[] buffer;
        private long longLengthMinusOne;
        private long resizeThreshold;
        private long fillCount;
        private long probeMax;

        public SpeedictPointerLess(int capacity = 32)
        {
            int targetLength = capacity < 8 ? 8 : capacity;
            probeMax = BitwiseLog2(targetLength);
            buffer = new (long, long, TValue)[targetLength + probeMax];
            resizeThreshold = (int)(buffer.Length * 0.5);
            longLengthMinusOne = targetLength - 1;
            Array.Fill(buffer, (long.MaxValue, long.MaxValue, default));
        }

        //https://stackoverflow.com/questions/8970101/whats-the-quickest-way-to-compute-log2-of-an-integer-in-c
        // BitOperations.Log2 is very recent implementation and not available on more recent platforms 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long BitwiseLog2(int targetLength)
        {
            return ((BitConverter.DoubleToInt64Bits(targetLength) >> 52) + 1) & 0xFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long HashToIndex(long hash, long lengthMinusOne)
        {
            return hash & lengthMinusOne;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long Squirrel3(long at)
        {
            const long BIT_NOISE1 = unchecked((long)0x9E3779B185EBCA87L);
            const long BIT_NOISE2 = unchecked((long)0xC2B2AE3D27D4EB4FL);
            const long BIT_NOISE3 = 0x27D4EB2F165667C5L;
            at *= BIT_NOISE1;
            at ^= (at >> 8);
            at += BIT_NOISE2;
            at ^= (at << 8);
            at *= BIT_NOISE3;
            at ^= (at >> 8);
            return at;
        }

        public ref TValue TryGet(long key, out bool success)
        {
            long index = HashToIndex(Squirrel3(key), longLengthMinusOne);
            long targetOffset = index;
            long targetMaxTarget = index + probeMax;
            for(; targetOffset < targetMaxTarget && buffer[(int)targetOffset].Key != key; targetOffset++) { }
            success = targetOffset < targetMaxTarget;

            ref (long Key, long Hash, TValue Value) value = ref buffer[targetOffset];
            return ref value.Value;
        }

        public ref TValue TryGet(long key, long hash, out bool success)
        {
            long index = HashToIndex(hash, longLengthMinusOne);
            long targetOffset = index;
            long targetMaxTarget = index + probeMax;
            for(; targetOffset < targetMaxTarget && buffer[(int)targetOffset].Key != key; targetOffset++) { }
            success = targetOffset < targetMaxTarget;

            ref (long Key, long Hash, TValue Value) value = ref buffer[targetOffset];
            return ref value.Value;
        }

        public bool Contains(long key)
        {
            long index = HashToIndex(Squirrel3(key), longLengthMinusOne);
            long targetOffset = index;
            long targetMaxTarget = index + probeMax;
            for(; targetOffset < targetMaxTarget && buffer[(int)targetOffset].Key != key; targetOffset++) { }
            return targetOffset < targetMaxTarget;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(ref T lhs, ref T rhs)
        {
            (lhs, rhs) = (rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool InternalAdd(long key, long hash, TValue value)
        {
start:
            long index = HashToIndex(hash, longLengthMinusOne);
            long distance = 0;
            while(true)
            {
                if(buffer[index].Key == long.MaxValue)
                {
                    buffer[index].Key = key;
                    buffer[index].Value = value;
                    return true;
                }
                if(buffer[index].Key == key)
                {
                    buffer[index].Value = value;
                    return false;
                }
                long desired = HashToIndex(buffer[index].Key, longLengthMinusOne);
                long currentDistance = (index + buffer.LongLength - desired);
                if(currentDistance < distance)
                {
                    Swap(ref key, ref buffer[index].Key);
                    Swap(ref value, ref buffer[index].Value);
                    distance = currentDistance;
                }
                distance++;

                if(distance >= probeMax)
                {
                    Resize();
                    goto start;
                }
                ++index;
            }
        }

        private void Resize()
        {
            long newSize = (longLengthMinusOne + 1) * 2;

            var oldBuffer = buffer;
            resizeThreshold = (int)(newSize * 0.5);
            probeMax = BitwiseLog2((int)newSize);
            buffer = new (long, long, TValue)[(int)(newSize + probeMax)];
            longLengthMinusOne = newSize - 1;
            Array.Fill(buffer, (long.MaxValue, long.MaxValue, default));

            // Create a new buffer, re-add all the existing values. Have to interate through due to potential replacement
            for(int i = 0; i < oldBuffer.Length; i++)
            {
                if(oldBuffer[i].Key != long.MaxValue)
                {
                    InternalAdd(oldBuffer[i].Key, oldBuffer[i].Hash, oldBuffer[i].Value);
                }
            }
        }

        public void Add(long key, TValue value)
        {
            if(fillCount > resizeThreshold)
            {
                Resize();
            }

            long hash = Squirrel3(key);
            if(InternalAdd(key, hash, value))
            {
                fillCount++;
            }
        }

        public void Add(long key, long hash, TValue value)
        {
            if(fillCount > resizeThreshold)
            {
                Resize();
            }
            if(InternalAdd(key, hash, value))
            {
                fillCount++;
            }
        }

        public long Length()
        {
            return fillCount;
        }

        public void Clear()
        {
            Array.Fill(buffer, (long.MaxValue, default, default));
            fillCount = 0;
        }
    }
}
