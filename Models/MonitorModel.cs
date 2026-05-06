using System;
using System.Collections.Generic;

namespace TabletCheckIn.Models
{
    public class DeviceRow
    {
        public string asset_no { get; set; }
        public string host_name { get; set; }
        public string owner_name { get; set; }
        public string dept_name { get; set; }
        public bool check_morn { get; set; }
        public bool check_night { get; set; }
        public bool is_online { get; set; }
        public string last_seen { get; set; }
        public bool is_checked_in { get; set; }
        public bool missed_yesterday_last { get; set; }
    }

    public class LogRow
    {
        public string asset_no { get; set; }
        public string checkin_shift { get; set; }
        public string check_ts { get; set; }
        public string status { get; set; }
        public string host_name { get; set; }
    }

    public class LogInfo
    {
        public string asset_no { get; set; }
        public string checkin_shift { get; set; }
        public string check_ts { get; set; }
        public string status { get; set; }
        public string host_name { get; set; }
    }

    public class MonitorSummary
    {
        public int total_devices { get; set; }
        public int online_count { get; set; }
        public int offline_count { get; set; }
        public int morn_ok { get; set; }
        public int morn_delay { get; set; }
        public int morn_miss { get; set; }
        public int morn_expected { get; set; }
        public int night_ok { get; set; }
        public int night_delay { get; set; }
        public int night_miss { get; set; }
        public int night_expected { get; set; }
        public int yest_ok { get; set; }
        public int yest_delay { get; set; }
        public int yest_miss { get; set; }
        public Dictionary<string, int> dept_breakdown { get; set; }
    }
}