using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using V3SClient.Attributes;

namespace V3SClient.libs
{
    public class BlacklistObjectFaceInfo
    {
        public Guid Id { get; set; } // Không hiển thị

        [DisplayInfo("Tên", 1)]
        public string Name { get; set; }

        [DisplayInfo("Bí danh", 2)]
        public string Alias { get; set; }

        [DisplayInfo("Số định danh", 3)]
        public string Identification { get; set; }

        [DisplayInfo("Giới tính", 4)]
        public int Gender { get; set; } // Nam:1, Nữ:0

        [DisplayInfo("Ngày sinh", 5)]
        public DateTime? BirthDate { get; set; }

        [DisplayInfo("Tình trạng hôn nhân", 6)]
        public int MaritalStatus { get; set; } // Chưa kết hôn:0, Kết hôn:1

        [DisplayInfo("Dân tộc", 7)]
        public string Nation { get; set; }

        [DisplayInfo("Tôn giáo", 8)]
        public string Religion { get; set; }

        [DisplayInfo("Quốc tịch", 9)]
        public string Nationality { get; set; }

        [DisplayInfo("Tỉnh/Thành phố", 10)]
        public string Province { get; set; }

        [DisplayInfo("Phường/Xã", 11)]
        public string Ward { get; set; }

        [DisplayInfo("Quận/Huyện", 12)]
        public string District { get; set; }

        [DisplayInfo("Nguyên quán", 13)]
        public string PlaceOfOrigin { get; set; }

        [DisplayInfo("Địa chỉ thường trú", 14)]
        public string ResidentialAddress { get; set; }

        [DisplayInfo("Địa chỉ hiện tại", 15)]
        public string CurrentAddess { get; set; }

        [DisplayInfo("Số điện thoại", 16)]
        public string PhoneNumber { get; set; }

        [DisplayInfo("Email", 17)]
        public string Email { get; set; }

        [DisplayInfo("Cơ quan làm việc", 18)]
        public string Company { get; set; }

        [DisplayInfo("Địa chỉ cơ quan", 19)]
        public string CompanyAddress { get; set; }

        [DisplayInfo("Nghề nghiệp", 20)]
        public string Job { get; set; }

        [DisplayInfo("Trình độ học vấn", 21)]
        public string EducationLevel { get; set; }

        [DisplayInfo("Nhóm đối tượng", 22)]
        public string GroupSupects { get; set; }

        [DisplayInfo("Hành vi phạm tội", 23)]
        public string Crime { get; set; }

        [DisplayInfo("Tiền sử vi phạm", 24)]
        public string ViolationHistory { get; set; }

        [DisplayInfo("Màu da", 25)]
        public string SkinColor { get; set; }

        [DisplayInfo("Đặc điểm nhận dạng", 26)]
        public string IdenticalFutures { get; set; }

        [DisplayInfo("Màu tóc", 27)]
        public string HairColor { get; set; }

        [DisplayInfo("Màu mắt", 28)]
        public string EyeColor { get; set; }

        [DisplayInfo("Chiều cao", 29)]
        public string Height { get; set; }

        [DisplayInfo("Cân nặng", 30)]
        public string Weight { get; set; }

        [DisplayInfo("Thông tin khác", 31)]
        public string Other { get; set; }

      
        public string MainImagePath { get; set; }

       
        public string SubImagePath { get; set; }

    }

}
