using System;

namespace TabletCheckIn.Models
{
    // คลาสสำหรับรับข้อมูลจาก Form ตอน Save
    public class DeviceSaveRequest
    {
        public string data_id { get; set; }
        public string asset_no { get; set; }
        public string status { get; set; }
        public string host_name { get; set; }
        public string owner { get; set; }
        public string dept { get; set; }
        public string detail { get; set; }
        public bool check_morn { get; set; }
        public bool check_night { get; set; }
        public string change_details { get; set; }
    }

    // คลาสสำหรับแสดงข้อมูลในตาราง (List)
    public class DeviceListModel
    {
        public int id { get; set; }
        public string asset_no { get; set; }
        public string status { get; set; }
        public string host_name { get; set; }
        public string owner_name { get; set; }
        public string dept_name { get; set; }
        public string detail { get; set; }
        public bool check_morn { get; set; }
        public bool check_night { get; set; }
        public string registered_at { get; set; }
        public string last_updated_by { get; set; }
    }

    // คลาสสำหรับแสดงประวัติ (Activity Log)
    public class DeviceLogModel
    {
        public string action { get; set; }
        public string details { get; set; }
        public string date { get; set; }
    }
}