namespace CastleDefender.UI
{
    public enum ProgressionViewerMode
    {
        LobbyViewer = 0,
        PreMatchConfirm = 1,
    }

    public struct ProgressionViewerLaunchRequest
    {
        public ProgressionViewerMode mode;
        public string raceId;
    }

    public static class ProgressionViewerLaunchContext
    {
        static bool _hasPendingRequest;
        static ProgressionViewerLaunchRequest _pendingRequest;

        public static void OpenLobbyViewer(string raceId = null)
        {
            _pendingRequest = new ProgressionViewerLaunchRequest
            {
                mode = ProgressionViewerMode.LobbyViewer,
                raceId = raceId,
            };
            _hasPendingRequest = true;
        }

        public static bool TryConsume(out ProgressionViewerLaunchRequest request)
        {
            request = _pendingRequest;
            if (!_hasPendingRequest)
                return false;

            _hasPendingRequest = false;
            _pendingRequest = default;
            return true;
        }

        public static void Clear()
        {
            _hasPendingRequest = false;
            _pendingRequest = default;
        }
    }
}
