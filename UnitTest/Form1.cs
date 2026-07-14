using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Renci.SshNet;

namespace UnitTest
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            TEST_SFTP();
        }

        public void TEST_SFTP()
        {
            string host = "127.0.0.1";
            int port = 2200; 
            string username = "user";
            string privateKeyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "key"); 
          
            if (!File.Exists(privateKeyPath))
            {
                Console.WriteLine("Private key file không tồn tại!");
                return;
            }

            // Đọc private key (không có passphrase, nếu có thì dùng PrivateKeyFile(path, passphrase))
            var keyFile = new PrivateKeyFile(privateKeyPath);
            var keyFiles = new[] { keyFile };
            var methods = new AuthenticationMethod[]
            {
            new PrivateKeyAuthenticationMethod(username, keyFiles)
            };

            var connectionInfo = new ConnectionInfo(host, port, username, methods);

            using (var client = new SftpClient(connectionInfo))
            {
                try
                {
                    client.Connect();
                    Console.WriteLine("✅ Kết nối SFTP thành công!");

                    var files = client.ListDirectory(".");
                    Console.WriteLine("📂 Danh sách file:");
                    foreach (var file in files)
                    {
                        Console.WriteLine($" - {file.Name}");
                    }

                    client.Disconnect();
                    Console.WriteLine("🔌 Ngắt kết nối.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ Kết nối thất bại: " + ex.Message);
                }
            }
        }
    }
}
