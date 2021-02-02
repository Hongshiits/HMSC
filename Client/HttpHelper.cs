/*
 * 类名：HttpHelper
 * 描述：Http下载帮助类
 * 功能：
 *      1. 断点续传
 *      2. 可暂停下载
 *      3. 计算下载速度、下载剩余时间、下载用时
 * 作者：Liang
 * 版本：V1.0.1
 * 修改时间：2020-06-29
 */
using System;
using System.IO;
using System.Net;

namespace SeaSharp.Utils
{
    public enum eDownloadSta
    {
        STA_NUL,//没有下载任务
        STA_START,//开始下载
        STA_ING,//下载中
        STA_PAUSE,//暂停下载
        STA_CONTINUE,//继续下载
        STA_STOP,//停止下载
        STA_FINISH,//下载完成
        STA_ERR//下载出错
    }

    public class HttpHelper
    {
        #region Properties
        public double FullSize { get; private set; }//文件大小FullSize
        public double DownloadedSize { get; private set; }//已下载大小
        public double LengthInOneSecond { get; private set; }//1秒下载大小
        public double Speed { get; private set; }//下载速度
        public uint RemainTime { get; private set; }//剩余下载时间
        public uint TotalTime { get; private set; } = 0;//下载总时间 单位：秒
        public eDownloadSta mCurDownloadSta { get; private set; }//当前下载状态

        #endregion

        #region Fields
        //定时器间隔 与下载速度的计算有关
        const int TIMER_TICK = 10;//每秒计算一次网速

        //接收数据的缓冲区
        const int BUF_SIZE = 8 * 1024;

        //最大读取错误数，达到之后会出下载
        const int READ_ERR_MAX_CNT = 3;

        public event UnhandledExceptionEventHandler ErrorHappend;

        ////文件大小
        //public delegate void delegateFileSize(double fileSize);
        //public delegateFileSize downFileSize;

        //定时器,定时将下载进度等信息发送给UI线程
        public System.Timers.Timer mTimer = null;


        string mSaveFileName = "";//文件保存名称
        string mHttpUrl = "";//http下载路径

        //务必使用此函数初始化，因为定时器需要再主类创建
        public HttpHelper(System.Timers.Timer _timer)
        {
            if (mTimer == null)
            {
                mTimer = _timer;
                mTimer.Interval = TIMER_TICK;
                mTimer.Stop();
                mTimer.Elapsed += new System.Timers.ElapsedEventHandler(tickEventHandler); //到达时间的时候执行事件；   
                mTimer.AutoReset = true;   //设置重复执行(true)；  
                mTimer.Enabled = true;     //设置执行tickEventHandler事件
            }
            init();
            mCurDownloadSta = eDownloadSta.STA_FINISH;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            System.Net.ServicePointManager.ServerCertificateValidationCallback +=
                delegate (object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                         System.Security.Cryptography.X509Certificates.X509Chain chain,
                         System.Net.Security.SslPolicyErrors sslPolicyErrors)
                {
                    return true; // **** Always accept
                };
        }

        #endregion
        #region Events

        public delegate void DownloadProgressEventHandler(object sender, DownloadProgressEventArgs e);
        public event DownloadProgressEventHandler Progress;

        public class DownloadProgressEventArgs : EventArgs
        {
            public double CurrentSize { get; set; }
            public double Speed { get; set; }
            public uint RemainTime { get; set; }
            public uint SpanTime { get; set; }
            public eDownloadSta State { get; set; }

            public DownloadProgressEventArgs(double curSize, double speed, uint remainTime, uint spanTime, eDownloadSta sta)
            {
                CurrentSize = curSize;
                Speed = speed;
                RemainTime = remainTime;
                SpanTime = spanTime;
                State = sta;
            }
        }

        #endregion

        public void init()
        {
            FullSize = 0.0;
            DownloadedSize = 0.0;
            LengthInOneSecond = 0.0;
            Speed = 0.0;
            RemainTime = 0;
            TotalTime = 0;
        }

        /// <summary>
        ///格式化文件大小，格式化为合适的单位
        /// </summary>
        /// <param name="size">字节大小</param>
        /// <returns></returns>
        public static string formateSize(double size)
        {
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB", "PB" };
            double mod = 1024.0;
            int i = 0;
            while (size >= mod)
            {
                size /= mod;
                i++;
            }
            return size.ToString("f2") + units[i];
        }

        /// <summary>
        /// 将秒数格式化为 时分秒格式
        /// </summary>
        /// <param name="second"></param>
        /// <returns></returns>
        public static string formatTime(uint second)
        {
            //return new DateTime(1970, 01, 01, 00, 00, 00).AddSeconds(second).ToString("HH:mm:ss");
            uint hour = second / 3600;
            uint tmp1 = second - hour * 3600;
            uint min = tmp1 / 60;
            uint sec = tmp1 - min * 60;
            return string.Format("{0}:{1}:{2}", hour.ToString("00"), min.ToString("00"), sec.ToString("00"));

        }

        /// <summary>
        /// 下载文件（同步）  支持断点续传
        /// </summary>
        public int dowLoadFile()
        {            
            int errCnt = 0;            

            //打开上次下载的文件或新建文件
            long startPos = 0;
            FileStream fs = null;
            if (File.Exists(mSaveFileName))//文件已经存在就继续下载
            {
                try
                {
                    fs = File.OpenWrite(mSaveFileName);
                    if (null == fs)
                    {
                        Console.WriteLine("open file failed:" + mSaveFileName);
                        return -1;
                    }
                    startPos = fs.Length;
                }
                catch(Exception e)
                {
                    Console.WriteLine("open file err:"+e.Message);
                    ErrorHappend?.Invoke(this, new UnhandledExceptionEventArgs(e, false));
                    if (null != fs)
                    {
                        fs.Close();
                        return -2;
                    }
                }
                                

            }
            else//新文件
            {
                fs = new FileStream(mSaveFileName, FileMode.Create);
                Console.WriteLine("创建文件");
                startPos = 0;
            }

            /**获取文件大小**/
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(mHttpUrl);
            if (null == request)
            {
                return -3;
            }

            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36";
            //request.KeepAlive = true;//保持连接
            //设置Range值，请求指定位置开始的数据,实现断点续传            
            request.AddRange(startPos);//request.AddRange(startPos, endPos)获取指定范围数据，续传跟重传(中间某部分)可使用该函数

            WebResponse respone = null;
            //注:断点续传必须在request后、并设置AddRange后第一次获取的Response才是正确的数据流
            try
            {
                respone = request.GetResponse();
                if (null == respone)
                {
                    request.Abort();
                    fs.Close();
                    return -4;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("getResponse err:"+e.Message);
                ErrorHappend?.Invoke(this, new UnhandledExceptionEventArgs(e, false));
                fs.Close();
                return -4;
            }
            
            FullSize = Convert.ToDouble(respone.ContentLength) + startPos;            
            //发送文件大小
            if (FullSize < 0)
            {
                Console.WriteLine("获取文件大小失败");
                return -5;
            }

            //文件错误，清空文件
            if (FullSize < startPos)
            {
                fs.SetLength(0);//截断文件，相当于是清空文件内容
                Console.WriteLine("文件错误，清空后重新下载");
            }

            DownloadedSize = startPos;

            //文件已下载
            if (DownloadedSize >= FullSize)
            {
                fs.Close();
                respone.Close();
                request.Abort();
                //Finished?.Invoke(this, new EventArgs());
                Console.WriteLine("文件已经下载");
                return 0;
            }

            fs.Seek(startPos, SeekOrigin.Begin);   //移动文件流中的当前指针
            //Console.WriteLine("startPos:{0}", startPos);
            Stream responseStream = null;
            byte[] dataBuf = new byte[BUF_SIZE];

            //打开网络连接
            try
            {
                //向服务器请求，获得服务器回应数据流
                responseStream = respone.GetResponseStream();
                if (null == responseStream)
                {
                    fs.Close();
                    respone.Close();
                    request.Abort();
                    return -6;
                }             
                
                int nReadSize = 0;

                do
                {
                    //读取数据
                    nReadSize = responseStream.Read(dataBuf, 0, BUF_SIZE);
                    if (nReadSize > 0)
                    {
                        fs.Write(dataBuf, 0, nReadSize);
                        //此处应该判断是否写入成功

                        //已下载大小
                        DownloadedSize += nReadSize;
                        
                        LengthInOneSecond += nReadSize;
                    }
                    else
                    {
                        errCnt++;
                        if (errCnt > READ_ERR_MAX_CNT)
                        {
                            Console.WriteLine("下载出错，退出下载");
                            break;
                        }
                    }

                    if (mCurDownloadSta == eDownloadSta.STA_STOP)
                    {
                        Console.WriteLine("停止下载");
                        break;
                    }

                } while (DownloadedSize<FullSize);

                responseStream.Close();
                respone.Close();
                fs.Close();
                request.Abort();
                
                //Console.WriteLine("DownloadedSize:{0}, FullSize:{1}", DownloadedSize, FullSize);
                if (DownloadedSize == FullSize)//下载完成
                {
                    Console.WriteLine("下载完成");
                    //Finished?.Invoke(this, new EventArgs());

                    return 100;
                }
                return 1;
            }
            catch (Exception ex)//下载失败
            {
                responseStream.Close();
                respone.Close();
                fs.Close();
                request.Abort();
                Console.WriteLine("下载失败:" + ex.ToString());
                return -7;
            }        
        }

        //停止下载
        public void downloadStop()
        {
            mCurDownloadSta = eDownloadSta.STA_STOP;
            mTimer.Stop();
        }

        /// <summary>
        /// 开始下载
        /// </summary>
        /// <param name="url">下载url</param>
        /// <param name="fileName">保存文件名</param>
        public void download(string url, string fileName)
        {
            if(mCurDownloadSta == eDownloadSta.STA_ING)
            {
                Console.WriteLine("url:{0} is downloading...", url);
                return;
            }

            if (mHttpUrl != url)//新的下载文件要重新计数
            {
                init();
            }
            else
            {
                FullSize = 0.0;
                DownloadedSize = 0.0;
                LengthInOneSecond = 0.0;
                RemainTime = 0;
            }

            mHttpUrl = url;
            mSaveFileName = fileName;
            if (fileName.Length<1)
            {
                mSaveFileName = Directory.GetCurrentDirectory() + Path.GetFileName(url);
            }
            mCurDownloadSta = eDownloadSta.STA_START;
            //Task.Run(() =>
            //{

            //});
            mTimer.Start();
            mCurDownloadSta = eDownloadSta.STA_ING;
            Console.WriteLine("start download:");
                
            int ret = -1;
            try
            {
                ret = dowLoadFile();
            }
            catch(Exception e)
            {
                ret = -100;
                Console.WriteLine("dowload err, err:"+e.Message);
                ErrorHappend?.Invoke(this, new UnhandledExceptionEventArgs(e, false));
            }
            mCurDownloadSta = eDownloadSta.STA_FINISH;
            mTimer.Stop();

            if (ret < 0)
            {
                Progress?.Invoke(this,new DownloadProgressEventArgs(FullSize, FullSize, 0, 0, eDownloadSta.STA_ERR));
            }
            else if (ret == 0)
            {
                Progress?.Invoke(this,new DownloadProgressEventArgs(FullSize, FullSize, 0, 0, eDownloadSta.STA_FINISH));
            }
            else
            {
                Progress?.Invoke(this, new DownloadProgressEventArgs(DownloadedSize, Speed, RemainTime, TotalTime, eDownloadSta.STA_FINISH));
            } 
        }


        /// <summary>
        /// 定时器方法 定时将下载进度和速度和剩余时间发送出去
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tickEventHandler(object sender, EventArgs e)
        {
            if (mCurDownloadSta == eDownloadSta.STA_ING)
            {
                TotalTime++;
                //下载速度 1秒内下载大小/1秒
                Speed = LengthInOneSecond;
                LengthInOneSecond = 0;
                
                //剩余时间 剩余大小/速度 单位:秒
                if (Speed != 0)
                {
                    RemainTime = (uint)((FullSize - DownloadedSize) / Speed);
                }
                else
                {
                    RemainTime = 0;
                }

                Progress?.Invoke(this, new DownloadProgressEventArgs(DownloadedSize, Speed, RemainTime, TotalTime, eDownloadSta.STA_ING));
            }
        }        
    }
}