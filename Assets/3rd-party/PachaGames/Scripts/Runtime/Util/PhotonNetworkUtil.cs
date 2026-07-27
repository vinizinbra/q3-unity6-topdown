using Photon; // for DisconnectCause
using System.Text;
using Photon.Realtime;

public static class PhotonNetworkUtil
{
    public static string GetDisconnectDescription(DisconnectCause cause)
    {
        switch (cause)
        {
            // 🔌 Connection & Timeout Issues
            case DisconnectCause.ExceptionOnConnect:
                return "Failed to connect to the server. Please check your internet or server address.";
            case DisconnectCause.Exception:
                return "Network exception occurred. Please check your internet connection.";
            case DisconnectCause.ServerTimeout:
                return "The server connection timed out. Please try again.";
            case DisconnectCause.ClientTimeout:
                return "The client connection timed out. Please try again.";
            // 🔑 Authentication & Config Errors
            case DisconnectCause.InvalidAuthentication:
                return "Invalid authentication. Check your AppId or login credentials.";
            case DisconnectCause.CustomAuthenticationFailed:
                return "Custom authentication failed. Please log in again.";
            case DisconnectCause.AuthenticationTicketExpired:
                return "Your authentication ticket expired. Please reconnect.";

            // ⚡ Server / Region Issues
            case DisconnectCause.MaxCcuReached:
                return "The server is full. Please try again later.";
            case DisconnectCause.DisconnectByServerLogic:
                return "Disconnected by server logic (e.g., room closed).";
            case DisconnectCause.DisconnectByServerReasonUnknown:
                return "Disconnected by the server for an unknown reason.";
            case DisconnectCause.OperationNotAllowedInCurrentState:
                return "Tried to do something that’s not allowed right now.";

            // 🧑 User Initiated
            case DisconnectCause.DisconnectByClientLogic:
                return "You disconnected manually.";

            // Default
            default:
                return $"Unknown disconnect cause: {cause}";
        }
    }
}
