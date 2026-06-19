using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ScreenTranslator.Infrastructure
{
    /// <summary>
    /// Quản lý Configuration Profile: cho phép người dùng tạo, lưu, tải, và chuyển đổi
    /// giữa nhiều bộ cấu hình khác nhau (ví dụ: "Game", "Phim", "Công việc").
    /// Mỗi profile là một file JSON riêng biệt lưu trong thư mục 'profiles/'.
    /// </summary>
    public class ProfileManager
    {
        private readonly string _profileDirectory;
        private readonly LogService _log = LogService.Instance;

        public ProfileManager()
        {
            _profileDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (!Directory.Exists(_profileDirectory))
                Directory.CreateDirectory(_profileDirectory);
        }

        /// <summary>
        /// Dữ liệu chứa trong mỗi Profile.
        /// </summary>
        public class ProfileData
        {
            public string Name { get; set; } = "Default";
            public string ApiKey { get; set; } = "";
            public string ApiModel { get; set; } = "groq|llama-3.1-8b-instant";
            public string TargetLang { get; set; } = "vi";
            public double OverlayOpacity { get; set; } = 0.8;
            public double OverlayFontSize { get; set; } = 26;
            public string OverlayTextColor { get; set; } = "#00FF00";
            public bool LockOverlayClickThrough { get; set; } = false;
            public CaptureRegionData? CaptureRegion { get; set; }
        }

        public class CaptureRegionData
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        /// <summary>
        /// Lấy danh sách tên tất cả Profile đã lưu.
        /// </summary>
        public List<string> GetProfileNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var file in Directory.GetFiles(_profileDirectory, "*.json"))
                {
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception ex)
            {
                _log.Error("ProfileManager", "Error listing profiles", ex);
            }

            // Luôn đảm bảo có profile "Default"
            if (!names.Contains("Default"))
            {
                names.Insert(0, "Default");
            }

            return names;
        }

        /// <summary>
        /// Lưu một Profile vào file JSON.
        /// </summary>
        public bool SaveProfile(ProfileData profile)
        {
            try
            {
                string sanitizedName = SanitizeFileName(profile.Name);
                string filePath = Path.Combine(_profileDirectory, $"{sanitizedName}.json");
                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);

                _log.Info("ProfileManager", $"Profile '{profile.Name}' saved successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("ProfileManager", $"Failed to save profile '{profile.Name}'", ex);
                return false;
            }
        }

        /// <summary>
        /// Tải một Profile theo tên. Trả về null nếu không tìm thấy.
        /// </summary>
        public ProfileData? LoadProfile(string name)
        {
            try
            {
                string sanitizedName = SanitizeFileName(name);
                string filePath = Path.Combine(_profileDirectory, $"{sanitizedName}.json");

                if (!File.Exists(filePath))
                {
                    _log.Warning("ProfileManager", $"Profile '{name}' not found. Returning default.");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                var profile = JsonSerializer.Deserialize<ProfileData>(json);

                _log.Info("ProfileManager", $"Profile '{name}' loaded successfully.");
                return profile;
            }
            catch (Exception ex)
            {
                _log.Error("ProfileManager", $"Failed to load profile '{name}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Xóa một Profile. Profile "Default" không thể bị xóa.
        /// </summary>
        public bool DeleteProfile(string name)
        {
            if (name == "Default")
            {
                _log.Warning("ProfileManager", "Cannot delete the Default profile.");
                return false;
            }

            try
            {
                string sanitizedName = SanitizeFileName(name);
                string filePath = Path.Combine(_profileDirectory, $"{sanitizedName}.json");

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _log.Info("ProfileManager", $"Profile '{name}' deleted.");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.Error("ProfileManager", $"Failed to delete profile '{name}'", ex);
                return false;
            }
        }

        /// <summary>
        /// Kiểm tra xem profile có tồn tại hay không.
        /// </summary>
        public bool ProfileExists(string name)
        {
            string sanitizedName = SanitizeFileName(name);
            return File.Exists(Path.Combine(_profileDirectory, $"{sanitizedName}.json"));
        }

        /// <summary>
        /// Loại bỏ ký tự không hợp lệ trong tên file.
        /// </summary>
        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
