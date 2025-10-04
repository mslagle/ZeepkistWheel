using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RewiredConsts;
using System;
using System.Linq;
using UnityEngine;
using ZeepSDK.Racing;

namespace Zeepkist.Wheel
{

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("ZeepSDK")]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;

        public static ConfigEntry<bool> EnableReason { get; private set; }
        public static ConfigEntry<bool> EnableCrash { get; private set; }
        public static ConfigEntry<bool> EnableWheel { get; private set; }
        public static ConfigEntry<bool> EnableCheckpoint { get; private set; }
        public static ConfigEntry<bool> EnableFinish { get; private set; }
        public static ConfigEntry<bool> EnableSpawn { get; private set; }
        public static ConfigEntry<bool> EnableMud { get; private set; }
        public static ConfigEntry<bool> EnableCentering { get; private set; }
        public static ConfigEntry<bool> EnableGForce { get; private set; }
        public static ConfigEntry<float> MinimumGForce { get; private set; }
        public static ConfigEntry<float> MaximumGForce { get; private set; }
        public static ConfigEntry<int> Gain { get; private set; }
        public static ConfigEntry<bool> EnableTireSmoke { get; private set; }
        public static ConfigEntry<KeyCode> ReinitWheel { get; private set; }

        static SDL3ForceFeedback forceFeedback = new SDL3ForceFeedback();

        static New_ControlCar playerCar = null;

        private float updateTimer = 0.0f;
        private const float updateInterval = 0.1f; // 100 milliseconds

        // States
        static bool isDead { get; set; }
        static bool isInitalized { get; set; }

        private void Awake()
        {
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // Plugin startup logic
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            EnableReason = Config.Bind<bool>("Mod", "Enable message for rumble reason", false);
            EnableCentering = Config.Bind<bool>("Mod", "Enable for auto-centering", true);
            EnableCrash = Config.Bind<bool>("Mod", "Enable for crash", true);
            EnableWheel = Config.Bind<bool>("Mod", "Enable for wheel loss", true);
            EnableFinish = Config.Bind<bool>("Mod", "Enable for finish", true);
            EnableCheckpoint = Config.Bind<bool>("Mod", "Enable for checkpoint", true);
            EnableSpawn = Config.Bind<bool>("Mod", "Enable for spawn", true);
            EnableMud = Config.Bind<bool>("Mod", "Enable for mud", true);
            EnableTireSmoke = Config.Bind<bool>("Mod", "Enable for tiresmoke", true);
            ReinitWheel = Config.Bind<KeyCode>("Mod", "Re-init Wheel", KeyCode.F2);
            Gain = Config.Bind<int>("Mod", "Force Feedback Gain", 80);

            EnableGForce = Config.Bind<bool>("G Force", "Enable for high G force", true);
            MinimumGForce = Config.Bind<float>("G Force", "Minimum G Force", 3);
            MaximumGForce = Config.Bind<float>("G Force", "Maximum G Force", 10);

            EnableReason.SettingChanged += SettingChanged;
            EnableCentering.SettingChanged += SettingChanged;
            EnableCrash.SettingChanged += SettingChanged;
            EnableWheel.SettingChanged += SettingChanged;
            EnableFinish.SettingChanged += SettingChanged;
            EnableCheckpoint.SettingChanged += SettingChanged;
            EnableSpawn.SettingChanged += SettingChanged;
            EnableMud.SettingChanged += SettingChanged;
            EnableTireSmoke.SettingChanged += SettingChanged;

            EnableGForce.SettingChanged += SettingChanged;
            MinimumGForce.SettingChanged += SettingChanged;
            MaximumGForce.SettingChanged += SettingChanged;

            RacingApi.Crashed += RacingApi_Crashed;
            RacingApi.PlayerSpawned += RacingApi_PlayerSpawned;
            RacingApi.PassedCheckpoint += RacingApi_PassedCheckpoint;
            RacingApi.WheelBroken += RacingApi_WheelBroken;
            RacingApi.CrossedFinishLine += RacingApi_CrossedFinishLine;

            RacingApi.Quit += () =>
            {
                forceFeedback?.StopAllEffects();
                isDead = true;
                Logger.LogInfo($"Quit to menu, setting isDead = true");
            };
        }

        private void SettingChanged(object sender, EventArgs e)
        {
            var configObject = sender as ConfigEntryBase;

            if (configObject != null)
            {
                Logger.LogInfo($"Detected config change.  {configObject.Definition} = {configObject.BoxedValue}");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(ReinitWheel.Value))
            {
                Logger.LogInfo($"Re-initializing wheel force feedback due to keypress {ReinitWheel.Value}");
                InitalizeWheel();
                return;
            }

            if (playerCar == null || isDead == true || forceFeedback == null || isInitalized == false)
            {
                return;
            }

            // Only need to run every 100 ms
            updateTimer += Time.deltaTime;

            if (updateTimer <= updateInterval)
            {
                return;
            }

            // Reset the timer
            updateTimer = 0.0f;

            // Detect when hitting ground hard
            if (EnableGForce.Value && playerCar.localGForce.y > MinimumGForce.Value)
            {
                float strength = playerCar.localGForce.y / MaximumGForce.Value;
                if (strength > 1)
                {
                    strength = 1.0f;
                }
                Logger.LogInfo($"Detected strong Y Gforce of {playerCar.localGForce.y} = {strength}");
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Y Force", 1.0f);
                }
                forceFeedback.PlayRumble(200, strength);

            }

            // Detect when hitting something hard
            if (EnableGForce.Value && playerCar.localGForce.x > MinimumGForce.Value)
            {
                float strength = playerCar.localGForce.x / MaximumGForce.Value;
                if (strength > 1)
                {
                    strength = 1.0f;
                }
                Logger.LogInfo($"Detected strong X Gforce of {playerCar.localGForce.x} = {strength}");
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - X Force", 1.0f);
                }
                forceFeedback.PlayRumble(200, strength);
            }

            // Auto-centering of front wheels if on ground
            if (EnableCentering.Value)
            {
                var frontWheelsOnGround = playerCar.wheels.All(x => x.IsGrounded());
                Logger.LogInfo($"Front wheels on ground: {frontWheelsOnGround}");
                if (frontWheelsOnGround)
                {
                    // Get a surface normal from one of the front wheels
                    var surface = playerCar.wheels.FirstOrDefault(x => x.isFrontWheel).GetCurrentSurface();
                    var friction = surface.physics.frictionFront;

                    // Adjust the centering strength based on speed and surface friction
                    float speedFactor = (playerCar.GetLocalVelocity().magnitude > 50 ? 50 : playerCar.localVelocity.magnitude) / 50; // Normalize speed factor (0 to 1)
                    short centeringStrength = (short)Math.Round(friction * speedFactor * 20000);

                    if (centeringStrength > 15000)
                    {
                        centeringStrength = 15000;
                    }
                    if (centeringStrength < 3000)
                    {
                        centeringStrength = 3000;
                    }

                    //Logger.LogInfo($"Auto-centering with friction {friction}, speedFactor {speedFactor}, centeringStrength {centeringStrength} - {playerCar.GetLocalVelocity().magnitude}");
                    forceFeedback.UpdateSpringEffect(centeringStrength);
                } else
                {
                    forceFeedback.UpdateSpringEffect(0);
                }
            }

            // Make steering harder if in mud or sand
            if (EnableMud.Value)
            {
                // Detect when wheels are in mud
                var wheel = playerCar.wheels.FirstOrDefault(x => x.IsGrounded()).GetCurrentSurface();
                var wheelInMud = wheel.physics.name.ToLower().Contains("mud") || wheel.physics.name.ToLower().Contains("sand");
                if (wheelInMud)
                {
                    float maxMudValue = Math.Abs(1.1f - (playerCar.localVelocity.magnitude > 50 ? 50 : playerCar.localVelocity.magnitude) / 50f);
                    short dampingValue = (short)Math.Round(maxMudValue * 32000);

                    Logger.LogInfo($"Adding damping due to mud with mud value {maxMudValue} and damping {dampingValue}");
                    forceFeedback.PlayDampingEffect(dampingValue);
                } else
                {
                    forceFeedback.PlayDampingEffect(0);
                }
            }   


            // Only run the following in 3rd person so 1st person doesnt get advantage
            if (EnableTireSmoke.Value && false)
            {
                // Detect when wheels are slipping
                var wheelLocked = playerCar.wheels.FirstOrDefault(x => x.IsGrounded() && x.IsSlipping());
                if (wheelLocked != null)
                {
                    float rumbleIntensity = wheelLocked.GetCurrentSurface().physics.frictionFront / 1.5f;
                    if (rumbleIntensity >= 0.0001f)
                    {
                        Logger.LogInfo($"Detected a wheel slipping on a hard surface {wheelLocked.name} on {wheelLocked.GetCurrentSurface().name} with {wheelLocked.GetCurrentSurface().physics.frictionFront} with rumble intensity = {rumbleIntensity}");

                        if (EnableReason.Value)
                        {
                            PlayerManager.Instance.messenger.Log("Force feedback - Wheel smoke", 1.0f);
                        }
                        forceFeedback.PlayRumble(50, rumbleIntensity, 25);
                    }

                }
            }
        }

        private void InitalizeWheel()
        {
            Logger.LogInfo($"Initializing wheel force feedback");
            if (!forceFeedback.Initialize())
            {
                PlayerManager.Instance.messenger.LogError("Force feedback wheel not found!", 2.0f);

                Logger.LogError("Failed to initialize force feedback for wheel.");
                Logger.LogError(forceFeedback.GetLastError());
                return;
            }

            isInitalized = true;
            Logger.LogInfo("Force feedback initialized successfully.");
            PlayerManager.Instance.messenger.Log("Force feedback wheel found and initialized!", 2.0f);

            Logger.LogInfo($"Initialized: {forceFeedback.DeviceName}");
            Logger.LogInfo($"Supported features: 0x{forceFeedback.GetSupportedFeatures():X}\n");

            forceFeedback.SetGain(Gain.Value);
            forceFeedback.SetAutocenter(20);
        }

        private void RacingApi_WheelBroken()
        {
            if (EnableWheel.Value)
            {
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Wheel broken", 1.0f);
                }
                Logger.LogInfo($"Detected a broken wheel");
                forceFeedback.PlayRumble(100, .5f, 100);
            }
        }

        private void RacingApi_PassedCheckpoint(float time)
        {
            if (EnableCheckpoint.Value)
            {
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Checkpoint", 1.0f);
                }
                Logger.LogInfo($"Detected passing a checkpoint");
                forceFeedback.PlayRumble(50, .1f, 50);
            }
        }

        private void RacingApi_PlayerSpawned()
        {
            playerCar = PlayerManager.Instance.currentMaster.carSetups.First().cc;
            isDead = false;
            Logger.LogInfo($"Detected a player spawn, setting isDead = false and isFirstPerson = false, player car = {playerCar}");

            if (EnableSpawn.Value)
            {
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Player spawn", 1.0f);
                }
                forceFeedback.PlayRumble(100, .5f, 50);
            }

        }

        private void RacingApi_Crashed(CrashReason reason)
        {
            isDead = true;
            Logger.LogInfo($"Detected a crash, setting isDead = true");

            if (EnableCrash.Value)
            {
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Crash", 1.0f);
                }
                forceFeedback.PlayRumble(500, 1f, 125);
            }
        }

        private void RacingApi_CrossedFinishLine(float time)
        {
            isDead = true;
            Logger.LogInfo($"Crossed Finish line {time}, setting isDead = true");

            if (EnableCrash.Value)
            {
                if (EnableReason.Value)
                {
                    PlayerManager.Instance.messenger.Log("Force feedback - Finish", 1.0f);
                }

                forceFeedback.PlayRumble(300, .5f);
            }
        }

        public void OnDestroy()
        {
            forceFeedback?.StopAllEffects();
            forceFeedback?.Cleanup();

            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}