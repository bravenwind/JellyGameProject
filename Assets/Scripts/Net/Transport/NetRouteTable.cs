using System;
using System.Collections.Generic;

namespace JellyNet
{
    public class NetRouteTable
    {
        private readonly Dictionary<MsgType, Action<int, NetReader>> hostRoutes
            = new Dictionary<MsgType, Action<int, NetReader>>();

        private readonly Dictionary<MsgType, Action<NetReader>> clientRoutes
            = new Dictionary<MsgType, Action<NetReader>>();

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            if (handler == null)
                return;

            if (hostRoutes.ContainsKey(type))
                return;

            hostRoutes[type] = handler;
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            if (handler == null)
                return;

            if (clientRoutes.ContainsKey(type))
                return;

            clientRoutes[type] = handler;
        }

        public void UnrouteHost(MsgType type) { hostRoutes.Remove(type); }

        public void UnrouteClient(MsgType type) { clientRoutes.Remove(type); }

        public void DispatchHost(int peerId, MsgType type, NetReader reader)
        {
            Action<int, NetReader> route;
            if (hostRoutes.TryGetValue(type, out route))
            {
                route(peerId, reader);
                return;
            }
        }

        public void DispatchClient(MsgType type, NetReader reader)
        {
            Action<NetReader> route;
            if (clientRoutes.TryGetValue(type, out route))
            {
                route(reader);
                return;
            }
        }
    }
}
