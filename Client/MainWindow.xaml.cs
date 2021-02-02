using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SeaSharp.Utils;
using System.Threading;
using System.ComponentModel;
using System.IO;

namespace HMSC.Client
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            brdLoading.Visibility = Visibility.Visible;
            btnStart.Visibility = Visibility.Collapsed;
        }

        string downPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods");

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            listView.ItemsSource = infos;
            LoadList();

            if (!System.IO.Directory.Exists("mods"))
            {
                MessageBox.Show("未检测到mods文件夹，清检查是否安装fabric或forge");
                Environment.Exit(-1);
            }
        }

        ObservableCollection<DownloadInfo> infos = new ObservableCollection<DownloadInfo>();
 
        #region List Loader
        private HttpHelper listHelper;
        private string tempPath = System.IO.Path.GetTempFileName();
        private string tempPath2 = System.IO.Path.GetTempFileName();
        private void LoadList()
        {
            listHelper = new HttpHelper(new System.Timers.Timer());
            listHelper.Progress += ListHelper_Progress;
            new Thread(() => { listHelper.download(Properties.Settings.Default.urlList, tempPath); }).Start();

            HttpHelper blackHelper = new HttpHelper(new System.Timers.Timer());
            blackHelper.Progress += BlackHelper_Progress;
            new Thread(() => { blackHelper.download(Properties.Settings.Default.urlBlack, tempPath2); }).Start();

        }

        private void BlackHelper_Progress(object sender, HttpHelper.DownloadProgressEventArgs e)
        {
            if (e.State == eDownloadSta.STA_FINISH)
            {
                string[] black = System.IO.File.ReadAllText(tempPath2).Split('\n');
                foreach (string name in black)
                {
                    if (System.IO.File.Exists(downPath + @"\" + name))
                        File.Delete(downPath + @"\" + name);
                }

            }
        }

        private void ListHelper_Progress(object sender, HttpHelper.DownloadProgressEventArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (listHelper.FullSize != 0)
                    prgLoading.Value = e.CurrentSize / listHelper.FullSize * 100;
            }));

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.State == eDownloadSta.STA_FINISH)
                {
                    string[] list = System.IO.File.ReadAllText(tempPath).Split('\n');

                    foreach (string n in list)
                    {
                        string[] s = n.Split('|');
                        if (s.Length == 2)
                        {
                            DownloadInfo inf = new DownloadInfo();
                            inf.Helper = new HttpHelper(new System.Timers.Timer());
                            inf.FileName = s[1];
                            inf.Url = s[0];
                            inf.StatusText = "等待下载...";
                            inf.Enabled = !System.IO.File.Exists(System.IO.Path.Combine(downPath, inf.FileName));
                            if (!inf.Enabled)
                                inf.StatusText = "已存在";
                            infos.Add(inf);
                        }
                    }
                    brdLoading.Visibility = Visibility.Collapsed;
                    btnStart.Visibility = Visibility.Visible;

                    try { System.IO.File.Delete(tempPath); } catch { }
                }

            }));
        }


        #endregion

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            infos.Remove(listView.SelectedItem as DownloadInfo);
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            foreach (DownloadInfo inf in infos)
            {
                if (inf.Enabled)
                {
                    inf.StatusText = "";
                    new Thread(() => { inf.Helper.download(inf.Url, System.IO.Path.Combine(downPath, inf.FileName)); }).Start();

                }

            }
            btnStart.IsEnabled = false;
            btnStart.Visibility = Visibility.Collapsed;
            this.Resources["DeleteButtonVisiblity"] = Visibility.Collapsed;

        }
    }
    public class DownloadInfo : INotifyPropertyChanged
    {
        private HttpHelper helper;

        public event PropertyChangedEventHandler PropertyChanged;

        public HttpHelper Helper
        {
            get { return helper; }
            set { helper = value; helper.Progress += Helper_Progress; }
        }

        private void Helper_Progress(object sender, HttpHelper.DownloadProgressEventArgs e)
        {
            if (helper.FullSize != 0)
                Percent = e.CurrentSize / helper.FullSize * 100;

            Reamin = Helper.RemainTime;

            if (e.State == eDownloadSta.STA_FINISH)
                StatusText = "下载完成！";
            if (e.State == eDownloadSta.STA_ERR)
                StatusText = "下载出错！";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Percent"));

        }

        public string FileName { get; set; }
        public double Percent { get; set; }
        public uint Reamin { get; set; }
        public string Url { get; set; }
        public bool Enabled { get; set; } = true;


        private string statusText = "";
        public string StatusText
        {
            get { return statusText; }
            set { statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("StatusText")); }
        }
    }
}
