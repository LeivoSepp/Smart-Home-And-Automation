namespace HomeModule.Helpers
{
    class CONSTANT
    {
        public const int TIMER_SECONDS_WHEN_ZONE_EMPTY = 120; //2 minutes
        public const int TIMER_SECONDS_CLEAR_DOOR_QUEUE = 120;
        public const int TIMER_MINUTES_WHEN_HOME_EMPTY = 30;
        public const int TIMER_MINUTES_WHEN_SECURED_HOME_EMPTY = 2;
        public const int TIMER_MINUTES_TO_SEND_ZONE_COSMOS = 10;

        public const int MAX_ITEMS_IN_ALERTING_LIST = 1000;

        public const int TIMER_MINUTES_CHECK_CO2 = 5;
        public const int TIMER_MINUTES_VENT_ON = 60;
        public const int CO2_LEVEL_TO_CHECK = 900;

        public const int EXTREME_SAUNA_TEMP = 110;
        public const int MAX_SAUNA_HEATING_TIME = 180; //minutes

        public const string CONTAINER_MAPPED_FOLDER = "mappedFolder";
        public const string FILENAME_SAUNA_TIME = "SaunaStartedTime";
        public const string FILENAME_ROOM_TEMPERATURES = "temperatureSET";
        public const string FILENAME_HOME_DEVICES = "homeDevices";


        public const int OUTSIDE_LIGHTS_MANUAL_DURATION = 10; //minutes

        public const string SLEEP_TIME = "00:00";
        public const string WAKEUP_TIME = "07:00";

        public const int CHECK_NETATMO_IN_MINUTES = 10;

        public const double DEFAULT_ROOM_TEMP = 21.5;
        public const double DEFAULT_SAUNA_TEMP = 95;
        public const double MIN_WATER_TEMP = 40;

        public const int NORMAL_HEATING = 1; //heating rooms an warm water
        public const int REDUCED_HEATING = 2; //creating only warm water
        public const int EVU_STOP = 3; //heating off at all

        public const int SIGNAL_TRESHOLD = -70; //dB
        public const int ACTIVE_DEVICES_IN_LAST = -600; //seconds

        public const int MOBILE_DURATION = 30; //20
        public const int NOTEBOOK_DURATION = 45; //45
        public const int WATCH_DURATION = 2;
        public const int OTHER_DURATION = 40; //40

    }
}
