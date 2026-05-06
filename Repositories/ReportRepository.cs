using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Dapper; // 🌟 พระเอกของเรา
using RestSharp;
using System.Web.Script.Serialization;
using TabletCheckIn.Models;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class ReportRepository
    {
        // 📌 1. ดึงวันหยุดจาก API (ไม่ต้องใช้ Dapper เพราะไม่ได้ต่อ Database)
        public Dictionary<string, string> GetHolidaysFromApi(int year, int month)
        {
            var holidays = new Dictionary<string, string>();
            try
            {
                var options = new RestClientOptions("http://chtsv0046t:8686");
                var client = new RestClient(options);
                var request = new RestRequest("/api/Hrms/chtcalendarbyyear", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJIVE1GRzIgVG9rZW4iLCJqdGkiOiIyMzVkMDc4My0wNDJlLTQxZjMtYWJhYS05MTQwOWM5Y2EzZWIiLCJleHAiOjQ5MjQ0ODc0MjN9.KLz4lWLCtb-3vz1cDj1p2oCG6kCkjVgJ5AyskobTpzs");

                var payload = new { empType = "GENERAL", year = year.ToString() };
                var serializer = new JavaScriptSerializer();
                request.AddStringBody(serializer.Serialize(payload), DataFormat.Json);

                var response = client.Execute(request);
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var apiResult = serializer.Deserialize<List<HolidayApiModel>>(response.Content);
                    if (apiResult != null)
                    {
                        foreach (var item in apiResult)
                        {
                            if (item.WorkingDate.Year == year && item.WorkingDate.Month == month)
                            {
                                string dayKey = item.WorkingDate.Day.ToString();
                                if (!holidays.ContainsKey(dayKey)) holidays.Add(dayKey, item.HolidayType);
                            }
                        }
                    }
                }
            }
            catch { }
            return holidays;
        }

        // 📌 2. ดึงประวัติ Config ของอุปกรณ์ (ใช้ Dapper)
        public Dictionary<string, List<ConfigHistoryModel>> GetAllConfigHistory()
        {
            var result = new Dictionary<string, List<ConfigHistoryModel>>();
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                // ใช้ "AS" เพื่อบอกให้ Dapper แมปข้อมูลเข้าตัวแปรในคลาส ConfigHistoryModel ให้เป๊ะๆ
                string sql = @"
                    SELECT 
                        asset_no AS AssetNo, 
                        COALESCE(status, 'Delivered') AS Status, 
                        check_morn AS CheckMorn, 
                        check_night AS CheckNight, 
                        effective_date AS EffectiveDate 
                    FROM tablet_check_in.device_config_history 
                    ORDER BY effective_date ASC";

                var data = conn.Query<ConfigHistoryModel>(sql);

                // จัดกลุ่มใส่ Dictionary
                foreach (var item in data)
                {
                    if (!result.ContainsKey(item.AssetNo)) result[item.AssetNo] = new List<ConfigHistoryModel>();
                    result[item.AssetNo].Add(item);
                }
            }
            return result;
        }

        // 📌 3. ดึงรายชื่ออุปกรณ์สำหรับทำรายงาน (ใช้ Dapper + DynamicParameters)
        public List<ReportRowModel> GetReportDevices(string search, string deptFilter)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                // แปลง Date เป็น String ในฝั่ง DB เลย โค้ดฝั่ง C# จะได้คลีนๆ
                string sql = @"
                    SELECT 
                        asset_no, owner_name, dept_name, status,
                        COALESCE(check_morn, false) AS check_morn, 
                        COALESCE(check_night, false) AS check_night, 
                        COALESCE(TO_CHAR(registered_at, 'DD/MM/YYYY'), '-') AS registered_at,
                        registered_at AS reg_date_obj
                    FROM tablet_check_in.devices 
                    WHERE status != 'Delete' ";

                var param = new DynamicParameters();

                if (!string.IsNullOrEmpty(search))
                {
                    sql += " AND (asset_no ILIKE @search OR owner_name ILIKE @search) ";
                    param.Add("search", $"%{search}%");
                }

                if (!string.IsNullOrEmpty(deptFilter))
                {
                    sql += " AND dept_name = ANY(string_to_array(@dept, ',')) ";
                    param.Add("dept", deptFilter);
                }

                sql += " ORDER BY asset_no ASC";

                // Dapper กวาดและจับคู่เข้า ReportRowModel ให้แบบง่ายๆ
                return conn.Query<ReportRowModel>(sql, param).ToList();
            }
        }

        // 📌 4. ดึงข้อมูล Log ดิบรายเดือน (ใช้ Dapper)
        public List<RawLogData> GetRawLogs(int year, int month)
        {
            var logs = new List<RawLogData>();
            DateTime selectedMonth = new DateTime(year, month, 1);

            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT asset_no, checkin_shift, checkin_time 
                    FROM tablet_check_in.checkin_logs 
                    WHERE checkin_time >= @startDate AND checkin_time < @endDate";

                // ดึงข้อมูลมาเป็น Object เลย
                var rawData = conn.Query<RawLogData>(sql, new
                {
                    startDate = selectedMonth.AddDays(-1),
                    endDate = selectedMonth.AddMonths(1).AddDays(1)
                });

                // วนลูปเพื่อใช้ Logic การคำนวณวันข้ามคืนแบบที่คุณเขียนไว้
                foreach (var r in rawData)
                {
                    DateTime bizDate = r.checkin_time.Date;

                    // จัดการวันข้ามคืนของกะดึก
                    if (r.checkin_shift == "20:00-00:00" && r.checkin_time.Hour < 8)
                    {
                        bizDate = r.checkin_time.Date.AddDays(-1);
                    }

                    if (bizDate.Year == year && bizDate.Month == month)
                    {
                        r.business_date = bizDate;
                        logs.Add(r);
                    }
                }
            }
            return logs;
        }
    }
}