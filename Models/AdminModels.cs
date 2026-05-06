using System;

namespace TabletCheckIn.Models
{
    public class UserModel
    {
        public string username { get; set; }
        public string password { get; set; }
        public string full_name { get; set; }
        public string dept_name { get; set; }
        public string user_role { get; set; }
    }

    public class UserSaveRequest
    {
        public string username { get; set; }
        public string password { get; set; }
        public string full_name { get; set; }
        public string dept_name { get; set; }
        public bool is_edit { get; set; }
        public string email { get; set; }
        public string user_role { get; set; }
    }

    public class DepartmentModel
    {
        public int id { get; set; }
        public string dept_name { get; set; }
        public string email { get; set; }
    }

    public class DeptSaveRequest
    {
        public string id { get; set; }
        public string dept_name { get; set; }
        public string email { get; set; }
    }
}