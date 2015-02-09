// <copyright file="MonoWindowsPhonePushChannel.cs">
// Copyright (c) 2014 All Right Reserved, https://web.valuephone.com/
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
// KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// </copyright>
// <author>altima</author>
// <date>2015-01-15</date>

using System;
using System.IO;
using System.Net;
using System.Threading;
using PushSharp.Core;

// ReSharper disable once CheckNamespace
namespace PushSharp.WindowsPhone
{
    public class MonoWindowsPhonePushChannel : IPushChannel
    {
        WindowsPhonePushChannelSettings windowsPhoneSettings;
        long waitCounter = 0;
        static Version assemblyVerison;

        public MonoWindowsPhonePushChannel(WindowsPhonePushChannelSettings channelSettings)
        {
            windowsPhoneSettings = channelSettings;
            assemblyVerison = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, errors) => true;
        }

        public void SendNotification(INotification notification, SendNotificationCallbackDelegate callback)
        {
            var wpNotification = notification as WindowsPhoneNotification;

            var uri = new Uri(wpNotification.EndPointUrl, UriKind.Absolute);

            var wr = (HttpWebRequest)WebRequest.Create(uri);

            wr.ServicePoint.Expect100Continue = false;
            wr.UserAgent = string.Format("PushSharp/{0}", assemblyVerison.ToString(4));
            wr.ContentType = "text/xml;charset=\"utf-8\"";
            wr.Method = "POST";
            wr.KeepAlive = false;

            #region batching intervall
            var immediateValue = 3;
            var mediumValue = 13;
            var slowValue = 23;

            if (wpNotification is WindowsPhoneToastNotification)
            {
                immediateValue = 2;
                mediumValue = 12;
                slowValue = 22;
            }
            else if (wpNotification is WindowsPhoneTileNotification ||
                wpNotification is WindowsPhoneCycleTileNotification ||
                wpNotification is WindowsPhoneFlipTileNotification ||
                wpNotification is WindowsPhoneIconicTileNotification)
            {
                immediateValue = 1;
                mediumValue = 11;
                slowValue = 21;
            }

            var val = immediateValue;

            if (wpNotification.NotificationClass.HasValue)
            {
                if (wpNotification.NotificationClass.Value == BatchingInterval.Medium)
                    val = mediumValue;
                else if (wpNotification.NotificationClass.Value == BatchingInterval.Slow)
                    val = slowValue;
            }
            wr.Headers.Add("X-NotificationClass", val.ToString());
            #endregion

            #region notification type
            if (wpNotification is WindowsPhoneToastNotification)
                wr.Headers.Add("X-WindowsPhone-Target", "toast");
            else if (wpNotification is WindowsPhoneTileNotification ||
                wpNotification is WindowsPhoneCycleTileNotification ||
                wpNotification is WindowsPhoneFlipTileNotification ||
                wpNotification is WindowsPhoneIconicTileNotification)
                wr.Headers.Add("X-WindowsPhone-Target", "token");
            #endregion

            #region message id
            if (wpNotification.MessageID != null)
                wr.Headers.Add("X-MessageID", wpNotification.MessageID.ToString());
            #endregion

            #region certificate
            if (this.windowsPhoneSettings.WebServiceCertificate != null)
                wr.ClientCertificates.Add(this.windowsPhoneSettings.WebServiceCertificate);
            #endregion

            wr.BeginGetRequestStream(requestCallback, new object[] { wr, wpNotification, callback });
        }

        private void requestCallback(IAsyncResult result)
        {
            var objs = (object[])result.AsyncState;

            var wr = (HttpWebRequest)objs[0];
            var wpNotification = (WindowsPhoneNotification)objs[1];
            var callback = (SendNotificationCallbackDelegate)objs[2];

            try
            {
                var wrStream = wr.EndGetRequestStream(result);
                using (var sw = new StreamWriter(wrStream))
                {
                    sw.Write(wpNotification.PayloadToString());
                    sw.Close();
                }

                try
                {
                    wr.BeginGetResponse(responseCallback, objs);
                }
                catch (WebException wex)
                {
                    var status = ParseStatus(wex.Response as HttpWebResponse, wpNotification);
                    HandleStatus(callback, status, wpNotification);
                    wex.Response.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Error("WP {0}", ex.ToString());
                callback(this, new SendNotificationResult(wpNotification, true, ex));
                Interlocked.Decrement(ref waitCounter);
            }
        }

        void responseCallback(IAsyncResult asyncResult)
        {
            //Good list of statuses:
            //http://msdn.microsoft.com/en-us/library/ff941100(v=vs.92).aspx

            var objs = (object[])asyncResult.AsyncState;

            var wr = (HttpWebRequest)objs[0];
            var wpNotification = (WindowsPhoneNotification)objs[1];
            var callback = (SendNotificationCallbackDelegate)objs[2];

            HttpWebResponse resp = null;

            try
            {
                resp = wr.EndGetResponse(asyncResult) as HttpWebResponse;
                var status = ParseStatus(resp, wpNotification);
                HandleStatus(callback, status, wpNotification);
            }
            catch (WebException webEx)
            {
                resp = webEx.Response as HttpWebResponse;
                var status = ParseStatus(resp, wpNotification);
                HandleStatus(callback, status, wpNotification);
            }
            catch (Exception ex)
            {
                Log.Error("WP2 {0}", ex.ToString());
                callback(this, new SendNotificationResult(wpNotification, false, ex));
                Interlocked.Decrement(ref waitCounter);
            }
            finally
            {
                if (resp != null) resp.Close();
            }
        }

        WindowsPhoneMessageStatus ParseStatus(HttpWebResponse resp, WindowsPhoneNotification notification)
        {
            var result = new WindowsPhoneMessageStatus();

            result.Notification = notification;
            result.HttpStatus = HttpStatusCode.ServiceUnavailable;

            var wpStatus = string.Empty;
            var wpChannelStatus = string.Empty;
            var wpDeviceConnectionStatus = string.Empty;
            var messageID = string.Empty;

            if (resp != null)
            {
                result.HttpStatus = resp.StatusCode;

                wpStatus = resp.Headers["X-NotificationStatus"];
                wpChannelStatus = resp.Headers["X-SubscriptionStatus"];
                wpDeviceConnectionStatus = resp.Headers["X-DeviceConnectionStatus"];
                messageID = resp.Headers["X-MessageID"];
            }

            Guid msgGuid = Guid.NewGuid();
            if (Guid.TryParse(messageID, out msgGuid))
                result.MessageID = msgGuid;

            WPDeviceConnectionStatus devConStatus = WPDeviceConnectionStatus.NotAvailable;
            Enum.TryParse<WPDeviceConnectionStatus>(wpDeviceConnectionStatus, true, out devConStatus);
            result.DeviceConnectionStatus = devConStatus;

            WPNotificationStatus notStatus = WPNotificationStatus.NotAvailable;
            Enum.TryParse<WPNotificationStatus>(wpStatus, true, out notStatus);
            result.NotificationStatus = notStatus;

            WPSubscriptionStatus subStatus = WPSubscriptionStatus.NotAvailable;
            Enum.TryParse<WPSubscriptionStatus>(wpChannelStatus, true, out subStatus);
            result.SubscriptionStatus = subStatus;

            return result;
        }

        void HandleStatus(SendNotificationCallbackDelegate callback, WindowsPhoneMessageStatus status, WindowsPhoneNotification notification = null)
        {
            if (status.SubscriptionStatus == WPSubscriptionStatus.Expired)
            {
                if (callback != null)
                    callback(this, new SendNotificationResult(notification, false, new Exception("Device Subscription Expired")) { IsSubscriptionExpired = true });
            }
            else if (status.HttpStatus == HttpStatusCode.OK
                     && status.NotificationStatus == WPNotificationStatus.Received)
            {
                if (callback != null)
                    callback(this, new SendNotificationResult(notification));
            }
            else
            {
                if (callback != null)
                    callback(this, new SendNotificationResult(status.Notification, false, new WindowsPhoneNotificationSendFailureException(status)));
            }
            Interlocked.Decrement(ref waitCounter);
        }

        public void Dispose()
        {
            var slept = 0;
            while (Interlocked.Read(ref waitCounter) > 0 && slept <= 5000)
            {
                slept += 100;
                Thread.Sleep(100);
            }
        }
    }
}
