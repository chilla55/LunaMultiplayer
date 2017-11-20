﻿using Lidgren.Network;
using LunaClient.Systems;
using LunaClient.Systems.SettingsSys;
using LunaClient.Systems.TimeSyncer;
using LunaCommon.Enums;
using LunaCommon.Message.Interface;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LunaClient.Network
{
    public class NetworkSender
    {
        public static ConcurrentQueue<IMessageBase> OutgoingMessages { get; set; } = new ConcurrentQueue<IMessageBase>();
        public static NetworkSimpleMessageSender SimpleMessageSender { get; } = new NetworkSimpleMessageSender();

        /// <summary>
        /// Main sending thread
        /// </summary>
        public static void SendMain()
        {
            try
            {
                while (!NetworkConnection.ResetRequested)
                {
                    if (OutgoingMessages.TryDequeue(out var sendMessage))
                    {
                        SendNetworkMessage(sendMessage);
                    }
                    else
                    {
                        Thread.Sleep(SettingsSystem.CurrentSettings.SendReceiveMsInterval);
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Send thread error: {e}");
            }
        }

        /// <summary>
        /// Adds a new message to the queue
        /// </summary>
        /// <param name="message"></param>
        public static void QueueOutgoingMessage(IMessageBase message)
        {
            OutgoingMessages.Enqueue(message);
        }

        /// <summary>
        /// Sends the network message. It will skip client messages to send when we are not connected
        /// </summary>
        /// <param name="message"></param>
        private static void SendNetworkMessage(IMessageBase message)
        {
            if (NetworkMain.ClientConnection.Status == NetPeerStatus.NotRunning)
                NetworkMain.ClientConnection.Start();

            message.Data.SentTime = DateTime.UtcNow.Ticks;
            var bytes = message.Serialize(SettingsSystem.CurrentSettings.CompressionEnabled);
            if (bytes != null)
            {
                try
                {
                    NetworkStatistics.LastSendTime = DateTime.Now;

                    if (message is IMasterServerMessageBase)
                    {
                        foreach (var masterServer in NetworkServerList.MasterServers)
                        {
                            //Create a new message for every main server otherwise lidgren complains when you reuse the msg
                            var lidgrenMsg = GetLidgrenMessage(bytes);

                            NetworkMain.ClientConnection.SendUnconnectedMessage(lidgrenMsg, masterServer);
                            NetworkMain.ClientConnection.FlushSendQueue();
                        }
                    }
                    else
                    {
                        if (MainSystem.NetworkState >= ClientState.Connected)
                        {
                            var lidgrenMsg = GetLidgrenMessage(bytes);

                            NetworkMain.ClientConnection.SendMessage(lidgrenMsg, message.NetDeliveryMethod, message.Channel);
                        }
                    }

                    NetworkMain.ClientConnection.FlushSendQueue();
                }
                catch (Exception e)
                {
                    NetworkMain.HandleDisconnectException(e);
                }
            }
            message.Recycle();
        }

        private static NetOutgoingMessage GetLidgrenMessage(byte[] bytes)
        {
            var lidgrenMsg = NetworkMain.ClientConnection.CreateMessage(bytes.Length);
            lidgrenMsg.Write(bytes);
            return lidgrenMsg;
        }
    }
}