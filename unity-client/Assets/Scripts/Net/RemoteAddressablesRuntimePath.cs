using System;
using UnityEngine;

namespace CastleDefender.Net
{
    public static class RemoteAddressablesRuntimePath
    {
        public static string RemoteLoadPath => $"{BaseUrl.TrimEnd('/')}/addressables";

        static string BaseUrl
        {
            get
            {
                if (NetworkManager.Instance != null && !string.IsNullOrWhiteSpace(NetworkManager.Instance.ResolvedServerUrl))
                    return NetworkManager.Instance.ResolvedServerUrl;

                return "http://127.0.0.1:3000";
            }
        }
    }
}
