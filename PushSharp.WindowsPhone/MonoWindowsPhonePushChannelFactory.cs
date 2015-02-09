// <copyright file="MonoWindowsPhonePushChannelFactory.cs">
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
using PushSharp.Core;

namespace PushSharp.WindowsPhone
{
    public class MonoWindowsPhonePushChannelFactory : IPushChannelFactory
    {
        public IPushChannel CreateChannel(IPushChannelSettings channelSettings)
        {
            if (!(channelSettings is WindowsPhonePushChannelSettings))
                throw new ArgumentException("channelSettings must be of type WindowsPhonePushChannelSettings");

            return new MonoWindowsPhonePushChannel(channelSettings as WindowsPhonePushChannelSettings);
        }
    }
}