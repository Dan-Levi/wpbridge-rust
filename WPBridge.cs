using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WordPress Bridge", "Murky", "1.2.12")]
    [Description("WordPress Bridge integrates Rust servers with Wordpress, making it possible to embed player and server statistics on your Wordpress site with shortcodes.")]
    internal class WPBridge : RustPlugin
    {
        #region Singleton
        // Singleton instance. Practical when using static methods when we need to refer to plugin 
        WPBridge GetInstance()
        {
            if (_instance == null) _instance = this;
            return _instance;
        }
        #endregion

        #region Variables
        // Only set true if validation and endpoint conditions in Init method is met.
        private bool _isConfiguredAndReady = false;
        // Plugin configuration
        private Configuration _config = new Configuration();
        // Singleton instance
        static WPBridge _instance;
        // Web Request wrapper
        public static WebRequester webRequester = new WebRequester();
        // The class holding the server information such as seed, level etc.
        WPBServer WPBServerData;
        // A list of all active players
        List<WPBPlayer> WPBPlayerData;
        // Group name permissions.
        string _reservedPlayerGroupName = "wpbridge_reserved_players";
        //WordPress request object to send to server;
        WordPressBridgeRequest wordPressRequest;
        Timer syncTimer;
        string endPointUriSecret;
        string endPointUriSync;
        static List<string> WPBPlayersLeftSteamIds = new List<string>();
        System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
        #endregion

        #region Configuration
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                // Log and print errors if configuration fails to load
                LogToFile("ErrorLog", $"[{DateTime.Now}] [LoadConfig] Configuration file contains an error. Using default configuration values.", this);
                PrintError("ERROR: " + "Your configuration file contains an error. Using default configuration values.");

                LoadDefaultConfig();
            }
        }
        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(_config);
        private void InitConfig(Action<bool, string> p)
        {
            LoadConfig();
            // Try validate the external IP for the Rust server
            if (_config.External_IP == "" || _config.External_IP == "PASTE_EXTERNAL_IP_HERE" || !ValidIPv4(_config.External_IP))
            {
                // Using webRequester to request danlevi service for external ip
                webRequester.Post("https://danlevi.no/services/whatismyip/", null, (code, ip) => {
                    if(code != 0)
                    {
                        // Remove any occurences of line feed (LF) character / carriage return (CR) character
                        ip = ip.Replace("\n", "").Replace("\r", "");
                        if (code == 200 && ValidIPv4(ip))
                        {
                            _config.External_IP = ip;
                            SaveConfig();
                        }
                    }
                    p(false, "External IP needs to be set.");
                    return;
                });
            }
            if (_config.Wordpress_Site_URL == "" || _config.Wordpress_Site_URL == "PASTE_WORDPRESS_SITE_URL_HERE")
            {
                p(false, "Wordpress Site Url needs to be set.");
                return;
            }
            if (!ValidHttpURL(_config.Wordpress_Site_URL))
            {
                var errorMessage = $"Wordpress Site Url seems to be invalid!";
                errorMessage += $"\n\r\n\r> WordPress Bridge needs the fully qualified site url to where you have installed WordPress.";
                errorMessage += $"\n\r> In config: [{_config.Wordpress_Site_URL}]";
                errorMessage += $"\n\r> Example:   [http://www.your-wordpress-site.com/]";
                errorMessage += $"\n\r";
                p(false, errorMessage);
                return;
            }
            if (!_config.Wordpress_Site_URL.EndsWith("/"))
            {
                p(false, "Wordpress Site Url must end with a trailing slash. [http://www.your-wordpress-site.com/]");
                return;
            }
            if (_config.Wordpress_Secret == "" || _config.Wordpress_Secret == "PASTE_WPBRIDGE_UNIQUE_SECRET_HERE")
            {
                p(false, "Wordpress secret needs to be set.");
                return;
            }
            if (_config.UpdateInterval < 5)
            {
                p(false, "Update interval cannot be less than 5 seconds.");
                return;
            }
            p(true, "");
        }
        #endregion

        #region WordPress
        void WordPressSiteIsUp(Action<bool,string> returnMethod)
        {
            webRequester.Get($"{_config.Wordpress_Site_URL}index.php/wp-json", null, (code, json) => {
                returnMethod(code != 200, json);
                return;
            });
        }
        bool WordPressPluginInstalled(WordPressJson json)
        {
            return json.routes.Wpbridge != null;
        }
        private void TryValidateWordPressSecret(Action<bool> returnMethod)
        {
            wordPressRequest = new WordPressBridgeRequest(_config.Wordpress_Secret);
            webRequester.Post(endPointUriSecret, wordPressRequest, (code, json) =>
            {
                if (code != 200)
                {
                    returnMethod(true);
                    return;
                }
                var wordPressResponse = JsonConvert.DeserializeObject<WordPressBridgeResponse>(json);
                if (wordPressResponse.code != "success")
                {
                    returnMethod(true);
                    return;
                }
                returnMethod(false);                
            });
        }
        #endregion

        #region Main Logic

        #region Initialization
        private void WPBRidgeInit()
        {
            if(TryCreateReservedGroup())
            {
                UpdateWPBPlayers();

                TryValidateWordPressSecret((err) =>
                {
                    if(err)
                    {
                        PrintError($"[TryValidateWordPressSecret] Failed to validate secret. Secret missing or mismatch. Have you updated the WordPress plugin?");
                        timer.Once(5f,() => {
                            WPBRidgeInit();
                        });
                        return;
                    }
                    PrintDebug($"[TryValidateWordPressSecret] Secret validated");
                    Sync();
                });
                return;
            }
            PrintError($"[TryCreateReservedGroup] Failed to create reserved player group.");
            timer.Once(5f, () =>
            {
                WPBRidgeInit();
            });
        }
        #endregion

        #region Sync
        void Sync()
        {
            WPBServerData.UpdatePlayerCount();
            UpdateWPBPlayers();
            wordPressRequest = new WordPressBridgeRequest(_config.Wordpress_Secret);
            wordPressRequest.SetServerData(WPBServerData);
            float serializedRequestSize = (float)(JsonConvert.SerializeObject(wordPressRequest).Length * 2) / 1024;
            string payloadSizeFormatted = serializedRequestSize.ToString("0.00");
            if (WPBPlayerData != null && WPBPlayerData.Count > 0)
            {
                wordPressRequest.SetPlayerData(WPBPlayerData);
                PrintDebug($"[Sync] Sending {payloadSizeFormatted}kB of statistics for {WPBPlayerData.Count} players.");
            }
            else
            {
                PrintDebug($"[Sync] Sending {payloadSizeFormatted}kB of statistics, (No players on, syncing server data only).");
            }
            stopWatch.Start();
            webRequester.Post(endPointUriSync, wordPressRequest, (code, json) =>
            {
                ClearWPBPlayerStats();
                ClearWPBPlayerLoot();
                if (code != 200)
                {
                    PrintDebug("[Sync] Failed to read to response from WordPress");
                    LoadConfig();
                    PrintDebug(json);
                    return;
                }
                WordPressBridgeResponse wordPressResponse = null;
                try
                {
                    wordPressResponse = JsonConvert.DeserializeObject<WordPressBridgeResponse>(json);
                }
                catch (JsonReaderException jsonException)
                {
                    stopWatch.Stop();
                    PrintDebug($"[Sync] The exchange took {stopWatch.ElapsedMilliseconds} milliseconds but was unsuccessful.");
                    stopWatch.Reset();
                    PrintError("[Sync] [JsonReaderException] Error parsing JSON response!");
                    PrintError(jsonException.Message);
                    PrintError(json);
                }
                if (wordPressResponse != null && wordPressResponse.code == "success")
                {
                    stopWatch.Stop();
                    PrintDebug($"[Sync] The exchange took {stopWatch.ElapsedMilliseconds} milliseconds.");
                    stopWatch.Reset();
                }
                if (WPBPlayersLeftSteamIds != null && WPBPlayersLeftSteamIds.Count > 0)
                {
                    if(WPBPlayerData != null)
                    {
                        WPBPlayerData.RemoveAll(wpbPlayer => { return WPBPlayersLeftSteamIds.Contains(wpbPlayer.SteamID); });
                        WPBPlayersLeftSteamIds.Clear();
                    }
                }
            });
            timer.Once(_config.UpdateInterval, Sync);
        }
        #endregion

        #region Loot
        void WPBPlayerOnLoot(Item item, BasePlayer basePlayer)
        {
            if (item == null || item.info == null || basePlayer == null) return;
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer == null) return;
            if (item.info.name.EndsWith(".item"))
            {
                int itemNameLength = item.info.name.Length;
                var itemName = item.info.name.Replace(".item", "");
                if (itemName.Length < itemNameLength)
                {
                    var itemAmount = item.amount;
                    if (wpbPlayer.LootedItems.Count > 0)
                    {
                        var lootItem = wpbPlayer.LootedItems.Where(l => l.Name == itemName).FirstOrDefault();
                        if (lootItem != null)
                        {
                            lootItem.Amount += itemAmount;
                        }
                        else
                        {
                            wpbPlayer.LootedItems.Add(new LootItem(itemName, itemAmount));
                        }
                    }
                    else
                    {
                        wpbPlayer.LootedItems.Add(new LootItem(itemName, itemAmount));
                    }
                }
            }
        }
        #endregion

        #region Players
        /// <summary>
        /// Returns the WPBPlayer that matches SteamID on BasePlayer
        /// </summary>
        /// <param name="basePlayer"></param>
        /// <returns>WPBPlayer or NULL</returns>
        private WPBPlayer GetWPBPlayer(BasePlayer basePlayer)
        {
            if (basePlayer == null || WPBPlayerData == null || WPBPlayerData.Count == 0) return null;
            return WPBPlayerData.FirstOrDefault(bp => bp.SteamID == basePlayer.UserIDString);
        }

        /// <summary>
        /// Returns the WPBPlayer that matches SteamID on IPlayer
        /// </summary>
        /// <param name="iPlayer"></param>
        /// <returns>WPBPlayer or NULL</returns>
        private WPBPlayer GetWPBPlayer(IPlayer iPlayer)
        {
            if (iPlayer == null || WPBPlayerData == null || WPBPlayerData.Count == 0) return null;
            return WPBPlayerData.FirstOrDefault(bp => bp.SteamID == iPlayer.Id);
        }

        /// <summary>
        /// Update all WPBPlayers in WPBPlayers list if the BasePlayer is not reserved from sharing statistics
        /// </summary>
        private void UpdateWPBPlayers()
        {
            var activePlayers = BasePlayer.activePlayerList;
            if (activePlayers.Count > 0)
            {
                if (WPBPlayerData == null) WPBPlayerData = new List<WPBPlayer>();
                activePlayers.ToList().ForEach(p => {
                    if (WPBPlayerIsReserved(p)) return;
                    WPBUpdatePlayer(p);
                });
            }
        }

        private void ClearWPBPlayerStats()
        {
            if (WPBPlayerData != null && WPBPlayerData.Count > 0) WPBPlayerData.ForEach(wpbPlayer => { wpbPlayer.ClearStats(); });
        }

        private void ClearWPBPlayerLoot()
        {
            if (WPBPlayerData != null && WPBPlayerData.Count > 0) WPBPlayerData.ForEach(wpbPlayer => { wpbPlayer.ClearLoot(); });
        }

        /// <summary>
        /// Updates the WPBPlayer position or adding if player doesnt exist 
        /// </summary>
        /// <param name="basePlayer"></param>
        private void WPBUpdatePlayer(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = new WPBPlayer(basePlayer);
            if (WPBPlayerExists(wpbPlayer))
            {
                wpbPlayer = WPBPlayerData.Find(p => p.SteamID == wpbPlayer.SteamID);
                wpbPlayer.SetPosition(basePlayer.GetNetworkPosition());
                return;
            }
            else
            {
                WPBPlayerData.Add(wpbPlayer);
            }
        }

        /// <summary>
        /// Returns true if the WPBPlayers list contain the WPBPlayer. If not returns false.
        /// </summary>
        /// <param name="wpbPlayer">WPBPlayer object.</param>
        /// <returns>true or false.</returns>
        private bool WPBPlayerExists(WPBPlayer wpbPlayer)
        {
            return WPBPlayerData.Find(p => p.SteamID == wpbPlayer.SteamID) != null;
        }

        private WPBPlayer CreateWPBPlayer(IPlayer iPlayer)
        {
            if (iPlayer != null) return new WPBPlayer(iPlayer);
            return null;
        }

        private void InsertWPBPlayer(WPBPlayer wpbPlayer)
        {
            if (WPBPlayerData != null && wpbPlayer != null) WPBPlayerData.Add(wpbPlayer);
        }
        private void InsertWPBPlayer(IPlayer iPlayer)
        {
            if (WPBPlayerData != null && iPlayer != null) WPBPlayerData.Add(new WPBPlayer(iPlayer));
        }

        /// <summary>
        /// Removes the WPBPlayer from the WPBPlayers list if WPBPlayers list contains the WPBPlayer
        /// </summary>
        /// <param name="wpbPlayer"></param>
        private void RemoveWPBPlayer(WPBPlayer wpbPlayer)
        {
            if (wpbPlayer != null && WPBPlayerData.Contains(wpbPlayer)) WPBPlayerData.Remove(wpbPlayer);
        }
        private void RemoveWPBPlayer(BasePlayer basePlayer)
        {
            if(basePlayer != null)
            {
                WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
                if (wpbPlayer != null) WPBPlayerData.Remove(wpbPlayer);
            }
        }
        #endregion

        #endregion

        #region Rust Hooks

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Endpoint connection established", "Endpoint connection established"},
                {"Could not communicate with WordPress Rest API", "Couldn't communicate with WordPress Rest API using endpoint"},
                {"Could not authenticate with WPBridge for Rust", "Couldn't authenticate with WPBridge for Rust. Is the plugin installed and activated?"},
                {"TypeWIPHelp", "Type /wip.help to see a list of commands"},
                {"You are currently", "You are currently"},
                {"sharing your statistics", "sharing your statistics. You can always toggle this on/off using chatcommand /wip.reserve"},
                {"player looted", "Player looted"},
                {"which currently is not tracked", "which currently is not tracked."},
                {"Available commands", "Available commands"},
                {"Toggles share statistics.", "Toggles share statistics."},
                {"share statistics", "share statistics"},
                {"sharing statistics", "sharing statistics."},
                {"Check if you are sharing your statistics", "Check if you are sharing your statistics."},
                {"Your statistics are not shared", "Reserved. Your statistics are not shared."},
                {"Your statistics are shared", "Reservation removed. Your statistics are shared."},
                {"DEFAULT", "DEFAULT"},
                {"not", "not"},
            }, this);
        }
        #endregion

        #region Server Hooks
        void Init()
        {
            _instance = GetInstance();
            InitConfig((bool isConfigured, string error) =>
            {
                if (!isConfigured)
                {
                    PrintDebug($"[CONFIG] {error}");
                    timer.Once(5, () =>
                     {
                         Init();
                     });
                    return;
                }
                WordPressSiteIsUp((err,json) => {
                    if (err)
                    {
                        PrintDebug($"[INIT] {GetMsg("Could not communicate with WordPress Rest API")}: {_config.Wordpress_Site_URL}wp-json");
                        timer.Once(5, () =>
                        {
                            Init();
                        });
                        return;
                    }
                    var wpJson = JsonConvert.DeserializeObject<WordPressJson>(json);
                    if(!WordPressPluginInstalled(wpJson))
                    {
                        PrintDebug($"[WordPress] {GetMsg("Could not authenticate with WPBridge for Rust")}");
                        timer.Once(5, () =>
                        {
                            Init();
                        });
                        return;
                    }
                    WPBServerData = new WPBServer(_config.External_IP,ConVar.Server.port,ConVar.Server.level,ConVar.Server.identity,ConVar.Server.seed,ConVar.Server.worldsize,ConVar.Server.maxplayers,ConVar.Server.hostname,ConVar.Server.description);
                    _isConfiguredAndReady = true;
                    endPointUriSecret = $"{_config.Wordpress_Site_URL}index.php/wp-json/wpbridge/secret";
                    endPointUriSync = $"{_config.Wordpress_Site_URL}index.php/wp-json/wpbridge/player-stats";
                    PrintDebug(GetMsg("Endpoint connection established"));
                });
            });
        }
        void OnServerInitialized(bool initial)
        {
            if(!_isConfiguredAndReady)
            {

                timer.Once(2f, () =>
                {
                    OnServerInitialized(false);
                });
                return;
            }
            WPBRidgeInit();
        }

        #endregion

        #region Player Hooks
        #region Connect / Disconnect / Respawn
        void OnPlayerConnected(BasePlayer basePlayer)
        {
            if (basePlayer == null) return;
            if (basePlayer.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(2f, () => OnPlayerConnected(basePlayer));
                return;
            }
            //Tell the player that stats are stored unless command is used
            string isReservedString;
            if (WPBPlayerIsReserved(basePlayer))
            {
                isReservedString = GetMsg("not") + " ";
                WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
                if (wpbPlayer != null) RemoveWPBPlayer(basePlayer);
            }
            else
            {
                isReservedString = "";
            }
            basePlayer.ChatMessage($"[WIP] {GetMsg("TypeWIPHelp")}");
            basePlayer.ChatMessage($"[WIP] {GetMsg("You are currently")} {isReservedString}{GetMsg("sharing your statistics")}");
        }
        void OnUserConnected(IPlayer iPlayer)
        {
            if (iPlayer == null) return;
            if (WPBPlayerIsReserved(iPlayer)) return; // Player is reserved and statistics should not be shared
            WPBPlayer wpbPlayer = GetWPBPlayer(iPlayer);
            if (wpbPlayer == null)
            {
                wpbPlayer = CreateWPBPlayer(iPlayer);
                if(wpbPlayer != null) InsertWPBPlayer(wpbPlayer);
            }
            wpbPlayer.Stats.Joins++;
        }
        void OnPlayerDisconnected(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null)
            {
                wpbPlayer.Stats.Leaves++;
                WPBPlayersLeftSteamIds.Add(wpbPlayer.SteamID);
            }
        }
        void OnPlayerRespawned(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Respawns++;
        }
        #endregion

        object OnUserChat(IPlayer iPlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(iPlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Chats++;
            return null;
        }
        void OnPlayerRecovered(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Recoveries++;
        }
        object OnPlayerWound(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Wounded++;
            return null;
        }

        #region Death, NPC Kills, Killed by NPC, Suicide
        object OnPlayerDeath(BasePlayer basePlayer, HitInfo hitInfo)
        {
            if (basePlayer == null) return null;
            BasePlayer attackingBasePlayer;
            if (basePlayer.IsNpc && hitInfo != null && hitInfo.Initiator != null)
            {
                attackingBasePlayer = hitInfo.Initiator as BasePlayer;
                if(attackingBasePlayer != null)
                {
                    WPBPlayer attackingWPBPlayer = GetWPBPlayer(attackingBasePlayer);
                    if(attackingWPBPlayer != null)
                    {
                        attackingWPBPlayer.Stats.NPCKills++;
                        return null;
                    }
                }
            }

            WPBPlayer deadPlayer = GetWPBPlayer(basePlayer);
            if (deadPlayer == null) return null;
            if (hitInfo != null && hitInfo.damageTypes != null && hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Suicide)
            {
                deadPlayer.Stats.Suicides++;
                return null;
            } else
            {
                deadPlayer.Stats.Deaths++;
            }

            if(hitInfo != null && hitInfo.Initiator != null)
            {
                attackingBasePlayer = hitInfo.Initiator as BasePlayer;
                if (attackingBasePlayer != null)
                {
                    if(attackingBasePlayer.IsNpc)
                    {
                        deadPlayer.Stats.KilledByNPC++;
                        return null;
                    }
                    WPBPlayer attackingWPBPlayer = GetWPBPlayer(attackingBasePlayer);
                    if (attackingWPBPlayer == null) return null;
                    attackingWPBPlayer.Stats.Kills++;
                }
            }
            return null;
        }
        #endregion

        object OnPlayerVoice(BasePlayer basePlayer, byte[] data)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.VoiceBytes++;
            return null;
        }
        void OnMeleeAttack(BasePlayer basePlayer, HitInfo info)
        {
            if (basePlayer != null && info != null)
            {
                if (info.HitEntity != null && info.HitEntity.ToPlayer() != null)
                {
                    WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
                    if (wpbPlayer != null) wpbPlayer.Stats.MeleeAttacks++;
                }
            }
        }
        void OnMapMarkerAdded(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.MapMarkers++;
        }
        object OnPlayerViolation(BasePlayer basePlayer, AntiHackType type, float amount)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.AntiHackViolations++;
            return null;
        }
        void OnNpcConversationEnded(NPCTalking npcTalking, BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.NPCSpeaks++;
        }
        private void OnLootEntityEnd(BasePlayer basePlayer, BaseEntity baseEntity)
        {
            if (basePlayer == null || !baseEntity.IsValid()) return;

            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer == null) return;

            var lootContainerName = baseEntity.GetType().Name.ToLower();
            if (lootContainerName == null || lootContainerName == "") return;
            switch (lootContainerName)
            {
                case "lootcontainer":
                    wpbPlayer.Stats.LootContainer++;
                    break;
                case "freeablelootcontainer":
                    wpbPlayer.Stats.LootContainerUnderWater++;
                    break;
                case "lockedbyentcrate":
                    wpbPlayer.Stats.LootBradHeli++;
                    break;
                case "hackablelockedcrate":
                    wpbPlayer.Stats.LootHackable++;
                    break;
                default:
                    PrintDebug($"[OnLootEntity] {GetMsg("player looted")} \"{lootContainerName}\" {GetMsg("which currently is not tracked")}");
                    break;
            }
        }
        
        #endregion

        #region Entity Hooks
        object OnEntityTakeDamage(BaseCombatEntity baseCombatEntity, HitInfo hitInfo)
        {
            if (hitInfo?.InitiatorPlayer == null || !hitInfo.isHeadshot) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(hitInfo.InitiatorPlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Headshots++;
            return null;
        }
        #endregion

        #region Item Hooks
        object OnItemPickup(Item item, BasePlayer basePlayer)
        {
            if (item == null || item.info == null || item.info.name == null || basePlayer == null) return null;
            if (item.info.name.EndsWith(".item")) WPBPlayerOnLoot(item, basePlayer);
            return null;
        }
        void OnItemCraftFinished(ItemCraftTask itemCraftTask, Item item)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(itemCraftTask.owner);
            if (wpbPlayer != null) wpbPlayer.Stats.CraftedItems++;
        }
        object OnItemRepair(BasePlayer basePlayer, Item item)
        {
            if (item == null) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.RepairedItems++;
            return null;
        }
        void OnItemResearch(ResearchTable researchTable, Item item, BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.ResearchedItems++;
        }
        #endregion

        #region Weapon Hooks
        void OnExplosiveThrown(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.ExplosivesThrown++;
        }
        object OnReloadWeapon(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Reloads++;
            return null;
        }
        void OnWeaponFired(BaseProjectile baseProjectile, BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.Shots++;
        }
        void OnRocketLaunched(BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.RocketsLaunched++;
        }
        #endregion

        #region Structure Hooks
        object OnHammerHit(BasePlayer basePlayer, HitInfo info)
        {
            if (info == null) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.HammerHits++;
            return null;
        }
        #endregion

        #region Resource Hooks
        object OnDispenserGather(ResourceDispenser resourceDispenser, BaseEntity baseEntity, Item item)
        {
            if (baseEntity.ToPlayer() == null) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(baseEntity.ToPlayer());
            if (wpbPlayer == null) return null;
            if (item == null || item.info == null || item.info.name == null) return null;
            if (item.info.name.EndsWith(".item")) WPBPlayerOnLoot(item, baseEntity.ToPlayer());
            return null;
        }
        object OnDispenserBonus(ResourceDispenser resourceDispenser, BaseEntity baseEntity, Item item)
        {
            if (!baseEntity.ToPlayer()) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(baseEntity.ToPlayer());
            if (wpbPlayer == null) return null;
            if (item == null || item.info == null || item.info.name == null) return null;
            if (item.info.name.EndsWith(".item")) WPBPlayerOnLoot(item, baseEntity.ToPlayer());
            return null;
        }
        object OnCollectiblePickup(Item item, BasePlayer basePlayer)
        {
            if (item == null || item.info == null || basePlayer == null) return null;
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer == null) return null;
            if (item.info.name.EndsWith(".item")) WPBPlayerOnLoot(item, basePlayer);
            return null;
        }
        object OnGrowableGather(GrowableEntity growableEntity, Item item, BasePlayer basePlayer)
        {
            WPBPlayer wpbPlayer = GetWPBPlayer(basePlayer);
            if (wpbPlayer != null) wpbPlayer.Stats.GrowablesGathered++;
            return null;
        }
        #endregion
        #endregion

        #region Permission Group
        /// <summary>
        /// Returns true if the group already exists or if successfully created. If creating group fails returns false.
        /// </summary>
        /// <returns>true or false</returns>
        private bool TryCreateReservedGroup()
        {
            if (permission.GroupExists(_reservedPlayerGroupName)) return true;
            return permission.CreateGroup(_reservedPlayerGroupName, "Hide my stats", 0);
        }

        /// <summary>
        /// Returns true if the reserved player group contains BasePlayer.UserIDString, if not returns false.
        /// </summary>
        /// <param name="basePlayer">BasePlayer object.</param>
        /// <returns>true or false.</returns>
        private bool WPBPlayerIsReserved(BasePlayer basePlayer)
        {
            return permission.UserHasGroup(basePlayer.UserIDString, _reservedPlayerGroupName);
        }

        /// <summary>
        /// Returns true if the reserved player group contains IPlayer.UserIDString, if not returns false.
        /// </summary>
        /// <param name="iPlayer">IPlayer object.</param>
        /// <returns>true or false.</returns>
        private bool WPBPlayerIsReserved(IPlayer iPlayer)
        {
            return permission.UserHasGroup(iPlayer.Id, _reservedPlayerGroupName);
        }
        #endregion

        #region Classes

        #region Configuration
        private class Configuration
        {
            [JsonProperty(PropertyName = "External_IP")]
            public string External_IP = "PASTE_EXTERNAL_IP_HERE";
            [JsonProperty(PropertyName = "Wordpress_Site_URL")]
            public string Wordpress_Site_URL = "PASTE_WORDPRESS_SITE_URL_HERE";
            [JsonProperty(PropertyName = "Wordpress_Secret")]
            public string Wordpress_Secret = "PASTE_WPBRIDGE_UNIQUE_SECRET_HERE";
            [JsonProperty(PropertyName = "Player_Data_Update_Interval")]
            public int UpdateInterval = 30;
            [JsonProperty(PropertyName = "Print_Debug_To_Console")]
            public bool Debug = true;
        }
        #endregion

        #region Server
        internal class WPBServer
        {
            public WPBServer(string ip, int port, string level, string identity, int seed, int worldsize, int maxPlayers, string hostName, string description)
            {
                IP = ip;
                Port = port;
                Level = level;
                Identity = identity;
                Seed = seed;
                WorldSize = worldsize;
                MaxPlayers = maxPlayers;
                HostName = hostName;
                Description = description;
                PlayerCount = BasePlayer.activePlayerList.Count;
            }

            public void UpdatePlayerCount()
            {
                PlayerCount = BasePlayer.activePlayerList.Count;
            }

            public string IP { get; set; }
            public int Port { get; set; }
            public string Level { get; set; }
            public string Identity { get; set; }
            public int Seed { get; set; }
            public int WorldSize { get; set; }
            public int MaxPlayers { get; set; }
            public string HostName { get; set; }
            public string Description { get; set; }
            public int PlayerCount { get; set; }
        }
        #endregion

        #region Loot
        internal class LootItem
        {
            public string Name;
            public int Amount;

            public LootItem(string name, int amount)
            {
                Name = name;
                Amount = amount;
            }
        }
        #endregion

        #region Player
        internal class WPBPlayer
        {
            private string _steamID;
            private string _displayName;
            private Vector3 _position;
            private WPBPlayerStats _stats;
            private List<LootItem> _lootedItems;

            public string SteamID { get { return this._steamID; } }
            public string DisplayName { get { return this._displayName; } }
            public Vector3 Position { get { return this._position; } }
            public WPBPlayerStats Stats { get { return this._stats; } set { _stats = value; } }
            public List<LootItem> LootedItems { get { return _lootedItems; } set { _lootedItems = value; } }

            public WPBPlayer(string steamID, string displayName)
            {
                this._steamID = steamID;
                this._displayName = displayName;
                this._position = new Vector3();
                this._stats = new WPBPlayerStats();
                this._lootedItems = new List<LootItem>();
            }
            public WPBPlayer(IPlayer iPlayer)
            {
                this._steamID = iPlayer.Id;
                this._displayName = iPlayer.Name;
                this._position = Vector3.zero;
                this._stats = new WPBPlayerStats();
                this._lootedItems = new List<LootItem>();
            }
            public WPBPlayer(BasePlayer p)
            {
                this._steamID = p.UserIDString;
                this._displayName = p.displayName;
                this._position = p.GetNetworkPosition();
                this._stats = new WPBPlayerStats();
                this._lootedItems = new List<LootItem>();
            }

            internal void SetPosition(int x, int y, int z)
            {
                this._position = new Vector3(x, y, z);
            }   
            internal void SetPosition(Vector3 vector3)
            {
                this._position = vector3;
            }

            internal void ClearStats()
            {
                this.Stats.Clear();
            }
            internal void ClearLoot()
            {
                this.LootedItems.Clear();
            }
        }
        internal class WPBPlayerStats
        {
            public int Joins { get; internal set; }
            public int Leaves { get; internal set; }
            public int Deaths { get; internal set; }
            public int Suicides { get; internal set; }
            public int Kills { get; internal set; }
            public int Headshots { get; internal set; }
            public int Wounded { get; internal set; }
            public int Recoveries { get; internal set; }
            public int CraftedItems { get; internal set; }
            public int RepairedItems { get; internal set; }
            public int ExplosivesThrown { get; internal set; }
            public int VoiceBytes { get; internal set; }
            public int HammerHits { get; internal set; }
            public int Reloads { get; internal set; }
            public int Shots { get; internal set; }
            public int CollectiblesPickedUp { get; internal set; }
            public int GrowablesGathered { get; internal set; }
            public int Chats { get; internal set; }
            public int NPCKills { get; internal set; }
            public int MeleeAttacks { get; internal set; }
            public int MapMarkers { get; internal set; }
            public int Respawns { get; internal set; }
            public int RocketsLaunched { get; internal set; }
            public int AntiHackViolations { get; internal set; }
            public int NPCSpeaks { get; internal set; }
            public int ResearchedItems { get; internal set; }
            public int KilledByNPC { get; internal set; }
            public int LootContainer { get; internal set; }
            public int LootBradHeli { get; internal set; }
            public int LootHackable { get; internal set; }
            public int LootContainerUnderWater { get; internal set; }

            public void Clear()
            {
                Joins = 0;
                Leaves = 0;
                Deaths = 0;
                Suicides = 0;
                Kills = 0;
                Headshots = 0;
                Wounded = 0;
                Recoveries = 0;
                CraftedItems = 0;
                RepairedItems = 0;
                ExplosivesThrown = 0;
                VoiceBytes = 0;
                HammerHits = 0;
                Reloads = 0;
                Shots = 0;
                CollectiblesPickedUp = 0;
                GrowablesGathered = 0;
                Chats = 0;
                NPCKills = 0;
                MeleeAttacks = 0;
                MapMarkers = 0;
                Respawns = 0;
                RocketsLaunched = 0;
                AntiHackViolations = 0;
                NPCSpeaks = 0;
                ResearchedItems = 0;
                KilledByNPC = 0;
                LootContainer = 0;
                LootBradHeli = 0;
                LootHackable = 0;
                LootContainerUnderWater = 0;
            }
        }
        #endregion

        #region Web Requests
        public class WebRequester
        {
            static Dictionary<string, string> _headers = new Dictionary<string, string>()
            {
                { "Content-Type", "application/json" }
            };
            public void Post(string url, object data, Action<int,string> response)
            {
                string serializedRequest;
                if (data == null)
                {
                    serializedRequest = "";
                } else
                {
                    serializedRequest = JsonConvert.SerializeObject(data);
                }
                _instance.webrequest.Enqueue(url,serializedRequest, (responseCode, responseString) => {
                    response(responseCode, responseString);
                    return;
                }, _instance, Core.Libraries.RequestMethod.POST,_headers);
            }

            internal void Get(string url, object data, Action<int, string> response)
            {
                string serializedRequest;
                if (data == null)
                {
                    serializedRequest = "";
                }
                else
                {
                    serializedRequest = JsonConvert.SerializeObject(data);
                }
                _instance.webrequest.Enqueue(url, serializedRequest, (responseCode, responseString) => {
                    response(responseCode, responseString);
                    return;
                }, _instance, Core.Libraries.RequestMethod.GET, _headers, 10000f);
            }
        }
        public class WordPressBridgeResponse
        {
            public string code;
            public string message;
            public object data;
        }
        public class WordPressBridgeRequest
        {
            public WordPressBridgeRequest(string secret)
            {
                Secret = secret;
                PlayerData = new List<WPBPlayer>();
            }
            public void SetPlayerData(List<WPBPlayer> playerData)
            {
                PlayerData = playerData;
            }
            public void SetServerData(WPBServer serverData)
            {
                ServerData = serverData;
            }

            string _secret;
            List<WPBPlayer> _playerData;
            WPBServer _serverData;

            [JsonProperty(PropertyName = "Secret")]
            string Secret { get { return _secret; } set { _secret = value; } }
            [JsonProperty(PropertyName = "PlayerData")]
            List<WPBPlayer> PlayerData { get { return _playerData; } set { _playerData = value; } }
            [JsonProperty(PropertyName = "ServerData")]
            WPBServer ServerData { get { return _serverData; } set { _serverData = value; } }

        }
        #endregion

        #region WordPress Specific
        class WordPressJson
        {
            public string name { get; set; }
            public string description { get; set; }
            public string url { get; set; }
            public string home { get; set; }
            public WordPressJsonRoutes routes { get; set; }
        }
        class WordPressJsonRoutes
        {
            [JsonProperty("/wpbridge")]
            public WordPressJsonRouteWPBridge Wpbridge { get; set; }
        }
        class WordPressJsonRouteWPBridge
        {
            public List<string> methods { get; set; }
        }
        #endregion

        #endregion

        #region Commands

        #region General commands

        [ChatCommand("wip.help")]
        void HelpCommand(BasePlayer basePlayer, string command, string[] args)
        {
            if (basePlayer == null) return;
            basePlayer.ChatMessage($"[WIP] " +
                $"{GetMsg("Available commands")}:\n\n" +
                $"/wip.reserve\n{GetMsg("Toggles share statistics")}\n{GetMsg("DEFAULT")}: {GetMsg("share statistics")}.\n\n" +
                $"/wip.isreserved\n{GetMsg("Check if you are sharing your statistics")}");
        }

        #endregion

        #region Debug commands

        [ConsoleCommand("wip.debug")]
        void ToggleDebug(ConsoleSystem.Arg arg)
        {
            BasePlayer basePlayer = arg.Player();
            if(basePlayer != null && basePlayer.IsAdmin)
            {
                _config.Debug = !_config.Debug;
                SaveConfig();
            } else
            {
                _config.Debug = !_config.Debug;
                SaveConfig();
            }
        }

        #endregion

        #region Reservation commands

        [ChatCommand("wip.isreserved")]
        void IsReserved(BasePlayer basePlayer, string command, string[] args)
        {
            string isReservedString = WPBPlayerIsReserved(basePlayer) ? GetMsg("not") + " " : "";
            basePlayer.ChatMessage($"[WIP] {GetMsg("You are currently")} {isReservedString}{GetMsg("sharing statistics")}.");
        }

        [ChatCommand("wip.reserve")]
        void ReserveCommand(BasePlayer basePlayer, string command, string[] args)
        {
            if (basePlayer == null) return;
            if (!WPBPlayerIsReserved(basePlayer))
            {
                var existingPlayer = GetWPBPlayer(basePlayer);
                if (existingPlayer != null) RemoveWPBPlayer(basePlayer);
                permission.AddUserGroup(basePlayer.UserIDString, _reservedPlayerGroupName);
                basePlayer.ChatMessage($"[WIP] {GetMsg("Your statistics are not shared")}");
            }
            else
            {
                var existingPlayer = GetWPBPlayer(basePlayer);
                permission.RemoveUserGroup(basePlayer.UserIDString, _reservedPlayerGroupName);
                if (existingPlayer == null) InsertWPBPlayer(new WPBPlayer(basePlayer));
                basePlayer.ChatMessage($"[WIP] {GetMsg("Your statistics are shared")}");
            }
        }

        #endregion

        #endregion

        #region Debug
        private void PrintDebug(string stringToPrint)
        {
            if (_config.Debug) PrintWarning($"[DEBUG] {stringToPrint}");
        }
        private void PrintDebug(int intToPrint)
        {
            if (_config.Debug) PrintWarning($"[DEBUG] {intToPrint.ToString()}");
        }
        private void PrintDebug(bool boolToPrint)
        {
            if (_config.Debug) PrintWarning($"[DEBUG] {boolToPrint.ToString()}");
        }
        private void PrintDebug(Type typeToPrint)
        {
            if (_config.Debug) PrintWarning($"[DEBUG] {typeToPrint.ToString()}");
        }
        #endregion

        #region Helper Methods
        private string GetMsg(string key) => lang.GetMessage(key, this);
        public static bool ValidHttpURL(string s)
        {
            Uri uriResult;
            return Uri.TryCreate(s, UriKind.Absolute, out uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }
        public bool ValidIPv4(string ipString)
        {
            if (String.IsNullOrWhiteSpace(ipString))
            {
                return false;
            }

            string[] splitValues = ipString.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            byte tempForParsing;

            return splitValues.All(r => byte.TryParse(r, out tempForParsing));
        }
        #endregion
    }
}
