using System;

namespace HomeModule.EnergyPrice
{
    public sealed class EnergyPriceClass
    {
        public DateTimeOffset date { get; set; }
        public int heat { get; set; }
        public double price { get; set; }
        public int heatOn{ get; set; }
        public int heatReduced { get; set; }
        public int heatOff { get; set; }
        public string time { get; set; }
        public bool isHotWaterTime { get; set; }
    }
}
