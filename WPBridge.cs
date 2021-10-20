using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Rust;
using Newtonsoft.Json.Serialization;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("WordPress Bridge", "Murky", "1.1.124")]
    [Description("WordPress Bridge integrates Rust servers with Wordpress, making it possible to embed player and server statistics on your Wordpress site with shortcodes.")]
    internal class WPBridge : RustPlugin
    {

        #region VARIABLES

        //Config
        static Configuration _config = new Configuration();
        //Timer
        Timer dataTimer;
        //Stopwatch to measure the time it takes from a request is sent to a response is received
        System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
        //Player data
        static List<PlayerStats> PlayersData = new List<PlayerStats>();
        //Players that have disconnected
        static List<string> PlayersLeftSteamIds = new List<string>();
        //Group name for reserved players
        string ReservedPlayerGroupName = "wipreservedplayers";

        #endregion

        #region CONFIGURATION

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
                LogToFile("ErrorLog", $"[{DateTime.Now}] [LoadConfig] Configuration file contains an error. Using default configuration values.", this);
                PrintError("ERROR: " + "Your configuration file contains an error. Using default configuration values.");

                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(_config);

        KeyValuePair<bool, string> CheckConfig()
        {
            if (_config.External_IP == "" || _config.External_IP == "PASTE_EXTERNAL_IP_HERE") return new KeyValuePair<bool, string>(false, "External IP needs to be set.");
            if (_config.Wordpress_Site_URL == "" || _config.Wordpress_Site_URL == "PASTE_WORDPRESS_SITE_URL_HERE") return new KeyValuePair<bool, string>(false, "Wordpress Site Url needs to be set.");
            if (!ValidHttpURL(_config.Wordpress_Site_URL)) return new KeyValuePair<bool, string>(false, "Wordpress Site Url seems to be invalid.");
            if (!_config.Wordpress_Site_URL.EndsWith("/")) return new KeyValuePair<bool, string>(false, "Wordpress Site Url must end with a trailing slash. [http://www.your-wordpress-site.com/]");
            if (_config.Wordpress_Secret == "" || _config.Wordpress_Secret == "PASTE_WPBRIDGE_UNIQUE_SECRET_HERE") return new KeyValuePair<bool, string>(false, "Wordpress secret needs to be set.");
            if (_config.UpdateInterval < 5) return new KeyValuePair<bool, string>(false, "Update interval cannot be less than 5 seconds.");
            return new KeyValuePair<bool, string>(true, "Configuration validated.");
        }

        #endregion

        #region INITIALIZATION

        void WPBridgeInit()
        {
            if(!ReservedStatsGroupExists())
            {
                if(!CreateReservedStatsGroup())
                {
                    LogToFile("ErrorLog", $"[{DateTime.Now}] [WPBridgeInit] -> Couldn't create permission group \"{ReservedPlayerGroupName}\". Unloading plugin.", this);
                    PrintError($"Couldn't create permission group \"{ReservedPlayerGroupName}\". Unloading plugin.");
                    Interface.Oxide.UnloadPlugin("WPBridge");
                    return;
                }
            }
            var configCheck = CheckConfig();
            if (!configCheck.Key)
            {
                LogToFile("ErrorLog", $"[{DateTime.Now}] [WPBridgeInit] -> {configCheck.Value}. Unloading plugin.", this);
                PrintError(configCheck.Value);
                Interface.Oxide.UnloadPlugin("WPBridge");
                return;
            }
            PrintDebug(configCheck.Value);
            ValidateSecret();
        }

        #endregion

        #region WEBREQUESTS

        Dictionary<string, string> WPRequestHeaders = new Dictionary<string, string>()
        {
            {"Content-Type", "application/json" }
        };

        public class WPRequestRustServerInfo
        {
            public string Ip = _config.External_IP;
            public int Port = ConVar.Server.port;
            public string Level = ConVar.Server.level;
            public string Identity = ConVar.Server.identity;
            public int Seed = ConVar.Server.seed;
            public int WorldSize = ConVar.Server.worldsize;
            public int MaxPlayers = ConVar.Server.maxplayers;
            public string HostName = ConVar.Server.hostname;
            public string Description = ConVar.Server.description;
            public int PlayerCount = BasePlayer.activePlayerList.Count;
        }

        public class WPRequest
        {
            public string Secret = _config.Wordpress_Secret;
            public List<PlayerStats> PlayersData;
            public WPRequestRustServerInfo ServerInfo;
            public WPRequest(List<PlayerStats> _playersData)
            {
                ServerInfo = new WPRequestRustServerInfo();
                PlayersData = _playersData;
            }
            
        }
            
        public class WPResponseData
        {
            public int status;
        }

        public class WPResponse
        {
            public string code;
            public string message;
            public WPResponseData data;
        }

        void ValidateSecret()
        {
            var serializedRequest = JsonConvert.SerializeObject(new WPRequest(PlayersData));
            webrequest.Enqueue($"{_config.Wordpress_Site_URL}index.php/wp-json/wpbridge/secret", serializedRequest, (responseCode, responseString) => {
                WPResponse wpResponse = null;
                try
                {
                    wpResponse = JsonConvert.DeserializeObject<WPResponse>(responseString);

                }
                catch (Exception ex)
                {
                    PrintDebug($"[ValidateSecret] WordPress response error. See oxide/logs/WPBridge for more.");
                    LogToFile("ErrorLog",$"[{DateTime.Now}] [ValidateSecret] -> {responseString} ",this);

                }
                if(wpResponse == null)
                {
                    PrintError($"[ValidateSecret] Trying again in 5 seconds");
                    timer.Once(5f, ValidateSecret);
                    return;
                }
                if (wpResponse.data.status != 200)
                {
                    PrintWarning($"[{wpResponse.data.status}] -> {wpResponse.message}");
                    LogToFile("ErrorLog", $"[{DateTime.Now}] [SendPlayerData] -> [{wpResponse.data.status}] {wpResponse.message}", this);
                    return;
                }
                PrintDebug($"[200] => Secret validated. Server responded: {wpResponse.message}");
                dataTimer = timer.Every(_config.UpdateInterval, SendPlayerData);

            }, this, Core.Libraries.RequestMethod.POST, WPRequestHeaders);
        }

        private void SendPlayerData()
        {
            var request = new WPRequest(PlayersData);
            var serializedRequest = JsonConvert.SerializeObject(request);
            
            if(PlayersLeftSteamIds.Count == 0 && PlayersData.Count == 0)
            {
                PrintDebug($"No players and no leaves to report. Pinging WordPress only for Server statistics");
                stopWatch.Start();
                webrequest.Enqueue($"{_config.Wordpress_Site_URL}index.php/wp-json/wpbridge/secret", serializedRequest, (responseCode, responseString) => {
                    WPResponse wpResponse = null;
                    try
                    {
                        wpResponse = JsonConvert.DeserializeObject<WPResponse>(responseString);
                    }
                    catch (Exception ex)
                    {
                        PrintDebug($"[SendPlayerData] WordPress response error. See oxide/logs/WPBridge for more.");
                        LogToFile("ErrorLog", $"[{DateTime.Now}] [SendPlayerData] -> {responseString} ", this);

                    }
                    if (wpResponse == null)
                    {
                        return;
                    }
                    if (wpResponse.data.status != 200)
                    {
                        PrintWarning($"[{wpResponse.data.status}] -> {wpResponse.message}");
                        LogToFile("ErrorLog", $"[{DateTime.Now}] [SendPlayerData] -> [{wpResponse.data.status}] {wpResponse.message}", this);
                        return;
                    }
                    stopWatch.Stop();
                    long elapsedSeconds = stopWatch.ElapsedMilliseconds;
                    PrintDebug($"[WordPressResponse] [200] The exchange took {stopWatch.ElapsedMilliseconds} milliseconds. ResponseMessage => Server stats stored.");
                    stopWatch.Reset();
                }, this, Core.Libraries.RequestMethod.POST, WPRequestHeaders);
                return;
            }

            float requestSize = (float)(serializedRequest.Length * 2) / 1024;
            string payloadSizeFormatted = requestSize.ToString("0.00");
            PrintDebug($"Sending {payloadSizeFormatted}kB of statistics for {PlayersData.Count} players. "); // C# uses Unicode which is 2 bytes per character
            stopWatch.Start();
            webrequest.Enqueue($"{_config.Wordpress_Site_URL}index.php/wp-json/wpbridge/player-stats", serializedRequest, (responseCode, responseString) =>
            {
                if (PlayersLeftSteamIds.Count > 0)
                {
                    PlayersData.RemoveAll(p => { return PlayersLeftSteamIds.Contains(p.SteamId); });
                    PlayersLeftSteamIds.Clear();
                }

                WPResponse wpResponse = null;
                try
                {
                    wpResponse = JsonConvert.DeserializeObject<WPResponse>(responseString);

                }
                catch (Exception ex)
                {
                    PrintDebug($"[SendPlayerData] WordPress response error. See oxide/logs/WPBridge for more.");
                    LogToFile("ErrorLog", $"[{DateTime.Now}] [SendPlayerData] -> {responseString}", this);
                }
                if (wpResponse == null)
                {
                    return;
                }
                if (wpResponse.data.status != 200)
                {
                    PrintWarning($"[{wpResponse.data.status}] -> {wpResponse.message}");
                    LogToFile("ErrorLog", $"[{DateTime.Now}] [SendPlayerData] -> [{wpResponse.data.status}] {wpResponse.message}", this);
                    return;
                }
                stopWatch.Stop();
                long elapsedSeconds = stopWatch.ElapsedMilliseconds;
                PrintDebug($"[WordPressResponse] [200] The exchange took {stopWatch.ElapsedMilliseconds} milliseconds. ResponseMessage => {wpResponse.message}");
                stopWatch.Reset();

                ClearPlayerStats();

            }, this, Core.Libraries.RequestMethod.POST, WPRequestHeaders);
        }

        #endregion

        #region RUST HOOKS

        // Plugin
        void Unload()
        {
           if (dataTimer != null) dataTimer.Destroy();
        }

        // Server
        void OnServerInitialized()
        {
            SaveActivePlayersData();
            CheckActivePlayersAreReserved();
        }
        void Init()
        {
            WPBridgeInit();
        }

        // Player
        object OnUserChat(IPlayer _player)
        {
            var existingPlayer = FindExistingPlayer(_player.Id);
            if (existingPlayer != null)
            {
                existingPlayer.Chats++;
                PrintDebug($"Player: {existingPlayer.DisplayName} have sent {existingPlayer.Chats} chat messages.");
            }
            return null;
        }
        void OnPlayerRecovered(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Recoveries++;
                PrintDebug($"Player: {player.DisplayName} have been recovered {player.Recoveries} times.");
            }
        }
        object OnPlayerWound(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Wounded++;
                PrintDebug($"Player: {player.DisplayName} have been wounded {player.Wounded} times.");
            }
            return null;
        }
        void OnUserConnected(IPlayer _player)
        {
            if (PlayerIsReserved(_player.Id)) return; // Player is reserved and statistics should not be shared
            var player = FindExistingPlayer(_player.Id);
            if (player == null)
            {
                player = InsertPlayer(_player.Id, _player.Name);
                PrintDebug($"Player inserted: [{player.SteamId}] -> [{player.DisplayName}]");
            } else
            {
                PrintDebug($"Player exists: [{player.SteamId}] -> [{player.DisplayName}]");
            }
            player.Joins++;
            PrintDebug($"Player: {player.DisplayName} have joined {player.Joins} times.");
        }
        void OnPlayerDisconnected(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Leaves++;
                PrintDebug($"Player: {player.DisplayName} have left {player.Leaves} times.");
                PlayersLeftSteamIds.Add(player.SteamId);
            }
        }



        object OnLootNetworkUpdate(PlayerLoot loot)
        {
            PrintDebug("OnLootNetworkUpdate works");
            return null;
        }

        //Death, NPC Kills, Killed by NPC, Suicide
        object OnPlayerDeath(BasePlayer victim, HitInfo hitInfo)
        {
            if (victim == null) return null;
            if (hitInfo == null) return null; 
            if (hitInfo.Initiator == null) return null;
            
            var attacker = hitInfo.Initiator as BasePlayer;
            if (victim == null || attacker == null) return null;
            var victimPlayer = FindExistingPlayer(victim.UserIDString);

            if(attacker.IsNpc && victimPlayer != null)
            {
                victimPlayer.KilledByNPC++;
                victimPlayer.Deaths++;
                PrintDebug($"Player: {victimPlayer.DisplayName} have been killed by NPC's {victimPlayer.KilledByNPC} times.");
                return null;
            }

            var attackingPlayer = FindExistingPlayer(attacker.UserIDString);
            if (attackingPlayer == null) return null;
            
            if(victim.IsNpc)
            {
                attackingPlayer.NPCKills++;
                PrintDebug($"Player: {attackingPlayer.DisplayName} have killed {attackingPlayer.NPCKills} npc's.");
                return null;
            }
            
            if (victimPlayer == null) return null;

            if(hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Suicide)
            {
                victimPlayer.Suicides++;
                PrintDebug($"Player: {victimPlayer.DisplayName} have suicided {victimPlayer.Suicides} times.");
                return null;
            }

            victimPlayer.Deaths++;
            PrintDebug($"Player: {victimPlayer.DisplayName} have died {attackingPlayer.Deaths} times.");
            attackingPlayer.Kills++;
            PrintDebug($"Player: {victimPlayer.DisplayName} have killed {attackingPlayer.Kills} times.");
            return null;
        }

        /*void OnPlayerDeath(BasePlayer _player, HitInfo info)
        {
            if(_player.IsNpc && info != null)
            {
                var attacker = info.InitiatorPlayer;
                if(attacker != null && !attacker.IsNpc)
                {
                    var attackingPlayer = FindExistingPlayer(attacker.UserIDString);
                    if(attackingPlayer != null)
                    {
                        attackingPlayer.NPCKills++;
                        PrintDebug($"Player: {attackingPlayer.DisplayName} have killed {attackingPlayer.NPCKills} npc.");
                        return;
                    }

                }
            }
            if (_player == null) return;
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                if (info == null && _player != null && !_player.IsNpc)
                {
                    player.Deaths++;
                    PrintDebug($"Player: {player.DisplayName} have died {player.Deaths} times.");
                    return;
                }

                if (info == null || _player.IsNpc) return;

                if (info.damageTypes.GetMajorityDamageType() == DamageType.Suicide)
                {
                    player.Suicides++;
                    PrintDebug($"Player: {player.DisplayName} have suicided {player.Suicides} times.");
                    return;
                } else
                {
                    player.Deaths++;
                    PrintDebug($"Player: {player.DisplayName} have died {player.Deaths} times.");

                    var attacker = info.InitiatorPlayer;
                    if (attacker == null || attacker.IsNpc) return;

                    var attackingPlayer = FindExistingPlayer(info.InitiatorPlayer.UserIDString);
                    if (attackingPlayer != null)
                    {
                        attackingPlayer.Kills++;
                        PrintDebug($"Player: {player.DisplayName} have killed {player.Kills} times.");
                    }
                }
            }
        }*/


        object OnPlayerVoice(BasePlayer _player, byte[] data)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.VoiceBytes++;
                PrintDebug($"Player: {player.DisplayName} have generated {player.VoiceBytes} VoiceBytes.");
            }
            return null;
        }
        void OnMeleeAttack(BasePlayer _player, HitInfo info)
        {
            if(_player != null && info != null)
            {
                if(info.HitEntity != null && info.HitEntity.ToPlayer() != null)
                {
                    var infoMeleedPlayer = info.HitEntity.ToPlayer();
                    if(infoMeleedPlayer != null && !infoMeleedPlayer.IsNpc)
                    {
                        var meleedplayer = FindExistingPlayer(infoMeleedPlayer.UserIDString);
                        if(meleedplayer != null)
                        {
                            var player = FindExistingPlayer(_player.UserIDString);
                            player.MeleeAttacks++;
                        }
                    }
                }
            }
        }
        void OnMapMarkerAdded(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.MapMarkers++;
                PrintDebug($"Player: {player.DisplayName} have put {player.MapMarkers} map markers.");
            }
        }
        void OnPlayerRespawned(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Respawns++;
                PrintDebug($"Player: {player.DisplayName} has respawned {player.Respawns} times.");
            }
        }
        void OnPlayerViolation(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.AntiHackViolations++;
                PrintDebug($"Player: {player.DisplayName} has {player.AntiHackViolations} antihack violations triggered.");
            }
        }
        void OnNpcConversationEnded(NPCTalking npcTalking, BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.NPCSpeaks++;
                PrintDebug($"Player: {player.DisplayName} has spoken to an NPC {player.NPCSpeaks} times.");
            }
        }

        void OnPlayerConnected(BasePlayer _player)
        {
            if (_player == null) return;
            if (_player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(2, () => OnPlayerConnected(_player));
                return;
            }
            //Tell the player that stats are stored unless command is used
            string isReservedString;
            if (PlayerIsReserved(_player.UserIDString))
            {
                isReservedString = "not ";
                var existingPlayer = FindExistingPlayer(_player.UserIDString);
                if (existingPlayer != null) RemovePlayer(_player.UserIDString); 
            } else
            {
                isReservedString = "";
            }
            _player.ChatMessage($"[WIP] Type /wip.help to see a list of commands");
            _player.ChatMessage($"[WIP] You are currently {isReservedString}sharing your statistics. You can always toggle this on/off using chatcommand /wip.reserve");
        }

        // Entity
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer == null || !info.isHeadshot) return null;
            var player = FindExistingPlayer(info.InitiatorPlayer.UserIDString);
            if (player != null)
            {
                player.Headshots++;
                PrintDebug($"Player: {player.DisplayName} have {player.Headshots} number of headshots.");
            }
            return null;
        }

        // Item
        void OnItemCraftFinished(ItemCraftTask task, Item item)
        {
            var player = FindExistingPlayer(task.owner.UserIDString);
            if (player != null)
            {
                player.CraftedItems++;
                PrintDebug($"Player: {player.DisplayName} have crafted items {player.CraftedItems} times.");
            }
        }
        object OnItemRepair(BasePlayer _player, Item item)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.RepairedItems++;
                PrintDebug($"Player: {player.DisplayName} have repaired items {player.RepairedItems} times.");
            }
            return null;
        }
        void OnItemResearch(ResearchTable table, Item targetItem, BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.ResearchedItems++;
                PrintDebug($"Player: {player.DisplayName} have researched items {player.ResearchedItems} times.");
            }
        }

        // Weapon
        void OnExplosiveThrown(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.ExplosivesThrown++;
                PrintDebug($"Player: {player.DisplayName} have thrown explosives {player.ExplosivesThrown} times.");
            }
        }
        object OnReloadWeapon(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Reloads++;
                PrintDebug($"Player: {player.DisplayName} have reloaded a weapon {player.Reloads} times.");
            }
            return null;
        }
        void OnWeaponFired(BaseProjectile projectile, BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.Shots++;
                PrintDebug($"Player: {player.DisplayName} have shot {player.Shots} times.");
            }
        }
        void OnRocketLaunched(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.RocketsLaunched++;
                PrintDebug($"Player: {player.DisplayName} have fired {player.RocketsLaunched} rockets.");
            }
        }

        // Structure
        object OnHammerHit(BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.HammerHits++;
                PrintDebug($"Player: {player.DisplayName} have used his hammer {player.HammerHits} times.");
            }
            return null;
        }

        // Resource
        private void OnLootEntityEnd(BasePlayer _player, BaseEntity entity)
        {
            
            if (_player == null || !entity.IsValid()) return;

            var player = FindExistingPlayer(_player.UserIDString);
            if (player == null) return;

            var loot = entity.GetType().Name.ToLower();
            if (loot == null || loot == "") return;
            switch (loot)   
            {
                case "lootcontainer":
                    player.LootContainer++;
                    PrintDebug($"[OnLootEntity] Player {player.DisplayName} looted crate.");
                    break;
                case "freeablelootcontainer":
                    player.LootContainerUnderWater++;
                    PrintDebug($"[OnLootEntity] Player {player.DisplayName} looted Underwater crate.");
                    break;
                case "lockedbyentcrate":
                    player.LootBradHeli++;
                    PrintDebug($"[OnLootEntity] Player {player.DisplayName} looted Brad/Heli crate.");
                    break;
                case "hackablelockedcrate":
                    player.LootHackable++;
                    PrintDebug($"[OnLootEntity] Player {player.DisplayName} looted Hackable crate.");
                    break;
                default:
                    PrintDebug($"[OnLootEntity] Player looted \"{loot}\" which currently is not tracked.");
                    break;
            }

        }

        object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item _item)
        {
            if (!entity.ToPlayer()) return null;
            var player = FindExistingPlayer(entity.ToPlayer().UserIDString);
            if (player == null) return null;
            if (_item == null || _item.info == null || _item.info.name == null) return null;
            if (_item.info.name.EndsWith(".item"))
            {
                WPBridgeOnLoot(_item, entity.ToPlayer());
            }
            return null;
        }

        object OnDispenserBonus(ResourceDispenser dispenser, BaseEntity _entity, Item _item)
        {
            if (!_entity.ToPlayer()) return null;
            var player = FindExistingPlayer(_entity.ToPlayer().UserIDString);
            if (player == null) return null;
            if (_item == null || _item.info == null || _item.info.name == null) return null;
            if (_item.info.name.EndsWith(".item"))
            {
                WPBridgeOnLoot(_item,_entity.ToPlayer());
            }
            return null;
        }

        void WPBridgeOnLoot(Item _item, BasePlayer _player)
        {
            if (_item == null || _item.info == null || _player == null) return;
            var player = FindExistingPlayer(_player.UserIDString);
            if (player == null) return;
            if (_item.info.name.EndsWith(".item"))
            {
                int itemNameLength = _item.info.name.Length;
                var itemName = _item.info.name.Replace(".item", "");
                if (itemName.Length < itemNameLength)
                {
                    var itemAmount = _item.amount;
                    PrintDebug($" Player {player.DisplayName} looted {itemAmount} {itemName} : [{_item.info.name}].");
                    if (player.LootedItems.Count > 0)
                    {
                        var lootItem = player.LootedItems.Where(x => x.Name == itemName).FirstOrDefault();
                        if (lootItem != null)
                        {
                            lootItem.Amount += itemAmount;
                        }
                        else
                        {
                            player.LootedItems.Add(new LootItem(itemName, itemAmount));
                        }
                    }
                    else
                    {
                        player.LootedItems.Add(new LootItem(itemName, itemAmount));
                    }
                }
            }
        }

        object OnCollectiblePickup(Item _item, BasePlayer _player)
        {
            if (_item == null || _item.info == null || _player == null) return null;
            var player = FindExistingPlayer(_player.UserIDString);
            if (player == null) return null;
            if(_item.info.name.EndsWith(".item"))
            {
                WPBridgeOnLoot(_item, _player);
            }
            player.CollectiblesPickedUp++;
            return null;
        }

        void OnGrowableGather(GrowableEntity plant, Item item, BasePlayer _player)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null)
            {
                player.GrowablesGathered++;
                PrintDebug($"Player: {player.DisplayName} have gathered growables {player.GrowablesGathered} times.");
            }
        }

        #endregion

        #region COMMANDS

        [ChatCommand("wip.isreserved")]
        void IsReserved(BasePlayer _player, string command, string[] args)
        {
            string isReservedString = PlayerIsReserved(_player.UserIDString) ? "not " : "";
            _player.ChatMessage($"[WIP] You are currently {isReservedString}sharing statistics.");
        }

        [ChatCommand("wip.reserve")]
        void ReserveCommand(BasePlayer _player, string command, string[] args)
        {
            if (_player == null) return;
            if(!PlayerIsReserved(_player.UserIDString))
            {
                var existingPlayer = FindExistingPlayer(_player.UserIDString);
                if (existingPlayer != null) RemovePlayer(_player.UserIDString);
                permission.AddUserGroup(_player.UserIDString, ReservedPlayerGroupName);
                _player.ChatMessage("[WIP] Reserved. Your statistics are not shared.");
            } else
            {
                var existingPlayer = FindExistingPlayer(_player.UserIDString);
                if (existingPlayer == null) InsertPlayer(_player.UserIDString,_player.displayName);
                permission.RemoveUserGroup(_player.UserIDString, ReservedPlayerGroupName);
                _player.ChatMessage("[WIP] Reservation removed. Your statistics are shared.");
            }
        }

        [ChatCommand("wip.help")]
        void HelpCommand(BasePlayer _player, string command, string[] args)
        {
            if (_player == null) return;
            _player.ChatMessage($"[WIP] " +
                $"Available commands:\n\n" +
                $"/wip.reserve\nToggles share/not share statistics.\nDEFAULT: share statistics\n\n" +
                $"/wip.isreserved\nCheck if you are sharing or not sharing your statistics.");
        }

        [ChatCommand("wip.stats")]
        void StatsCommand(BasePlayer _player, string command, string[] args)
        {
            var player = FindExistingPlayer(_player.UserIDString);
            if (player != null) _player.ChatMessage($"[WIP] {player.ToString()}");
        }
        
        #endregion

        #region DEBUG
        private void PrintDebug(string stringToPrint)
        {
            if (_config.Debug) PrintWarning($"[DEBUG] {stringToPrint}");
        }
        #endregion

        #region PERMISSION GROUP

        void CheckActivePlayersAreReserved()
        {
            if(PlayersData != null && PlayersData.Count > 0)
            {
                PlayersData.ForEach(p => {
                    if (PlayerIsReserved(p.SteamId)) RemovePlayer(p.SteamId);
                });
            }
        }

        bool PlayerIsReserved(string userIdString)
        {
            return permission.UserHasGroup(userIdString, ReservedPlayerGroupName);
        }

        bool ReservedStatsGroupExists()
        {
            return permission.GroupExists(ReservedPlayerGroupName);
        }

        bool CreateReservedStatsGroup()
        {
            PrintDebug($"Creating permission group \"{ReservedPlayerGroupName}\".");
            return permission.CreateGroup(ReservedPlayerGroupName, "Hide my stats", 0);
        }

        #endregion

        #region LOOT DATA
        
        

        public class LootItem
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

        #region PLAYER DATA

        private void RemovePlayer(string steamId)
        {
            var player = FindExistingPlayer(steamId);
            if (player != null) PlayersData.Remove(player);
        }

        private PlayerStats InsertPlayer(string steamId, string displayName)
        {
            var player = new PlayerStats(steamId, displayName);
            PlayersData.Add(player);
            return player;
        }

        PlayerStats FindExistingPlayer(string steamId)
        {
            return PlayersData.Find(p => p.SteamId.ToString() == steamId);
        }


        public class PlayerStats
        {
            public PlayerStats(string steamId, string displayName)
            {
                SteamId = steamId;
                DisplayName = displayName;
                LootedItems = new List<LootItem>();
            }

            public string SteamId { get; internal set; }
            public string DisplayName { get; internal set; }
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

            public List<LootItem> LootedItems;

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
                LootedItems = new List<LootItem>();
            }

            
        }

        private void SaveActivePlayersData()
        {
            var activePlayers = BasePlayer.activePlayerList;
            if (activePlayers.Count > 0)
            {
                if (PlayersData == null) PlayersData = new List<PlayerStats>();
                foreach (var activePlayer in activePlayers)
                {
                    if (PlayerIsReserved(activePlayer.UserIDString))
                    {
                        PrintDebug($"Player {activePlayer.displayName} ({activePlayer.UserIDString}) -> reserved. Stats not saved.");
                        continue;
                    }
                    var existingPlayer = PlayersData.Find(p => p.SteamId.ToString() == activePlayer.UserIDString);
                    if (existingPlayer == null) PlayersData.Add(new PlayerStats(activePlayer.UserIDString, activePlayer.displayName));
                }
            }
        }

        private void ClearPlayerStats()
        {
            if (PlayersData.Count > 0)
            {
                foreach (var player in PlayersData)
                {
                    player.Clear();
                }
            }
        }

        #endregion

        #region HELPER METHODS

        public static bool ValidHttpURL(string s)
        {
            Uri uriResult;
            return Uri.TryCreate(s, UriKind.Absolute, out uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        #endregion

    }
}
