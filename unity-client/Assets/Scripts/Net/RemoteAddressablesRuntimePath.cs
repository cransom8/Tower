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

#if UNITY_WEBGL && !UNITY_EDITOR
                if (!string.IsNullOrWhiteSpace(Application.absoluteURL))
                {
                    var page = new Uri(Application.absoluteURL);
                    bool standard = (page.Scheme == "https" && page.Port == 443)
                                 || (page.Scheme == "http" && page.Port == 80)
                                 || page.Port < 0;
                    return standard
                        ? $"{page.Scheme}://{page.Host}"
                        : $"{page.Scheme}://{page.Host}:{page.Port}";
                }
#endif

                return "http://127.0.0.1:3000";
            }
        }
    }
}
