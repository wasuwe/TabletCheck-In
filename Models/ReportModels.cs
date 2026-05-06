using System;
using System.Collections.Generic;

namespace TabletCheckIn.Models
{
    public class ReportRowModel
    {
        public string asset_no { get; set; }
        public string owner_name { get; set; }
        public string dept_name { get; set; }
        public string status { get; set; }
        public string registered_at { get; set; }
        public DateTime? reg_date_obj { get; set; }
        public bool check_morn { get; set; }
        public bool check_night { get; set; }
        public double score { get; set; }
        public int count_ok { get; set; }
        public int count_delay { get; set; }
        public int count_missed { get; set; }
        public Dictionary<string, DayStatus> days { get; set; } = new Dictionary<string, DayStatus>();
    }

    public class DayStatus
    {
        public SlotResult morn { get; set; } = new SlotResult { status = "Future" };
        public SlotResult night { get; set; } = new SlotResult { status = "Future" };
    }

    public class SlotResult
    {
        public string status { get; set; }
        public string time { get; set; }
    }

    public class HolidayApiModel
    {
        public DateTime WorkingDate { get; set; }
        public int WorkingStatus { get; set; }
        public string HolidayType { get; set; }
    }

    public class ConfigHistoryModel
    {
        public string AssetNo { get; set; }
        public string Status { get; set; }
        public bool CheckMorn { get; set; }
        public bool CheckNight { get; set; }
        public DateTime EffectiveDate { get; set; }
    }

    public class RawLogData
    {
        public string asset_no { get; set; }
        public string checkin_shift { get; set; }
        public DateTime checkin_time { get; set; }
        public DateTime business_date { get; set; }
    }
}