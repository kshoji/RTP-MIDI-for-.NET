using System;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// The clock for RTP packet header
    /// </summary>
    public class RtpMidiClock
    {
        public const int MidiSamplingRateDefault = 10000;
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private int clockRate;
        private long startTime;
        
        /// <summary>
        /// Initializes the clock instance
        /// </summary>
        /// <param name="clockRateValue"></param>
        public void Init(int clockRateValue)
        {
            clockRate = clockRateValue;
            if (clockRate == 0)
            {
                clockRate = MidiSamplingRateDefault;
            }
            startTime = Ticks();
        }

        /// <summary>
        /// Returns an timestamp value suitable for inclusion in a RTP packet header.
        /// </summary>
        /// <returns></returns>
        public long Now()
        {
            return CalculateCurrentTimeStamp();
        }

        private long CalculateCurrentTimeStamp()
        {
            return CalculateTimeSpent() * clockRate / 1000L;
        }
        
        /// <summary>
        /// Returns the time spent since the initial clock timestamp value.
        /// </summary>
        /// <returns></returns>
        private long CalculateTimeSpent()
        {
            return Ticks() - startTime;
        }

        /// <summary>
        /// millisecond time
        /// </summary>
        /// <returns></returns>
        public static long Ticks()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        }
    }
}