using Netatmo.Net.Extensions;
using System;
using System.Collections.Generic;

namespace Netatmo.Net.Model
{
    public class Measurement
    {
        public long TimeStamp { get; }
        public List<MeasurementValue> MeasurementValues { get; private set; }

        public Measurement(long timestamp, List<MeasurementValue> values)
        {
            TimeStamp = timestamp;
            MeasurementValues = values;
        }

        public DateTime DateTime => TimeStamp.ToDateTime();
    }
}
