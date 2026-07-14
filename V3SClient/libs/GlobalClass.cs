using System;
using System.CodeDom;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Newtonsoft.Json;
using V3SClient.ucs;

namespace V3SClient.libs
{
    public static class GlobalClass
    {

        

        private static readonly ConcurrentDictionary<string, List<string>> tmpMp4Player = new ConcurrentDictionary<string, List<string>>();

        static string Root_Path { get; set; }


        static GlobalClass()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (config.AppSettings.Settings.Count == 0)
                    throw new ConfigurationErrorsException("Không tìm thấy appSettings trong V3SClient.config!");

                Root_Path = System.Configuration.ConfigurationManager.AppSettings["Root_Path"] ?? AppDomain.CurrentDomain.BaseDirectory;
                if (!Directory.Exists(Root_Path))
                {
                    Root_Path = AppDomain.CurrentDomain.BaseDirectory;
                }
            }
            catch (Exception ex) {
                System.Windows.MessageBox.Show("Lỗi khi tải cấu hình: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }
        public static BitmapImage LoadImage(string image_file)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(image_file, UriKind.Relative);
            bitmap.EndInit();
            return bitmap;
        }

        public static void FindRowsAndCols(int N, out int rows, out int cols)
        {
            N = N == 3 ? 4 : N;
            do
            {
                rows = 1;
                cols = N;

                for (int r = 1; r * r <= N; r++)
                    if (N % r == 0)
                    {
                        int c = N / r;
                        if (r <= c)
                        {
                            rows = r;
                            cols = c;
                        }
                    }

                while (rows * cols < N)
                {
                    rows++;
                    if (rows > cols)
                    {
                        cols++;
                        rows = 1;
                    }
                }
                N = N + 1;
            } while (rows + 3 < cols);
        }

        
       
        public static void UpdateOrAddKeyPairMp4Tmp(string deviceId, List<string> filePaths)
        {
            try
            {
                tmpMp4Player.AddOrUpdate(deviceId,
                    new List<string>(filePaths),
                    (key, existingList) =>
                    {
                        lock (existingList)
                        {
                            existingList.Clear();
                            existingList.AddRange(filePaths);
                        }
                        return existingList;
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating key pair: {ex.Message}");
            }
        }

        public static Dictionary<string, List<string>> GetAllKeyPairsMp4Tmp()
        {
            return new Dictionary<string, List<string>>(tmpMp4Player);
        }

       
   
       
        public static List<string> GetFilesMp4Tmp(string deviceId)
        {
            return tmpMp4Player.TryGetValue(deviceId, out var fileList) ? new List<string>(fileList) : new List<string>();
        }

        
        public static void RemoveAllMp4Tmp()
        {
            try
            {
                tmpMp4Player.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing all data: {ex.Message}");
            }
        }
        public static void Init() {
           
        }
    }
}

















