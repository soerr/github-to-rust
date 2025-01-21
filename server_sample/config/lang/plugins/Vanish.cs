using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Facepunch;
using Rust.Ai;

namespace Oxide.Plugins
{
    [Info("Vanish", "Whispers88", "1.9.0")]
    [Description("Allows players with permission to become invisible")]
    public class Vanish : CovalencePlugin
    {
        private static Vanish? vanish;
        private readonly List<ulong> _hiddenPlayers = new List<ulong>();
        private List<ulong> _hiddenOffline = new List<ulong>();
        private static List<string>? _registeredhooks;
        private static int PlayerLayermask;
        private static DamageTypeList? _EmptyDmgList;
        CuiElementContainer? cachedVanishUI = null;

        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("NoClip on Vanish (runs noclip command)")]
            public bool NoClipOnVanish = true;

            [JsonProperty("Inventory view cmd", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] InvViewCMD = new[] { "inv", "invspy" };

            [JsonProperty("Use OnEntityTakeDamage hook (Set to true to enable use of vanish.damage perm. Set to false for better performance)")]
            public bool UseOnEntityTakeDamage = false;

            [JsonProperty("Use CanUseLockedEntity hook (Allows vanished players with the perm vanish.unlock to bypass locks. Set to false for better performance)")]
            public bool UseCanUseLockedEntity = true;

            [JsonProperty("Automatically vanish players (with the vanish.use perm) on player connect")]
            public bool EnforceOnConnect = true;

            [JsonProperty("Automatically vanish players (with the vanish.use perm) on player disconnect")]
            public bool EnforceOnDisconnect = true;

            [JsonProperty("Keep a vanished player hidden on disconnect")]
            public bool HideOnDisconnect = true;

            [JsonProperty("Teleport a vanished player under the map on disconnect")]
            public bool UnderWorldOnDisconnect = true;

            [JsonProperty("Teleport a vanished player above the map on connect")]
            public bool AboveWorldOnConnect = true;

            [JsonProperty("Bypass violation checks for vanished players")]
            public bool BypassViolation = true;

            [JsonProperty("Turn off fly hack detection for players in vanish")]
            public bool AntiHack = true;

            [JsonProperty("Disable metabolism in vanish")]
            public bool Metabolism = true;

            [JsonProperty("Reset hydration and health on un-vanishing (resets to pre-vanished state)")]
            public bool MetabolismReset = true;

            [JsonProperty("Enable vanishing and reappearing sound effects")]
            public bool EnableSound = true;

            [JsonProperty("Make sound effects public")]
            public bool PublicSound = false;

            [JsonProperty("Enable chat notifications")]
            public bool EnableNotifications = true;

            [JsonProperty("Sound effect to use when vanishing")]
            public string VanishSoundEffect = "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab";

            [JsonProperty("Sound effect to use when reappearing")]
            public string ReappearSoundEffect = "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab";

            [JsonProperty("Enable GUI")]
            public bool EnableGUI = true;

            [JsonProperty("Icon URL (.png or .jpg)")]
            public string ImageUrlIcon = "https://i.ibb.co/3rZzftx/yL9HNRy.png";

            [JsonProperty("Image Color")]
            public string ImageColor = "1 1 1 0.3";

            [JsonProperty("Image AnchorMin")]
            public string ImageAnchorMin = "0.175 0.017";

            [JsonProperty("Image AnchorMax")]
            public string ImageAnchorMax = "0.22 0.08";

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (config.ImageUrlIcon == "http://i.imgur.com/Gr5G3YI.png")
                {
                    config.ImageUrlIcon = "https://i.imgur.com/yL9HNRy.png";
                    config.ImageColor = "1 1 1 0.8";
                    if (config.ImageAnchorMin == "0.175 0.017" && config.ImageAnchorMax == "0.22 0.08")
                    {
                        config.ImageAnchorMin = "0.18 0.017";
                        config.ImageAnchorMax = "0.22 0.09";
                    }
                    LogWarning("Updating image Icon URL");
                    SaveConfig();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        private void Loaded()
        {
            _hiddenOfflineData = Interface.Oxide.DataFileSystem.GetFile("VanishPlayers");
            LoadData();
            InitVanishedPlayers();
        }

        private void InitVanishedPlayers()
        {
            foreach (var playerid in _hiddenOffline)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if (player == null) continue;

                if (IsInvisible(player))
                    continue;

                if (!player.IsConnected)
                {
                    List<Connection> connections = Pool.Get<List<Connection>>();
                    foreach (var con in Net.sv.connections)
                    {
                        if (con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player)
                            connections.Add(con);
                    }
                    player.OnNetworkSubscribersLeave(connections);
                    player.DisablePlayerCollider();
                    player.syncPosition = false;
                    player.limitNetworking = true;
                    Pool.FreeUnmanaged(ref connections);
                }
                else
                {
                    Disappear(player);
                }
            }
        }
        #endregion Configuration

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VanishCommand"] = "vanish",
                ["Vanished"] = "Vanish: <color=orange> Enabled </color>",
                ["Reappear"] = "Vanish: <color=orange> Disabled </color>",
                ["NoPerms"] = "You do not have permission to do this",
                ["PermanentVanish"] = "You are in a permanent vanish mode",
                ["NoPlayers"] = "No players found using id: {0}"

            }, this);
        }

        #endregion Localization

        #region Initialization

        private const string PermAllow = "vanish.allow";
        private const string PermUnlock = "vanish.unlock";
        private const string PermDamage = "vanish.damage";
        private const string PermVanish = "vanish.permanent";
        private const string PermInvView = "vanish.invviewer";


        private void Init()
        {
            vanish = this;
            cachedVanishUI = CreateVanishUI();
            PlayerLayermask = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Player_Server));
            _registeredhooks = new List<string> { "CanUseLockedEntity", "OnEntityTakeDamage" };
            _EmptyDmgList = new DamageTypeList();

            // Register universal chat/console commands
            AddLocalizedCommand(nameof(VanishCommand));
            AddCovalenceCommand(config.InvViewCMD, "InventoryViewerCMD");

            // Register permissions for commands
            permission.RegisterPermission(PermAllow, this);
            permission.RegisterPermission(PermUnlock, this);
            permission.RegisterPermission(PermDamage, this);
            permission.RegisterPermission(PermVanish, this);
            permission.RegisterPermission(PermInvView, this);


            //Unsubscribe from hooks
            UnSubscribeFromHooks();

            if (!config.UseOnEntityTakeDamage)
            {
                _registeredhooks.Remove("OnEntityTakeDamage");
            }

            if (!config.BypassViolation)
            {
                _registeredhooks.Remove("OnPlayerViolation");
            }

            if (!config.UseCanUseLockedEntity)
            {
                _registeredhooks.Remove("CanUseLockedEntity");
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!HasPerm(player.UserIDString, PermVanish) || IsInvisible(player)) continue;
                Disappear(player);
            }

        }

        private void Unload()
        {
            for (int i = _hiddenPlayers.Count - 1; i > -1; i--)
            {
                BasePlayer? hiddenPlayer = GetPlayer(_hiddenPlayers[i]);
                if (hiddenPlayer == null) continue;

                if (!_hiddenOffline.Contains(hiddenPlayer.userID))
                    _hiddenOffline.Add(hiddenPlayer.userID);
                Reappear(hiddenPlayer);
            }
            SaveData();
            vanish = null;
            _registeredhooks = null;
            _EmptyDmgList = null;
        }

        private DynamicConfigFile _hiddenOfflineData;

        private void LoadData()
        {
            try
            {
                _hiddenOffline = _hiddenOfflineData.ReadObject<List<ulong>>();
            }
            catch
            {
                _hiddenOffline = new List<ulong>();
            }
            Puts("Load Data");
        }

        private void SaveData()
        {
            _hiddenOfflineData.WriteObject(_hiddenOffline);
        }

        #endregion Initialization

        #region Commands
        private void InventoryViewerCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = (BasePlayer)iplayer.Object;
            if (player == null) return;
            if (!HasPerm(player.UserIDString, PermInvView))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
                return;
            }
            if (args.Length < 1)
            {
                RaycastHit raycastHit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5f, PlayerLayermask))
                    return;

                BasePlayer? entity = raycastHit.GetEntity() as BasePlayer;

                if (entity == null)
                {
                    Message(player.IPlayer, "NoPlayers", "NoArgs");
                    return;
                }

                timer.Once(0.3f, () => { StartLootingPlayer(player, entity); });
                return;

            }
            BasePlayer? foundplayer = null;
            if (ulong.TryParse(args[0], out ulong steamID))
            {
                foundplayer = BasePlayer.FindAwakeOrSleepingByID(steamID);
            }
            else
            {
                foreach (var p in BasePlayer.allPlayerList)
                {
                    if (!p.displayName.StartsWith(args[0], StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    foundplayer = p;
                    break;
                }
            }
            if (foundplayer == null)
            {
                Message(player.IPlayer, "NoPlayers", args[0]);
                return;
            }

            timer.Once(0.3f, () => { StartLootingPlayer(player, foundplayer); });
        }

        private void StartLootingPlayer(BasePlayer player, BasePlayer foundplayer)
        {
            if (player == null || foundplayer == null)
                return;
            player.inventory.loot.AddContainer(foundplayer.inventory.containerMain);
            player.inventory.loot.AddContainer(foundplayer.inventory.containerWear);
            player.inventory.loot.AddContainer(foundplayer.inventory.containerBelt);
            player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPC<string>(RpcTarget.Player("RPC_OpenLootPanel", player), "player_corpse");
        }

        private void VanishCommand(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = (BasePlayer)iplayer.Object;
            if (player == null) return;
            if (!HasPerm(player.UserIDString, PermAllow))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
                return;
            }
            if (HasPerm(player.UserIDString, PermVanish))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "PermanentVanish");
                return;
            }
            if (IsInvisible(player)) Reappear(player);
            else Disappear(player);
        }

        private string drowneffect = "28ad47c8e6d313742a7a2740674a25b5";
        private string falldamageeffect = "ca14ed027d5924003b1c5d9e523a5fce";
        private void Reappear(BasePlayer player)
        {
            if (Interface.CallHook("OnVanishReappear", player) != null) return;

            if (config.AntiHack) player.ResetAntiHack();

            player.syncPosition = true;

            VanishPositionUpdate vanishPositionUpdate;
            if (player.TryGetComponent<VanishPositionUpdate>(out vanishPositionUpdate))
                UnityEngine.Object.Destroy(vanishPositionUpdate);

            SimpleAIMemory.RemoveIgnorePlayer(player);
            BaseEntity.Query.Server.RemovePlayer(player); // have to remove first in case of other plugins
            BaseEntity.Query.Server.AddPlayer(player);

            player._limitedNetworking = false;
            _hiddenPlayers.Remove(player.userID);

            player.EnablePlayerCollider();
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            player.GetHeldEntity()?.SendNetworkUpdate();

            //Un-Mute Player Effects
            player.drownEffect.guid = drowneffect;
            player.fallDamageEffect.guid = falldamageeffect;

            //metabolism
            if (config.Metabolism)
            {
                player.metabolism.temperature.min = -100;
                player.metabolism.temperature.max = 100;
                player.metabolism.radiation_poison.max = 500;
                player.metabolism.oxygen.min = 0;
                player.metabolism.calories.min = 0;
                player.metabolism.wetness.max = 1;
            }

            if (config.MetabolismReset && _storedMetabolism.TryGetValue(player.userID, out MetabolismValues value))
            {
                player.SetHealth(value.health);
                player.metabolism.hydration.SetValue(value.hydration);
                player.metabolism.calories.SetValue(value.calories);
                _storedMetabolism.Remove(player.userID);
            }
            player.metabolism.SendChangesToClient();

            if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();

            if (config.EnableSound)
            {
                if (config.PublicSound)
                {
                    Effect.server.Run(config.ReappearSoundEffect, player.transform.position);
                }
                else
                {
                    SendEffect(player, config.ReappearSoundEffect);
                }
            }

            CuiHelper.DestroyUi(player, "VanishUI");

            if (config.NoClipOnVanish && player.IsFlying) player.SendConsoleCommand(noclip);

            if (config.EnableNotifications) Message(player.IPlayer, "Reappear");
        }

        private class MetabolismValues
        {
            public float health;
            public float hydration;
            public float calories;
        }

        private GameObjectRef _emptygameObject = new GameObjectRef();
        private Dictionary<ulong, MetabolismValues> _storedMetabolism = new Dictionary<ulong, MetabolismValues>();
        private string noclip = "noclip";

        private void Disappear(BasePlayer player)
        {
            if (!_hiddenPlayers.Contains(player.userID))
                _hiddenPlayers.Add(player.userID);

            if (Interface.CallHook("OnVanishDisappear", player) != null) return;

            if (config.AntiHack)
            {
                player.PauseFlyHackDetection(float.MaxValue);
            }

            SimpleAIMemory.AddIgnorePlayer(player);
            BaseEntity.Query.Server.RemovePlayer(player);

            player.syncPosition = false;
            player.limitNetworking = true;
            player.fallDamageEffect = _emptygameObject;
            player.drownEffect = _emptygameObject;
            player.GetHeldEntity()?.SetHeld(false);
            player.DisablePlayerCollider();

            if (config.MetabolismReset)
                _storedMetabolism[player.userID] = new MetabolismValues() { health = player.health, hydration = player.metabolism.hydration.value, calories = player.metabolism.calories.value };

            //metabolism
            if (config.Metabolism)
            {
                player.SetHealth(100f);
                player.metabolism.calories.value = 500;
                player.metabolism.hydration.value = 250;
                player.metabolism.temperature.min = 20;
                player.metabolism.temperature.max = 20;
                player.metabolism.radiation_poison.max = 0;
                player.metabolism.oxygen.min = 1;
                player.metabolism.wetness.max = 0;
                player.metabolism.calories.min = player.metabolism.calories.value;
                player.metabolism.isDirty = true;
                player.metabolism.SendChangesToClient();
            }

            List<Connection> connections = Pool.Get<List<Connection>>();
            foreach (var con in Net.sv.connections)
            {
                if (con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player)
                    connections.Add(con);
            }
            player.OnNetworkSubscribersLeave(connections);
            Pool.FreeUnmanaged(ref connections);

            VanishPositionUpdate vanishPositionUpdate;
            if (player.TryGetComponent<VanishPositionUpdate>(out vanishPositionUpdate))
                UnityEngine.Object.Destroy(vanishPositionUpdate);

            player.gameObject.AddComponent<VanishPositionUpdate>();

            if (_hiddenPlayers.Count == 1) SubscribeToHooks();

            if (config.EnableSound)
            {
                if (config.PublicSound)
                {
                    Effect.server.Run(config.VanishSoundEffect, player.transform.position);
                }
                else
                {
                    SendEffect(player, config.VanishSoundEffect);
                }
            }

            if (config.NoClipOnVanish && !player.IsFlying && !player.isMounted) player.SendConsoleCommand(noclip);

            if (config.EnableGUI)
            {
                CuiHelper.AddUi(player, cachedVanishUI);
            }

            if (config.EnableNotifications) Message(player.IPlayer, "Vanished");
        }

        #endregion Commands

        #region Hooks
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;

            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(3f, () => OnPlayerConnected(player));
                return;
            }
            if (config.AboveWorldOnConnect && player._limitedNetworking)
            {
                float terrainY = TerrainMeta.HeightMap.GetHeight(player.transform.position);
                if (player.transform.position.y < terrainY)
                    player.transform.position = new Vector3(player.transform.position.x, terrainY + 0.5f, player.transform.position.z);
            }
            if (_hiddenOffline.Contains(player.userID))
            {
                _hiddenOffline.Remove(player.userID);
                if (HasPerm(player.UserIDString, PermAllow))
                {
                    Disappear(player);
                    return;

                }
            }

            if (HasPerm(player.UserIDString, PermVanish))
            {
                Disappear(player);
                return;
            }

            if (config.EnforceOnConnect && HasPerm(player.UserIDString, PermAllow))
            {
                Disappear(player);
                return;
            }
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (!player.limitNetworking) return null;
            if (HasPerm(player.UserIDString, PermUnlock)) return true;
            if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
            return null;
        }

        private object? OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || entity == null)
                return null;

            BasePlayer attacker = info.InitiatorPlayer;
            BasePlayer victim = entity.ToPlayer();
            if (!IsInvisible(victim) && !IsInvisible(attacker)) return null;
            if (attacker == null) return null;
            if (IsInvisible(attacker) && HasPerm(attacker.UserIDString, PermDamage)) return null;
            info.damageTypes = _EmptyDmgList;
            info.HitMaterial = 0;
            info.PointStart = Vector3.zero;
            info.HitEntity = null;
            return true;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (config.EnforceOnDisconnect && HasPerm(player.UserIDString, PermAllow))
            {
                Disappear(player);
            }

            if (!IsInvisible(player)) return;

            if (!config.HideOnDisconnect && !HasPerm(player.UserIDString, PermVanish))
                Reappear(player);
            else
            {
                if (config.UnderWorldOnDisconnect)
                {
                    float terrainY = TerrainMeta.HeightMap.GetHeight(player.transform.position);
                    if (player.transform.position.y > terrainY)
                        player.transform.position = new Vector3(player.transform.position.x, terrainY - 5f, player.transform.position.z);
                }

                if (!_hiddenOffline.Contains(player.userID))
                    _hiddenOffline.Add(player.userID);

                _hiddenPlayers.Remove(player.userID);
                VanishPositionUpdate t;
                if (player.TryGetComponent<VanishPositionUpdate>(out t))
                    UnityEngine.Object.Destroy(t);
            }
            if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();
        }

        private void OnPlayerSpectate(BasePlayer player, string spectateFilter)
        {
            VanishPositionUpdate vanishPositionUpdate;
            if (!player.TryGetComponent<VanishPositionUpdate>(out vanishPositionUpdate)) return;
            UnityEngine.Object.Destroy(vanishPositionUpdate);
        }

        private void OnPlayerSpectateEnd(BasePlayer player, string spectateFilter)
        {
            if (!player._limitedNetworking) return;

            VanishPositionUpdate vanishPositionUpdate;
            if (!player.TryGetComponent<VanishPositionUpdate>(out vanishPositionUpdate))
                player.gameObject.AddComponent<VanishPositionUpdate>().EndSpectate();
        }

        private object? OnPlayerColliderEnable(BasePlayer player, CapsuleCollider collider) => IsInvisible(player) ? (object)true : null;

        private object? OnPlayerViolation(BasePlayer player, AntiHackType type, float amount) => IsInvisible(player) ? (object)true : null;

        #endregion Hooks

        #region GUI
        private CuiElementContainer CreateVanishUI()
        {
            CuiElementContainer elements = new CuiElementContainer();
            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.0" },
                RectTransform = { AnchorMin = config.ImageAnchorMin, AnchorMax = config.ImageAnchorMax }
            }, "Hud.Menu", "VanishUI");
            elements.Add(new CuiElement
            {
                Parent = panel,
                Components =
                {
                    new CuiRawImageComponent {Color = config.ImageColor, Url = config.ImageUrlIcon},
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                }
            });
            return elements;
        }

        #endregion GUI

        #region Monobehaviour
        public class VanishPositionUpdate : FacepunchBehaviour
        {
            private BasePlayer player;
            private static int Layermask = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Construction), LayerMask.LayerToName((int)Layer.Deployed), LayerMask.LayerToName((int)Layer.Vehicle_World), LayerMask.LayerToName((int)Layer.Player_Server));
            LootableCorpse corpse;
            GameObject child;
            SphereCollider col;
            int LayerReserved1 = (int)Layer.Reserved1;
            BUTTON _reload = BUTTON.RELOAD;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                player.transform.localScale = Vector3.zero;
                CreateChildGO();
            }

            private void FixedUpdate()
            {
                if (player == null || !player.serverInput.IsDown(_reload) || !player.serverInput.WasDown(_reload)) return;
                player.serverInput.previous.buttons = 0;

                RaycastHit raycastHit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5f, Layermask))
                    return;

                BaseEntity entity = raycastHit.GetEntity() as BaseEntity;

                if (entity == null) return;
                if (entity is StorageContainer storageContainer)
                {
                    StorageContainer container = (StorageContainer)entity;
                    player.inventory.loot.Clear();
                    player.inventory.loot.AddContainer(container.inventory);
                    player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
                    player.inventory.loot.PositionChecks = false;
                    player.inventory.loot.MarkDirty();
                    player.SendNetworkUpdateImmediate();
                    player.ClientRPC<string>(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                    return;
                }

                if (entity is BasePlayer targetplayer)
                {
                    if (!vanish.HasPerm(player.UserIDString, PermInvView))
                        return;

                    player.inventory.loot.AddContainer(targetplayer.inventory.containerMain);
                    player.inventory.loot.AddContainer(targetplayer.inventory.containerWear);
                    player.inventory.loot.AddContainer(targetplayer.inventory.containerBelt);
                    player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
                    player.inventory.loot.PositionChecks = false;
                    player.inventory.loot.MarkDirty();
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC<string>(RpcTarget.Player("RPC_OpenLootPanel", player), "player_corpse");
                    return;
                }

                if (entity is Door door)
                {
                    if (door.IsOpen())
                    {
                        door.SetOpen(false, true);
                    }
                    else
                    {
                        door.SetOpen(true, false);
                    }
                    return;
                }

                BaseMountable component = entity.GetComponent<BaseMountable>();
                if (component == null)
                    return;
                component.AttemptMount(player, true);

            }

            private void UpdatePos()
            {
                if (player == null)
                    return;

                player.net.UpdateGroups(player.transform.position);
            }

            void OnTriggerEnter(Collider col)
            {
                TriggerParent triggerParent = col.GetComponentInParent<TriggerParent>();
                if (triggerParent != null)
                {
                    triggerParent.OnTriggerEnter(player.playerCollider);
                    return;
                }
                TriggerWorkbench triggerWorkbench = col.GetComponentInParent<TriggerWorkbench>();
                if (triggerWorkbench == null)
                    return;

                player.EnterTrigger(triggerWorkbench);
                player.nextCheckTime = float.MaxValue;
                player.cachedCraftLevel = triggerWorkbench.parentBench.Workbenchlevel;

                switch (player.cachedCraftLevel)
                {
                    case 1:
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, true); break;
                    case 2:
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, true); break;
                    case 3:
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, true); break;
                }
            }

            void OnTriggerExit(Collider col)
            {
                TriggerParent triggerParent = col.GetComponentInParent<TriggerParent>();
                if (triggerParent != null)
                {
                    triggerParent.OnTriggerExit(player.playerCollider);
                    return;
                }
                TriggerWorkbench triggerWorkbench = col.GetComponentInParent<TriggerWorkbench>();
                if (triggerWorkbench != null)
                {
                    player.LeaveTrigger(triggerWorkbench);
                    player.cachedCraftLevel = 0f;
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, false);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, false);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, false);
                    player.nextCheckTime = Time.realtimeSinceStartup;
                    return;
                }
            }

            public void EndSpectate()
            {
                InvokeRepeating(RespawnCheck, 1f, 0.5f);
            }

            public void RespawnCheck()
            {
                if (player == null || !player.IsAlive()) return;
                CancelInvoke(RespawnCheck);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                player.SendNetworkUpdateImmediate();
                CreateChildGO();
            }

            public void CreateChildGO()
            {
                if (player == null || player.IsSpectating())
                    return;

                player.transform.localScale = Vector3.zero;
                child = gameObject.CreateChild();
                col = child.AddComponent<SphereCollider>();
                child.layer = LayerReserved1;
                child.transform.localScale = Vector3.zero;
                col.isTrigger = true;
                player.lastAdminCheatTime = float.MaxValue;
                InvokeRepeating("UpdatePos", 1f, 5f);
            }

            private void OnDestroy()
            {
                CancelInvoke(UpdatePos);

                if (player != null)
                {
                    if (player.IsConnected)
                        player.Connection.active = true;

                    player.lastAdminCheatTime = Time.realtimeSinceStartup;
                    player.transform.localScale = new Vector3(1, 1, 1);

                    //Reset Triggers
                    if (player.triggers != null)
                    {
                        for (int i = player.triggers.Count - 1; i >= 0; i--)
                        {
                            if (player.triggers[i] is TriggerWorkbench)
                            {
                                player.triggers[i].OnEntityLeave(player);
                                player.triggers.RemoveAt(i);
                            }
                        }
                    }

                    //Reset Workbench Level
                    player.cachedCraftLevel = 0f;
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, false);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, false);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, false);
                    player.nextCheckTime = Time.realtimeSinceStartup;
                }

                if (col != null)
                    Destroy(col);
                if (child != null)
                    Destroy(child);

                GameObject.Destroy(this);
            }

        }

        #endregion Monobehaviour

        #region Helpers
        private BasePlayer? GetPlayer(ulong steamID)
        {
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.userID.Get() != steamID)
                    continue;

                return player;
            }
            return null;
        }
        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (!message.Key.Equals(command)) continue;

                    if (string.IsNullOrEmpty(message.Value)) continue;

                    AddCovalenceCommand(message.Value, command);
                }
            }
        }

        private bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

        private string GetLang(string langKey, string playerId = null, params object[] args) => string.Format(lang.GetMessage(langKey, this, playerId), args);

        private void Message(IPlayer player, string langKey, params object[] args)
        {
            if (player.IsConnected) player.Message(GetLang(langKey, player.Id, args));
        }

        private bool IsInvisible(BasePlayer player) => player?._limitedNetworking ?? false;

        private void UnSubscribeFromHooks()
        {
            foreach (var hook in _registeredhooks)
                Unsubscribe(hook);
        }

        private void SubscribeToHooks()
        {
            foreach (var hook in _registeredhooks)
                Subscribe(hook);

        }

        private static void SendEffect(BasePlayer player, string sound) => EffectNetwork.Send(new Effect(sound, player, 0, Vector3.zero, Vector3.forward), player.net.connection);


        #endregion Helpers

        #region Public Helpers
        public void _Disappear(BasePlayer basePlayer) => Disappear(basePlayer);
        public void _Reappear(BasePlayer basePlayer) => Reappear(basePlayer);
        public bool _IsInvisible(BasePlayer basePlayer) => IsInvisible(basePlayer);
        #endregion
    }
}