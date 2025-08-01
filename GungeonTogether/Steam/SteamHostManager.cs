using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GungeonTogether.Steam
{
    /// <summary>
    /// Manages host discovery and multiplayer session management
    /// </summary>
    public class SteamHostManager
    {
        private static SteamHostManager instance;
        public static SteamHostManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new SteamHostManager();
                }
                return instance;
            }
        }

        // Steam invite handling
        private static ulong lastInvitedBySteamId = 0;
        private static string lastInviteLobbyId = "";
        private static ulong currentHostSteamId = 0; // Track who is currently hosting
        private static bool isCurrentlyHosting = false;

        // Automatic host discovery system
        private static Dictionary<ulong, HostInfo> availableHosts = new Dictionary<ulong, HostInfo>();

        // Current lobby state
        private static ulong currentLobbyId = 0;
        private static bool isLobbyHost = false;

        // Add caching for host scanning too
        private static float lastHostScan = 0f;
        private static readonly float hostScanInterval = 3.0f; // Scan for hosts every 3 seconds max

        public static System.Action<ulong, string> OnPlayerJoined;
        // Property accessors
        public static bool IsCurrentlyHosting => isCurrentlyHosting;
        public static ulong CurrentHostSteamId => currentHostSteamId;
        public static ulong CurrentLobbyId => currentLobbyId;
        public static bool IsLobbyHost => isLobbyHost;
        public static string LastInviteLobbyId => lastInviteLobbyId;


        public struct HostInfo
        {
            public ulong steamId;
            public string sessionName;
            public int playerCount;
            public float lastSeen;
            public bool isActive;
        }

        /// <summary>
        /// Automatically set invite info when Steam overlay invite is clicked
        /// This captures the real Steam ID from Steam's callback system
        /// </summary>
        public static void SetInviteInfo(ulong hostSteamId, string lobbyId = "")
        {
            lastInvitedBySteamId = hostSteamId;
            lastInviteLobbyId = lobbyId;
            // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Auto-received invite from Steam ID: {hostSteamId}");
            // Add to available hosts if not already there
            if (!availableHosts.ContainsKey(hostSteamId))
            {
                availableHosts[hostSteamId] = new HostInfo
                {
                    steamId = hostSteamId,
                    sessionName = "Friend's Session",
                    playerCount = 1,
                    lastSeen = Time.time,
                    isActive = true
                };
                // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Added host from invite: {hostSteamId}");
            }
        }

        /// <summary>
        /// Get the Steam ID of the last person who invited this player
        /// Returns 0 if no invite is available
        /// </summary>
        public static ulong GetLastInviterSteamId()
        {
            return lastInvitedBySteamId;
        }

        /// <summary>
        /// Get the most recent available host Steam ID for automatic joining
        /// </summary>
        public static ulong GetBestAvailableHost()
        {
            try
            {
                // Get our own Steam ID to exclude it
                ulong mySteamId = 0;
                try
                {
                    mySteamId = SteamReflectionHelper.GetSteamID();
                }
                catch (Exception)
                {
                    // GungeonTogether.Logging.Debug.LogWarning($"[ETGSteamP2P] Could not get own Steam ID for host filtering: {ex.Message}");
                }
                // First priority: Direct invite (but not from ourselves)
                if (!lastInvitedBySteamId.Equals(0UL) && !lastInvitedBySteamId.Equals(mySteamId))
                {
                    // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Using direct invite: {lastInvitedBySteamId}");
                    return lastInvitedBySteamId;
                }
                // Second priority: Most recent active host (excluding ourselves)
                ulong bestHost = 0;
                float mostRecent = 0;
                foreach (var kvp in availableHosts)
                {
                    var host = kvp.Value;
                    if (host.isActive &&
                        !ReferenceEquals(host.steamId, mySteamId) &&
                        !ReferenceEquals(host.steamId, currentHostSteamId) &&
                        host.lastSeen > mostRecent)
                    {
                        bestHost = host.steamId;
                        mostRecent = host.lastSeen;
                    }
                }
                // if (!ReferenceEquals(bestHost,0))
                // {
                //     GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Auto-selected best host: {bestHost}");
                // }
                // else
                // {
                //     GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] No available hosts found (excluding self)");
                // }
                return bestHost;
            }
            catch (Exception)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error finding best host");
                return 0;
            }
        }

        /// <summary>
        /// Clear invite information after use
        /// </summary>
        public static void ClearInviteInfo()
        {
            lastInvitedBySteamId = 0;
            lastInviteLobbyId = "";
            // GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] Cleared invite info");
        }

        /// <summary>
        /// Automatically register this player as a host when they start hosting
        /// </summary>
        public static void RegisterAsHost()
        {
            try
            {
                ulong mySteamId = SteamReflectionHelper.GetSteamID();
                if (!ReferenceEquals(mySteamId, 0))
                {
                    currentHostSteamId = mySteamId;
                    isCurrentlyHosting = true;
                    availableHosts[mySteamId] = new HostInfo
                    {
                        steamId = mySteamId,
                        sessionName = "My Session",
                        playerCount = 1,
                        lastSeen = Time.time,
                        isActive = true
                    };
                    InitializeLobbyCallbacks(); // Ensure callback is registered when hosting
                }
            }
            catch (Exception)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error registering as host");
            }
        }

        /// <summary>
        /// Stop hosting and clean up host registration
        /// </summary>
        public static void UnregisterAsHost()
        {
            try
            {
                if (isCurrentlyHosting && (!ReferenceEquals(currentHostSteamId, 0)))
                {
                    availableHosts.Remove(currentHostSteamId);
                    // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Unregistered as host: {currentHostSteamId}");
                }
                currentHostSteamId = 0;
                isCurrentlyHosting = false;
            }
            catch (Exception)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error unregistering as host");
            }
        }

        /// <summary>
        /// Broadcast host availability to the network (called periodically when hosting)
        /// </summary>
        public static void BroadcastHostAvailability()
        {
            try
            {
                if (isCurrentlyHosting && (!ReferenceEquals(currentHostSteamId, 0)))
                {
                    // Update our host info
                    if (availableHosts.ContainsKey(currentHostSteamId))
                    {
                        var info = availableHosts[currentHostSteamId];
                        info.lastSeen = Time.time;
                        info.isActive = true;
                        availableHosts[currentHostSteamId] = info;
                    }
                    // In a real implementation, this would broadcast via P2P or Steam Rich Presence
                    // For now, we'll rely on Rich Presence and lobby system
                }
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error broadcasting host availability: {e.Message}");
            }
        }

        /// <summary>
        /// <summary>
        /// Automatically discover available hosts on the network
        /// </summary>
        public static ulong[] GetAvailableHosts()
        {
            try
            {
                // Get our own Steam ID to exclude it from available hosts
                ulong mySteamId = 0;
                try
                {
                    mySteamId = SteamReflectionHelper.GetSteamID();
                }
                catch (Exception)
                {
                    // GungeonTogether.Logging.Debug.LogWarning($"[ETGSteamP2P] Could not get own Steam ID for host filtering: {ex.Message}");
                }
                // Clean up old hosts
                var hostsToRemove = new List<ulong>();
                foreach (var kvp in availableHosts)
                {
                    if (Time.time - kvp.Value.lastSeen > 30f)
                    {
                        hostsToRemove.Add(kvp.Key);
                    }
                }
                foreach (var hostId in hostsToRemove)
                {
                    availableHosts.Remove(hostId);
                }
                // CRITICAL: Actively scan Steam friends for ETG players who might be hosting
                try
                {
                    ScanFriendsForHosts();
                }
                catch (Exception)
                {
                    // GungeonTogether.Logging.Debug.LogWarning($"[ETGSteamP2P] Error scanning friends for hosts: {ex.Message}");
                }
                // Return active host Steam IDs, excluding our own Steam ID
                var activeHostsList = new List<ulong>();
                foreach (var kvp in availableHosts)
                {
                    if (kvp.Value.isActive && !ReferenceEquals(kvp.Key, mySteamId))
                    {
                        activeHostsList.Add(kvp.Key);
                    }
                }
                // if (mySteamId > 0 && activeHostsList.Count < availableHosts.Count)
                // {
                //     GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Filtered out own Steam ID {mySteamId} from available hosts");
                // }
                return activeHostsList.ToArray();
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error getting available hosts: {e.Message}");
                return new ulong[0];
            }
        }

        /// <summary>
        /// Get available hosts as a dictionary for compatibility with existing code
        /// </summary>
        public static Dictionary<ulong, HostInfo> GetAvailableHostsDict()
        {
            try
            {
                // Clean up old hosts
                var hostsToRemove = new List<ulong>();
                foreach (var kvp in availableHosts)
                {
                    if (Time.time - kvp.Value.lastSeen > 30f)
                    {
                        hostsToRemove.Add(kvp.Key);
                    }
                }
                foreach (var hostId in hostsToRemove)
                {
                    availableHosts.Remove(hostId);
                }
                return new Dictionary<ulong, HostInfo>(availableHosts);
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error getting available hosts dictionary: {e.Message}");
                return new Dictionary<ulong, HostInfo>();
            }
        }

        /// <summary>
        /// Setup Rich Presence for hosting a multiplayer session
        /// This enables "Join Game" in Steam overlay and friends list
        /// </summary>
        public static void StartHostingSession()
        {
            try
            {
                ulong steamId = SteamReflectionHelper.GetSteamID();
                if (ReferenceEquals(steamId, 0))
                {
                    GungeonTogether.Logging.Debug.LogError("[ETGSteamP2P] Cannot start hosting session - Steam ID not available");
                    return;
                }
                // Set Rich Presence to show we're hosting
                var setRichPresenceMethod = SteamReflectionHelper.SetRichPresenceMethod;
                if (!ReferenceEquals(setRichPresenceMethod, null))
                {
                    setRichPresenceMethod.Invoke(null, new object[] { "status", "In Game" });
                    setRichPresenceMethod.Invoke(null, new object[] { "steam_display", "#Status_InGame" });
                }
                // Register as host
                RegisterAsHost();
                // Create lobby if possible
                var createLobbyMethod = SteamReflectionHelper.CreateLobbyMethod;
                if (!ReferenceEquals(createLobbyMethod, null))
                {
                    try
                    {
                        var result = createLobbyMethod.Invoke(null, new object[] { 1, 50 });
                        isLobbyHost = true;
                        ulong lobbyId = 0;
                        if (!ReferenceEquals(result, null))
                        {
                            // Try to extract lobby ID robustly
                            if (result is ulong)
                            {
                                lobbyId = (ulong)result;
                            }
                            else
                            {
                                var type = result.GetType();
                                var mSteamIDProp = type.GetProperty("m_SteamID");
                                var steamIDProp = type.GetProperty("steamID");
                                var valueField = type.GetField("m_SteamID");
                                var altValueField = type.GetField("steamID");
                                if (!ReferenceEquals(mSteamIDProp, null))
                                {
                                    lobbyId = (ulong)mSteamIDProp.GetValue(result, null);
                                }
                                else if (!ReferenceEquals(steamIDProp, null))
                                {
                                    lobbyId = (ulong)steamIDProp.GetValue(result, null);
                                }
                                else if (!ReferenceEquals(valueField, null))
                                {
                                    lobbyId = (ulong)valueField.GetValue(result);
                                }
                                else if (!ReferenceEquals(altValueField, null))
                                {
                                    lobbyId = (ulong)altValueField.GetValue(result);
                                }
                                else
                                {
                                    // Try parsing ToString() as ulong
                                    ulong parsedId = 0;
                                    if (ulong.TryParse(result.ToString(), out parsedId) && !ReferenceEquals(parsedId, 0UL))
                                    {
                                        lobbyId = parsedId;
                                    }
                                    else
                                    {
                                        GungeonTogether.Logging.Debug.LogError($"[Host manager] Could not extract lobby ID from result of type {type.FullName}. ");
                                        GungeonTogether.Logging.Debug.LogError($"[Host manager] Result: {result}");
                                    }
                                }
                            }
                        }
                        if (!ReferenceEquals(lobbyId, 0))
                        {
                            currentLobbyId = lobbyId;
                            // Set lobby joinable and public
                            var setLobbyJoinableMethod = SteamReflectionHelper.SetLobbyJoinableMethod;
                            if (!ReferenceEquals(setLobbyJoinableMethod, null))
                            {
                                var csteamId = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
                                setLobbyJoinableMethod.Invoke(null, new object[] { csteamId, true });
                            }
                            GungeonTogether.Logging.Debug.Log($"[Host manager] Hosting joinable/public lobby with ID: {currentLobbyId}");
                            UpdateRichPresenceConnectToLobby();
                        }
                        else
                        {
                            GungeonTogether.Logging.Debug.LogError($"[Host manager] Failed to get valid lobby ID from CreateLobby result. Result: {result}");
                        }
                    }
                    catch (Exception e)
                    {
                        GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Could not create lobby: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error starting hosting session: {e.Message}");
            }
        }

        /// <summary>
        /// Setup Rich Presence for joining a multiplayer session
        /// </summary>
        public static void StartJoiningSession(ulong hostSteamId)
        {
            try
            {
                var setRichPresenceMethod = SteamReflectionHelper.SetRichPresenceMethod;
                if (!ReferenceEquals(setRichPresenceMethod, null))
                {
                    // Set status to show we're joining a game
                    setRichPresenceMethod.Invoke(null, new object[] { "status", "Joining Game" });
                    setRichPresenceMethod.Invoke(null, new object[] { "steam_display", "#Status_JoiningGame" });
                    GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Started joining session with host/lobby: {hostSteamId}");
                    // Actually join the lobby
                    JoinLobby(hostSteamId);
                }
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error starting joining session: {e.Message}");
            }
        }

        /// <summary>
        /// Stop multiplayer session and clear Rich Presence
        /// </summary>
        public static void StopSession()
        {
            try
            {
                // Clear Rich Presence
                var clearRichPresenceMethod = SteamReflectionHelper.ClearRichPresenceMethod;
                if (!ReferenceEquals(clearRichPresenceMethod, null))
                {
                    clearRichPresenceMethod.Invoke(null, null);
                    GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] Cleared Rich Presence");
                }

                // Leave lobby if we're in one
                if (!ReferenceEquals(currentLobbyId, 0))
                {
                    var leaveLobbyMethod = SteamReflectionHelper.LeaveLobbyMethod;
                    if (!ReferenceEquals(leaveLobbyMethod, null))
                    {
                        var steamIdParam = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
                        leaveLobbyMethod.Invoke(null, new object[] { steamIdParam });
                        GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Left lobby: {currentLobbyId}");
                    }

                    currentLobbyId = 0;
                    isLobbyHost = false;
                }

                // Unregister as host
                UnregisterAsHost();

                GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] Stopped multiplayer session");
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error stopping session: {e.Message}");
            }
        }

        /// <summary>
        /// Set lobby metadata that friends can see
        /// </summary>
        public static bool SetLobbyData(string key, string value)
        {
            try
            {
                if (ReferenceEquals(currentLobbyId, 0) || ReferenceEquals(SteamReflectionHelper.SetLobbyDataMethod, null))
                    return false;

                var steamIdParam = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
                var result = SteamReflectionHelper.SetLobbyDataMethod.Invoke(null, new object[] { steamIdParam, key, value });

                if (!ReferenceEquals(result, null) && result is bool success)
                {
                    if (success)
                    {
                        GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Set lobby data - {key}: {value}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error setting lobby data: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a Steam lobby for multiplayer session
        /// </summary>
        public static bool CreateLobby(int maxPlayers = 50)
        {
            try
            {
                var createLobbyMethod = SteamReflectionHelper.CreateLobbyMethod;
                if (!ReferenceEquals(createLobbyMethod, null))
                {
                    // Create lobby with specified parameters
                    // ELobbyType.k_ELobbyTypePublic = 1, maxPlayers
                    var result = createLobbyMethod.Invoke(null, new object[] { 1, maxPlayers });

                    if (!ReferenceEquals(result, null))
                    {
                        GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Creating lobby for {maxPlayers} players...");
                        isLobbyHost = true;
                        // Set the lobby ID from the result if possible
                        ulong lobbyId = 0;
                        if (result is ulong)
                        {
                            lobbyId = (ulong)result;
                        }
                        else if (!ReferenceEquals(result.GetType().GetProperty("m_SteamID"), null))
                        {
                            lobbyId = (ulong)result.GetType().GetProperty("m_SteamID").GetValue(result, null);
                        }
                        if (!ReferenceEquals(lobbyId, 0))
                        {
                            currentLobbyId = lobbyId;
                            // Set lobby joinable and public
                            var setLobbyJoinableMethod = SteamReflectionHelper.SetLobbyJoinableMethod;
                            if (!ReferenceEquals(setLobbyJoinableMethod, null))
                            {
                                var csteamId = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
                                setLobbyJoinableMethod.Invoke(null, new object[] { csteamId, true });
                            }
                            GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Hosting joinable/public lobby with ID: {currentLobbyId}");
                            UpdateRichPresenceConnectToLobby();
                        }
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error creating lobby: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Join a Steam lobby by ID
        /// </summary>
        public static bool JoinLobby(ulong lobbyId)
        {
            try
            {
                var joinLobbyMethod = SteamReflectionHelper.JoinLobbyMethod;
                if (!ReferenceEquals(joinLobbyMethod, null))
                {
                    var steamIdParam = SteamReflectionHelper.ConvertToCSteamID(lobbyId);
                    var result = joinLobbyMethod.Invoke(null, new object[] { steamIdParam });

                    if (!ReferenceEquals(result, null))
                    {
                        GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Joining lobby: {lobbyId}");
                        currentLobbyId = lobbyId;
                        isLobbyHost = false;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error joining lobby: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Leave current lobby
        /// </summary>
        public static bool LeaveLobby()
        {
            try
            {
                if (!ReferenceEquals(currentLobbyId, 0) && !ReferenceEquals(SteamReflectionHelper.LeaveLobbyMethod, null))
                {
                    var steamIdParam = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
                    SteamReflectionHelper.LeaveLobbyMethod.Invoke(null, new object[] { steamIdParam });

                    GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Left lobby: {currentLobbyId}");
                    currentLobbyId = 0;
                    isLobbyHost = false;
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error leaving lobby: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scan Steam friends to find those playing ETG who might be hosting GungeonTogether
        /// </summary>
        public static void ScanFriendsForHosts()
        {
            try
            {
                // Don't scan too frequently
                if (Time.time - lastHostScan < hostScanInterval)
                {
                    return;
                }

                lastHostScan = Time.time;

                // GungeonTogether.Logging.Debug.Log("[SteamHostManager] Scanning friends for GungeonTogether hosts...");

                // Get Steam friends who are playing ETG
                var friends = SteamFriendsHelper.GetSteamFriends();

                // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Scanning {friends.Length} Steam friends for GungeonTogether hosts...");

                int etgPlayersFound = 0;
                int actualHostsFound = 0;
                int potentialHostsAdded = 0;

                foreach (var friend in friends)
                {
                    if (friend.isPlayingETG && friend.isOnline)
                    {
                        etgPlayersFound++;

                        // GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Found friend {friend.name} ({friend.steamId}) playing ETG - checking if hosting GungeonTogether...");

                        // Check if this friend is actually hosting GungeonTogether by checking Rich Presence
                        bool isHostingGungeonTogether = false;
                        try
                        {
                            // Check for GungeonTogether-specific Rich Presence keys
                            string gungeonTogetherStatus = SteamReflectionHelper.GetFriendRichPresence(friend.steamId, "gungeon_together");
                            string gtVersion = SteamReflectionHelper.GetFriendRichPresence(friend.steamId, "gt_version");
                            string connectString = SteamReflectionHelper.GetFriendRichPresence(friend.steamId, "connect");

                            // Friend is hosting if they have GungeonTogether Rich Presence set to "hosting"
                            if (string.Equals(gungeonTogetherStatus, "hosting") ||
                                (!string.IsNullOrEmpty(gtVersion) && !string.IsNullOrEmpty(connectString)))
                            {
                                isHostingGungeonTogether = true;
                                actualHostsFound++;
                                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] ✅ {friend.name} is hosting GungeonTogether (status: {gungeonTogetherStatus}, version: {gtVersion})");
                            }
                            else if (!string.IsNullOrEmpty(gungeonTogetherStatus))
                            {
                                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] 📝 {friend.name} is playing GungeonTogether but not hosting (status: {gungeonTogetherStatus})");
                            }
                            else
                            {
                                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] ❌ {friend.name} is playing Enter the Gungeon but not GungeonTogether (no GT Rich Presence)");
                            }
                        }
                        catch (Exception exception)
                        {
                            GungeonTogether.Logging.Debug.LogWarning($"[ETGSteamP2P] Could not check Rich Presence for {friend.name}: {exception.Message}");
                        }

                        // Only add as host if they're actually hosting GungeonTogether
                        if (isHostingGungeonTogether)
                        {
                            if (!availableHosts.ContainsKey(friend.steamId))
                            {
                                availableHosts[friend.steamId] = new HostInfo
                                {
                                    steamId = friend.steamId,
                                    sessionName = $"{friend.name}'s GungeonTogether",
                                    playerCount = 1,
                                    lastSeen = Time.time,
                                    isActive = true
                                };
                                potentialHostsAdded++;

                                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] ✅ Added {friend.name} as confirmed GungeonTogether host");
                            }
                            else
                            {
                                // Update existing entry
                                var hostInfo = availableHosts[friend.steamId];
                                hostInfo.lastSeen = Time.time;
                                hostInfo.isActive = true;
                                hostInfo.sessionName = $"{friend.name}'s GungeonTogether";
                                availableHosts[friend.steamId] = hostInfo;


                                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] 🔄 Updated existing host entry for {friend.name}");
                            }
                        }
                    }
                }

                GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Friend scan complete: {etgPlayersFound} playing ETG, {actualHostsFound} hosting GungeonTogether, {potentialHostsAdded} new hosts added");

                if (etgPlayersFound == 0)
                {
                    GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] No friends currently playing Enter the Gungeon");
                }
                else if (actualHostsFound == 0)
                {
                    GungeonTogether.Logging.Debug.Log("[ETGSteamP2P] No friends currently hosting GungeonTogether multiplayer sessions");
                }
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[ETGSteamP2P] Error scanning friends for hosts: {e.Message}");
            }
        }

        /// <summary>
        /// Set the Rich Presence 'connect' field to the current lobby ID (if valid)
        /// </summary>
        private static void UpdateRichPresenceConnectToLobby()
        {
            var setRichPresenceMethod = SteamReflectionHelper.SetRichPresenceMethod;
            if (!ReferenceEquals(setRichPresenceMethod, null) && !ReferenceEquals(currentLobbyId, 0))
            {
                GungeonTogether.Logging.Debug.Log($"[Host Manager] Setting Rich Presence 'connect' to lobby ID: {currentLobbyId}");
                setRichPresenceMethod.Invoke(null, new object[] { "connect", currentLobbyId.ToString() });
            }
        }

        /// <summary>
        /// Log when a player joins via invite or overlay (for debugging/analytics)
        /// </summary>
        public static void LogPlayerJoinedViaInviteOrOverlay(ulong steamId)
        {
            GungeonTogether.Logging.Debug.Log($"[ETGSteamP2P] Player joined via invite/overlay: SteamID={steamId}");
        }

        /// <summary>
        /// Polls the current lobby for new members and logs when someone joins.
        /// Should be called periodically by the host.
        /// </summary>
        private static HashSet<ulong> _lastLobbyMembers = new HashSet<ulong>();

        /// <summary>
        /// Checks for new players joining the current lobby using Steamworks ISteamMatchmaking.
        /// Should be called periodically while hosting.
        /// </summary>
        public static void CheckForLobbyJoins()
        {
            // Only poll when needed, not every frame
            if (!isLobbyHost || ReferenceEquals(currentLobbyId, 0UL))
                return;

            var steamworksAssembly = SteamReflectionHelper.GetSteamworksAssembly();
            if (ReferenceEquals(steamworksAssembly, null))
            {
                GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] Steamworks assembly is null");
                return;
            }

            var matchmakingType = steamworksAssembly.GetType("Steamworks.SteamMatchmaking", false);
            if (ReferenceEquals(matchmakingType, null))
            {
                GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] SteamMatchmaking type is null");
                return;
            }

            var getNumMembersMethod = matchmakingType.GetMethod("GetNumLobbyMembers");
            var getMemberByIndexMethod = matchmakingType.GetMethod("GetLobbyMemberByIndex");
            if (ReferenceEquals(getNumMembersMethod, null) || ReferenceEquals(getMemberByIndexMethod, null))
            {
                GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] Could not get GetNumLobbyMembers or GetLobbyMemberByIndex method");
                return;
            }

            var csteamId = SteamReflectionHelper.ConvertToCSteamID(currentLobbyId);
            GungeonTogether.Logging.Debug.Log($"[SteamHostManager] Converted currentLobbyId to csteamId: {csteamId} (type: {(csteamId == null ? "null" : csteamId.GetType().FullName)})");
            int memberCount = 0;
            try
            {
                var countObj = getNumMembersMethod.Invoke(null, new object[] { csteamId });
                GungeonTogether.Logging.Debug.Log($"[SteamHostManager] GetNumLobbyMembers returned: {countObj} (csteamId: {csteamId}, currentLobbyId: {currentLobbyId})");
                if (!ReferenceEquals(countObj, null))
                    memberCount = Convert.ToInt32(countObj);
            }
            catch (Exception ex)
            {
                GungeonTogether.Logging.Debug.LogError($"[SteamHostManager] Error getting lobby member count: {ex.Message} (csteamId: {csteamId}, currentLobbyId: {currentLobbyId})");
                return;
            }

            var currentMembers = new HashSet<ulong>();
            for (int i = 0; i < memberCount; i++)
            {
                try
                {
                    var memberObj = getMemberByIndexMethod.Invoke(null, new object[] { csteamId, i });
                    GungeonTogether.Logging.Debug.Log($"[SteamHostManager] GetLobbyMemberByIndex({i}) returned: {memberObj}");
                    ulong memberId = 0;
                    if (memberObj is ulong ul)
                    {
                        memberId = ul;
                    }
                    else if (!ReferenceEquals(memberObj, null))
                    {
                        var mSteamIDProp = memberObj.GetType().GetProperty("m_SteamID");
                        if (!ReferenceEquals(mSteamIDProp, null))
                        {
                            memberId = (ulong)mSteamIDProp.GetValue(memberObj, null);
                        }
                        else
                        {
                            ulong.TryParse(memberObj.ToString(), out memberId);
                        }
                    }
                    if (!ReferenceEquals(memberId, 0UL))
                        currentMembers.Add(memberId);
                }
                catch (Exception ex)
                {
                    GungeonTogether.Logging.Debug.LogError($"[SteamHostManager] Error getting lobby member at index {i}: {ex.Message}");
                }
            }
            GungeonTogether.Logging.Debug.Log($"[SteamHostManager] Current lobby members: [{string.Join(", ", currentMembers.Select(x => x.ToString()).ToArray())}]");

            // Detect new members
            foreach (var memberId in currentMembers)
            {
                if (!_lastLobbyMembers.Contains(memberId))
                {
                    GungeonTogether.Logging.Debug.Log($"[SteamHostManager] Detected new player joined lobby: {memberId}");
                    OnPlayerJoined?.Invoke(memberId, currentLobbyId.ToString());
                }
            }
            _lastLobbyMembers = currentMembers;
        }

        /// <summary>
        /// Alias for CheckForLobbyJoins for compatibility with legacy code.
        /// </summary>
        public static void PollAndLogLobbyJoins()
        {
            CheckForLobbyJoins();
        }

        private static object lobbyDataUpdateCallbackInstance;

        /// <summary>
        /// Initialize Steam lobby callbacks for host join detection
        /// </summary>
        public static void InitializeLobbyCallbacks()
        {
            try
            {
                var callbackType = SteamReflectionHelper.LobbyDataUpdateCallbackType;
                if (ReferenceEquals(callbackType, null))
                {
                    GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] LobbyDataUpdate_t callback type not found");
                    return;
                }
                var steamworksAssembly = SteamReflectionHelper.GetSteamworksAssembly();
                if (ReferenceEquals(steamworksAssembly, null))
                {
                    GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] Steamworks assembly not found");
                    return;
                }
                // Find callback base type
                var callbackBaseType = steamworksAssembly.GetType("Steamworks.Callback", false)
                    ?? steamworksAssembly.GetType("Steamworks.Callback`1", false)
                    ?? steamworksAssembly.GetType("Steamworks.CCallbackBase", false)
                    ?? steamworksAssembly.GetType("Steamworks.CallResult", false)
                    ?? steamworksAssembly.GetType("Steamworks.CallResult`1", false);
                if (ReferenceEquals(callbackBaseType, null))
                {
                    GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] Steam callback base type not found");
                    return;
                }
                // Use SteamCallbackManager.TryRegisterCallback
                var tryRegisterCallback = typeof(SteamCallbackManager)
                    .GetMethod("TryRegisterCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                bool registered = (bool)(tryRegisterCallback?.Invoke(null, new object[] { steamworksAssembly, callbackBaseType, callbackType, "OnLobbyDataUpdate" }) ?? false);
                if (registered)
                {
                    GungeonTogether.Logging.Debug.Log("[SteamHostManager] Registered LobbyDataUpdate_t callback for join detection");
                }
                else
                {
                    GungeonTogether.Logging.Debug.LogWarning("[SteamHostManager] Failed to register LobbyDataUpdate_t callback using SteamCallbackManager.TryRegisterCallback");
                }
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[SteamHostManager] Failed to register LobbyDataUpdate_t callback: {e.Message}");
            }
        }

        /// <summary>
        /// Get available hosts for joining as a List
        /// </summary>
        public List<HostInfo> GetAvailableHostsList()
        {
            var hostList = new List<HostInfo>();
            foreach (var kvp in availableHosts)
            {
                if (kvp.Value.isActive && Time.time - kvp.Value.lastSeen < 30f) // Only include recently seen hosts
                {
                    hostList.Add(kvp.Value);
                }
            }
            return hostList;
        }

        /// <summary>
        /// Join a specific host
        /// </summary>
        public void JoinHost(ulong hostSteamId)
        {
            try
            {
                // TODO: Implement actual host joining logic
                GungeonTogether.Logging.Debug.Log($"[SteamHostManager] Attempting to join host {hostSteamId}");
                currentHostSteamId = hostSteamId;
            }
            catch (Exception e)
            {
                GungeonTogether.Logging.Debug.LogError($"[SteamHostManager] Error joining host: {e.Message}");
            }
        }


    }
}
