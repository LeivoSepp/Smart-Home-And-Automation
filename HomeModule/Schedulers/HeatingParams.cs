using System;

namespace HomeModule.Schedulers
{
    class HeatingParams
    {
        internal const int NORMAL_HEATING = 1; //heating rooms an warm water
        internal const int REDUCED_HEATING = 2; //creating only warm water
        internal const int EVU_STOP = 3; //heating off at all

        public static bool TimeBetween(DateTimeOffset datetime, TimeSpan start, TimeSpan end)
        {
            TimeSpan now = datetime.TimeOfDay;
            // see if start comes before end
            if (start < end)
                return start <= now && now <= end;
            // start is after end, so do the inverse comparison
            return !(end < now && now < start);
        }
    }
}
