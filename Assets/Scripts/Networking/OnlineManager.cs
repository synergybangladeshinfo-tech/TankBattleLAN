using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using TankBattle.Core;

namespace TankBattle.Networking
{
    /// <summary>
    /// Internet play through Unity Relay.
    ///
    /// LAN play is untouched: ConnectionManager still binds a real UDP socket on
    /// the local network. This class is the second, parallel route - the host
    /// asks Relay for an allocation, gets a short JOIN CODE, and the transport is
    /// pointed at the Relay server instead of a LAN address. Friends anywhere in
    /// the world type that code and land in the same match. No port forwarding,
    /// no public IP, no router settings.
    ///
    /// Unity's free tier covers 50 concurrent players a month, which is far more
    /// than this game will ever need.
    ///
    /// Everything is wrapped in try/catch and reports through Status: if the
    /// Unity Cloud project is not linked yet the game must still run perfectly
    /// offline and on LAN, so a Relay failure can never be fatal.
    /// </summary>
    public static class OnlineManager
    {
        /// <summary>Human-readable result of the last online operation.</summary>
        public static string Status { get; private set; } = "";

        /// <summary>Join code of the room we are hosting (empty if not hosting).</summary>
        public static string JoinCode { get; private set; } = "";

        /// <summary>True once Unity Services has initialised and signed in.</summary>
        public static bool Ready { get; private set; }

        /// <summary>True while a room is running over Relay rather than LAN.</summary>
        public static bool IsOnlineSession { get; private set; }

        /// <summary>Relay's own limit is 100; we cap at the game's player limit.</summary>
        const int MaxConnections = GameConstants.MaxPlayers - 1;

        // ------------------------------------------------------------- services

        /// <summary>
        /// Initialise Unity Services and sign in anonymously. The cloud project
        /// id is normally baked into the build, but it can also be pasted into
        /// the Online screen at runtime - that way the player can switch the
        /// project without waiting for a new APK.
        /// </summary>
        public static async Task<bool> EnsureServices()
        {
            if (Ready) return true;

            try
            {
                string id = SettingsManager.CloudProjectId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var options = new InitializationOptions();
                    options.SetOption("com.unity.services.core.cloud-project-id", id.Trim());
                    await UnityServices.InitializeAsync(options);
                }
                else
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Ready = true;
                Status = "Connected to Unity servers";
                return true;
            }
            catch (Exception e)
            {
                Ready = false;
                Status = "Online not set up yet - paste your Unity Project ID below. " +
                         "LAN play still works.";
                Debug.LogWarning($"[Online] service init failed: {e.Message}");
                return false;
            }
        }

        // ----------------------------------------------------------------- host

        /// <summary>
        /// Create a Relay room and start hosting on it. Returns the join code, or
        /// null if anything went wrong (Status explains what).
        /// </summary>
        public static async Task<string> HostOnline()
        {
            if (!await EnsureServices()) return null;

            try
            {
                Status = "Creating room...";
                Allocation alloc = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
                string code = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

                var transport = Transport();
                if (transport == null)
                {
                    Status = "Transport missing";
                    return null;
                }

                // Explicit byte-array overload: it has been stable across every
                // UnityTransport version, unlike the Allocation helper structs.
                transport.SetRelayServerData(
                    alloc.RelayServer.IpV4,
                    (ushort)alloc.RelayServer.Port,
                    alloc.AllocationIdBytes,
                    alloc.Key,
                    alloc.ConnectionData,
                    null,
                    true);   // DTLS

                IsOnlineSession = true;
                JoinCode = code;

                // advertise:false - there is nothing to discover over UDP here.
                if (!ConnectionManager.Instance.StartHost(advertise: false))
                {
                    IsOnlineSession = false;
                    JoinCode = "";
                    Status = "Could not start hosting";
                    return null;
                }

                Status = $"Room open. Share the code: {code}";
                return code;
            }
            catch (Exception e)
            {
                IsOnlineSession = false;
                Status = "Could not create the room. Check your internet.";
                Debug.LogWarning($"[Online] host failed: {e.Message}");
                return null;
            }
        }

        // --------------------------------------------------------------- client

        /// <summary>Join a Relay room by its code. Returns true on success.</summary>
        public static async Task<bool> JoinOnline(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Status = "Type the room code first";
                return false;
            }
            if (!await EnsureServices()) return false;

            try
            {
                Status = "Joining room...";
                JoinAllocation join = await RelayService.Instance
                    .JoinAllocationAsync(code.Trim().ToUpperInvariant());

                var transport = Transport();
                if (transport == null)
                {
                    Status = "Transport missing";
                    return false;
                }

                transport.SetRelayServerData(
                    join.RelayServer.IpV4,
                    (ushort)join.RelayServer.Port,
                    join.AllocationIdBytes,
                    join.Key,
                    join.ConnectionData,
                    join.HostConnectionData,
                    true);   // DTLS

                IsOnlineSession = true;
                GameSession.IsHost = false;

                if (!ConnectionManager.Instance.StartClientRaw())
                {
                    IsOnlineSession = false;
                    Status = "Could not connect";
                    return false;
                }

                Status = "Connected - waiting for the host";
                return true;
            }
            catch (Exception e)
            {
                IsOnlineSession = false;
                Status = "Wrong code, or the room has closed.";
                Debug.LogWarning($"[Online] join failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Forget the current online session (called when leaving a match).</summary>
        public static void Reset()
        {
            IsOnlineSession = false;
            JoinCode = "";
        }

        static UnityTransport Transport()
        {
            var nm = NetworkManager.Singleton;
            return nm != null ? nm.NetworkConfig.NetworkTransport as UnityTransport : null;
        }
    }
}
