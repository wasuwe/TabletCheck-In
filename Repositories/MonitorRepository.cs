using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using TabletCheckIn.Models;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class MonitorRepository
    {
        public List<string> GetDepartments(string currentUser, string userDeptSession)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                if (string.IsNullOrEmpty(currentUser) || userDeptSession == "All" || userDeptSession == "Admin")
                {
                    string sql = "SELECT dept_name FROM tablet_check_in.departments ORDER BY dept_name";
                    return conn.Query<string>(sql).ToList();
                }
                else
                {
                    string sql = @"
                        SELECT dept_name 
                        FROM tablet_check_in.user_departments 
                        WHERE username = @currUser 
                        ORDER BY dept_name";

                    return conn.Query<string>(sql, new { currUser = currentUser }).ToList();
                }
            }
        }

        public List<DeviceRow> GetDevicesList(string deptFilter, string search, string currentUser, string userDeptSession)
        {
            var devices = new List<DeviceRow>();
            DateTime now = DateTime.Now;

            string deptInClause = "";
            if (!string.IsNullOrWhiteSpace(deptFilter))
            {
                var deptArray = deptFilter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(d => $"'{d.Trim()}'");

                deptInClause = string.Join(",", deptArray);
            }

            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
            SELECT asset_no, host_name, owner_name, dept_name, check_morn, check_night, last_seen 
            FROM tablet_check_in.devices 
            WHERE status = 'Delivered' 
            AND (@search = '' OR LOWER(asset_no) LIKE '%'||LOWER(@search)||'%' OR LOWER(host_name) LIKE '%'||LOWER(@search)||'%' OR LOWER(owner_name) LIKE '%'||LOWER(@search)||'%') ";

                if (!string.IsNullOrEmpty(deptInClause))
                {
                    sql += $" AND dept_name IN ({deptInClause}) ";
                }

                if (!string.IsNullOrEmpty(currentUser) && userDeptSession != "All" && userDeptSession != "Admin")
                {
                    sql += @" AND dept_name IN (
                        SELECT dept_name 
                        FROM tablet_check_in.user_departments 
                        WHERE username = @currUser
                      ) ";
                }

                sql += " ORDER BY asset_no ASC";

                var rawData = conn.Query(sql, new
                {
                    search = search ?? "",
                    currUser = currentUser ?? ""
                });

                foreach (var r in rawData)
                {
                    bool isOnline = false;
                    string lastSeenStr = "-";

                    if (r.last_seen != null)
                    {
                        DateTime ls = (DateTime)r.last_seen;
                        lastSeenStr = ls.ToString("dd/MM/yyyy HH:mm");
                        if ((now - ls).TotalMinutes <= 5) isOnline = true;
                    }

                    devices.Add(new DeviceRow
                    {
                        asset_no = r.asset_no,
                        host_name = r.host_name,
                        owner_name = r.owner_name,
                        dept_name = r.dept_name,
                        check_morn = r.check_morn != null && (bool)r.check_morn,
                        check_night = r.check_night != null && (bool)r.check_night,
                        is_online = isOnline,
                        last_seen = lastSeenStr
                    });
                }
            }
            return devices;
        }

        public Dictionary<string, LogInfo> LoadLogsByBusinessDate(string[] assetNos, DateTime businessDate)
        {
            var map = new Dictionary<string, LogInfo>(StringComparer.OrdinalIgnoreCase);
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT asset_no, checkin_shift, checkin_time
                    FROM tablet_check_in.checkin_logs 
                    WHERE (CASE WHEN checkin_shift = '20:00-00:00' AND EXTRACT(HOUR FROM checkin_time) < 8 
                           THEN (checkin_time::date - INTERVAL '1 day')::date ELSE checkin_time::date END) = @d 
                    AND asset_no = ANY(@assets) 
                    AND checkin_shift IN ('08:00-12:00', '20:00-00:00')";

                var rawLogs = conn.Query(sql, new { d = businessDate.Date, assets = assetNos });

                foreach (var r in rawLogs)
                {
                    DateTime t = (DateTime)r.checkin_time;
                    string s = r.checkin_shift;
                    string a = r.asset_no;

                    map[$"{a}||{s}"] = new LogInfo
                    {
                        asset_no = a,
                        checkin_shift = s,
                        check_ts = t.ToString("yyyy-MM-dd HH:mm:ss"),
                        status = ComputeStatus(s, t)
                    };
                }
            }
            return map;
        }

        public Dictionary<string, (bool checkMorn, bool checkNight)> LoadEffectiveConfigMap(string[] assetNos, DateTime targetDate)
        {
            var map = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT DISTINCT ON (asset_no) asset_no, check_morn, check_night 
                    FROM tablet_check_in.device_config_history 
                    WHERE effective_date <= @d AND asset_no = ANY(@assets) 
                    ORDER BY asset_no, effective_date DESC, updated_at DESC, id DESC";

                var rawData = conn.Query(sql, new { d = targetDate.Date, assets = assetNos });

                foreach (var r in rawData)
                {
                    map[r.asset_no] = ((bool)r.check_morn, (bool)r.check_night);
                }
            }
            return map;
        }

        public List<object> GetHistoryLogs(string asset, int offset)
        {
            var histories = new List<object>();
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT checkin_shift, checkin_time 
                    FROM tablet_check_in.checkin_logs 
                    WHERE asset_no = @a ORDER BY checkin_time DESC LIMIT 20 OFFSET @offset";

                var rawLogs = conn.Query(sql, new { a = asset, offset = offset });

                foreach (var r in rawLogs)
                {
                    DateTime t = (DateTime)r.checkin_time;
                    string s = r.checkin_shift;

                    histories.Add(new
                    {
                        checkin_time = t.ToString("dd/MM/yyyy HH:mm"),
                        checkin_shift = s,
                        status = ComputeStatus(s, t)
                    });
                }
            }
            return histories;
        }

        // ลบพารามิเตอร์ ip ออก
        public void CheckIn(string asset, string slot)
        {
            DateTime now = DateTime.Now;
            if (!TryEvaluateCheckinWindow(now, slot, out string checkStatus, out DateTime businessDate))
            {
                throw new Exception("Not allowed time window.");
            }

            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sqlCheck = @"
                    SELECT COUNT(*) FROM tablet_check_in.checkin_logs 
                    WHERE asset_no = @a AND checkin_shift = @s 
                    AND (CASE WHEN checkin_shift = '20:00-00:00' AND EXTRACT(HOUR FROM checkin_time) < 8 
                         THEN (checkin_time::date - INTERVAL '1 day')::date ELSE checkin_time::date END) = @biz";

                long count = conn.ExecuteScalar<long>(sqlCheck, new { a = asset, s = slot, biz = businessDate.Date });

                if (count > 0)
                {
                    throw new Exception("Already Checked In for this slot.");
                }

                // เอา ip_address ออกจาก SQL Insert
                string sqlInsert = @"INSERT INTO tablet_check_in.checkin_logs (asset_no, checkin_shift, checkin_time) VALUES (@a, @s, NOW())";
                conn.Execute(sqlInsert, new { a = asset, s = slot });
            }
        }

        // ================= HELPER METHODS =================
        private string ComputeStatus(string slot, DateTime checkinTime)
        {
            int h = checkinTime.Hour;
            if (slot == "08:00-12:00") return (h < 12) ? "OK" : "Delay";
            if (slot == "20:00-00:00") return (h >= 20) ? "OK" : "Delay";
            return "OK";
        }

        private bool TryEvaluateCheckinWindow(DateTime now, string slot, out string status, out DateTime businessDate)
        {
            status = "OK";
            businessDate = now.Date;
            int h = now.Hour;

            if (slot == "08:00-12:00")
            {
                if (h < 8 || h >= 20) return false;
                status = (h < 12) ? "OK" : "Delay";
                return true;
            }

            if (slot == "20:00-00:00")
            {
                if (!(h >= 20 || h < 8)) return false;
                status = (h >= 20) ? "OK" : "Delay";
                businessDate = (h < 8) ? now.Date.AddDays(-1) : now.Date;
                return true;
            }
            return false;
        }

        // ==========================================
        // 🌟 FORCE CHECK-IN METHODS (UPDATED: HOST NAME)
        // ==========================================

        public bool VerifyHostNameCheckInStatus(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName)) return true;

            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sqlDevice = "SELECT asset_no, check_morn, check_night FROM tablet_check_in.devices WHERE LOWER(host_name) = LOWER(@hostName) AND status = 'Delivered' LIMIT 1";
                var device = conn.QueryFirstOrDefault(sqlDevice, new { hostName = hostName });

                if (device == null) return true;

                DateTime now = DateTime.Now;
                int h = now.Hour;
                string currentSlot = "";
                if (h >= 8 && h < 20) currentSlot = "08:00-12:00";
                else if (h >= 20 || h < 8) currentSlot = "20:00-00:00";

                if (string.IsNullOrEmpty(currentSlot)) return true;

                bool checkMorn = device.check_morn != null && (bool)device.check_morn;
                bool checkNight = device.check_night != null && (bool)device.check_night;

                if (currentSlot == "08:00-12:00" && !checkMorn) return true;
                if (currentSlot == "20:00-00:00" && !checkNight) return true;

                DateTime businessDate = now.Date;
                if (currentSlot == "20:00-00:00" && h < 8) businessDate = now.Date.AddDays(-1);

                string sqlCheck = @"
                    SELECT COUNT(*) FROM tablet_check_in.checkin_logs 
                    WHERE asset_no = @a AND checkin_shift = @s 
                    AND (CASE WHEN checkin_shift = '20:00-00:00' AND EXTRACT(HOUR FROM checkin_time) < 8 
                         THEN (checkin_time::date - INTERVAL '1 day')::date ELSE checkin_time::date END) = @biz";

                long count = conn.ExecuteScalar<long>(sqlCheck, new { a = device.asset_no, s = currentSlot, biz = businessDate });

                return count > 0;
            }
        }

        public string PerformForceCheckInByHostName(string hostName)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                // นำการดึงฟิลด์ ip_address ออก
                string sqlDevice = "SELECT asset_no FROM tablet_check_in.devices WHERE LOWER(host_name) = LOWER(@hostName) AND status = 'Delivered' LIMIT 1";
                var device = conn.QueryFirstOrDefault(sqlDevice, new { hostName = hostName });

                if (device == null || string.IsNullOrEmpty(device.asset_no))
                    throw new Exception("ไม่พบเครื่องนี้ในระบบ (Host Name Not Found)");

                DateTime now = DateTime.Now;
                int h = now.Hour;
                string currentSlot = "";
                if (h >= 8 && h < 20) currentSlot = "08:00-12:00";
                else if (h >= 20 || h < 8) currentSlot = "20:00-00:00";

                if (string.IsNullOrEmpty(currentSlot))
                    throw new Exception("ไม่อยู่ในช่วงเวลาที่เปิดให้ Check-in");

                // ลบการส่ง parameter ip ออก เหลือแค่ asset_no และ slot
                CheckIn(device.asset_no, currentSlot);

                if (currentSlot == "08:00-12:00") return (h < 12) ? "OK" : "Delay";
                if (currentSlot == "20:00-00:00") return (h >= 20) ? "OK" : "Delay";

                return "OK";
            }
        }
    }
}