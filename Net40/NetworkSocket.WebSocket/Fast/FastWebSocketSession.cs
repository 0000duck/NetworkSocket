﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetworkSocket.WebSocket.Fast
{
    /// <summary>
    /// FastWebSocket的会话对象
    /// 不可继承
    /// </summary>
    public sealed class FastWebSocketSession : WebSocketSession, IFastWebSocketSession
    {
        /// <summary>
        /// 数据包id提供者
        /// </summary>
        private PacketIdProvider packetIdProvider;

        /// <summary>
        /// 任务行为表
        /// </summary>
        private TaskSetActionTable taskSetActionTable;

        /// <summary>
        /// 获取Json序列化工具       
        /// </summary>
        internal IJsonSerializer JsonSerializer { get; private set; }

        /// <summary>
        /// 获取Api行为特性过滤器提供者
        /// </summary>
        internal IFilterAttributeProvider FilterAttributeProvider { get; private set; }


        /// <summary>
        /// FastWebSocket的客户端对象
        /// </summary>
        /// <param name="packetIdProvider"></param>
        /// <param name="taskSetActionTable"></param>
        /// <param name="jsonSerializer"></param>
        /// <param name="filterAttributeProvider"></param>
        internal FastWebSocketSession(PacketIdProvider packetIdProvider, TaskSetActionTable taskSetActionTable, IJsonSerializer jsonSerializer, IFilterAttributeProvider filterAttributeProvider)
        {
            this.packetIdProvider = packetIdProvider;
            this.taskSetActionTable = taskSetActionTable;
            this.JsonSerializer = jsonSerializer;
            this.FilterAttributeProvider = filterAttributeProvider;
        }

        /// <summary>
        /// 调用远程端实现的服务方法        
        /// </summary>       
        /// <param name="api">api(区分大小写)</param>
        /// <param name="parameters">参数列表</param>    
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="SocketException"></exception>         
        public Task InvokeApi(string api, params object[] parameters)
        {
            return Task.Factory.StartNew(() =>
            {
                var id = this.packetIdProvider.GetId();
                var packet = new FastPacket
                {
                    api = api,
                    id = id,
                    state = true,
                    fromClient = false,
                    body = parameters
                };
                var packetJson = this.JsonSerializer.Serialize(packet);
                this.SendText(packetJson);
            });
        }

        /// <summary>
        /// 调用客户端实现的服务方法     
        /// 并返回结果数据任务
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>        
        /// <param name="api">api(区分大小写)</param>
        /// <param name="parameters">参数</param>     
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="SocketException"></exception> 
        /// <exception cref="RemoteException"></exception>
        /// <exception cref="TimeoutException"></exception>
        /// <returns>远程数据任务</returns>  
        public Task<T> InvokeApi<T>(string api, params object[] parameters)
        {
            var id = this.packetIdProvider.GetId();
            var taskSource = new TaskCompletionSource<T>();
            var packet = new FastPacket
            {
                api = api,
                id = id,
                state = true,
                fromClient = false,
                body = parameters
            };
            var packetJson = this.JsonSerializer.Serialize(packet);

            // 登记TaskSetAction           
            Action<SetTypes, string> setAction = (setType, json) =>
            {
                if (setType == SetTypes.SetReturnReult)
                {
                    if (json == null || json.Length == 0)
                    {
                        taskSource.TrySetResult(default(T));
                    }
                    else
                    {
                        var result = (T)this.JsonSerializer.Deserialize(json, typeof(T));
                        taskSource.TrySetResult(result);
                    }
                }
                else if (setType == SetTypes.SetReturnException)
                {
                    var exception = new RemoteException(json);
                    taskSource.TrySetException(exception);
                }
                else if (setType == SetTypes.SetTimeoutException)
                {
                    var exception = new TimeoutException();
                    taskSource.TrySetException(exception);
                }
                else if (setType == SetTypes.SetShutdownException)
                {
                    var exception = new SocketException((int)SocketError.Shutdown);
                    taskSource.TrySetException(exception);
                }
            };
            var taskSetAction = new TaskSetAction(setAction);
            taskSetActionTable.Add(packet.id, taskSetAction);

            this.SendText(packetJson);
            return taskSource.Task;
        }
    }
}