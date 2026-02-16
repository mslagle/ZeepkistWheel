// SDL3ForceFeedback.cs - Complete Force Feedback Library
// Place SDL3.dll in your output directory

using BepInEx.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Zeepkist.Wheel
{
    public class SDL3ForceFeedback
    {
        private const string SDL_LIB = "SDL3.dll";
        private const uint SDL_HAPTIC_CONSTANT = (1u << 0);
        private const uint SDL_HAPTIC_SINE = (1u << 1);
        private const uint SDL_HAPTIC_SPRING = (1u << 7);
        private const uint SDL_HAPTIC_DAMPER = (1u << 8);
        private const uint SDL_HAPTIC_FRICTION = (1u << 10);
        private const uint SDL_HAPTIC_AUTOCENTER = (1u << 17);
        private const uint SDL_HAPTIC_INFINITY = 0xFFFFFFFF;

        private ManualLogSource logger = Logger.CreateLogSource("SDL3ForceFeedback");

        [StructLayout(LayoutKind.Sequential)]
        private struct SDL_HapticDirection
        {
            public byte type;
            public int dir0, dir1, dir2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SDL_HapticConstant
        {
            public ushort type;
            public SDL_HapticDirection direction;
            public uint length;
            public ushort delay, button, interval;
            public short level;
            public ushort attack_length, attack_level, fade_length, fade_level;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SDL_HapticCondition
        {
            public ushort type;
            public SDL_HapticDirection direction;
            public uint length;
            public ushort delay, button, interval;
            public ushort right_sat0, right_sat1, right_sat2;
            public ushort left_sat0, left_sat1, left_sat2;
            public short right_coeff0, right_coeff1, right_coeff2;
            public short left_coeff0, left_coeff1, left_coeff2;
            public ushort deadband0, deadband1, deadband2;
            public short center0, center1, center2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SDL_HapticPeriodic
        {
            public ushort type;
            public SDL_HapticDirection direction;
            public uint length;
            public ushort delay, button, interval, period;
            public short magnitude, offset;
            public ushort phase, attack_length, attack_level, fade_length, fade_level;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct SDL_HapticEffect
        {
            [FieldOffset(0)] public ushort type;
            [FieldOffset(0)] public SDL_HapticConstant constant;
            [FieldOffset(0)] public SDL_HapticCondition condition;
            [FieldOffset(0)] public SDL_HapticPeriodic periodic;
        }

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_Init(uint flags);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_Quit();

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetJoysticks(out int count);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_OpenJoystick(uint instance_id);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_CloseJoystick(IntPtr joystick);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetJoystickName(IntPtr joystick);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_OpenHapticFromJoystick(IntPtr joystick);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_CloseHaptic(IntPtr haptic);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetHapticFeatures(IntPtr haptic);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_CreateHapticEffect(IntPtr haptic, ref SDL_HapticEffect effect);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_RunHapticEffect(IntPtr haptic, int effect, uint iterations);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_StopHapticEffect(IntPtr haptic, int effect);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_DestroyHapticEffect(IntPtr haptic, int effect);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_SetHapticAutocenter(IntPtr haptic, int autocenter);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_SetHapticGain(IntPtr haptic, int gain);

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetError();

        [DllImport(SDL_LIB, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_UpdateHapticEffect(IntPtr haptic, int effect, ref SDL_HapticEffect data);

        private IntPtr joystick = IntPtr.Zero;
        private IntPtr haptic = IntPtr.Zero;
        private int constantEffectId = -1;
        private int springEffectId = -1;
        private int dampingEffectId = -1;
        private int frictionEffectId = -1;
        private int sineEffectId = -1;
        private int shakeEffectId = -1;
        private CancellationTokenSource rumbleCancellation = null;
        private CancellationTokenSource shakeCancellation = null;

        public bool IsInitialized => haptic != IntPtr.Zero;
        public string DeviceName { get; private set; } = "";

        public bool Initialize(int deviceIndex = -1)
        {
            if (!SDL_Init(0x00000200 | 0x00001000)) return false;

            int count;
            IntPtr joysticksPtr = SDL_GetJoysticks(out count);
            if (count == 0) return false;

            uint[] joystickIds = new uint[count];
            Marshal.Copy(joysticksPtr, (int[])(object)joystickIds, 0, count);

            // If specific device requested, use it
            if (deviceIndex >= 0 && deviceIndex < count)
            {
                return InitializeDevice(joystickIds[deviceIndex]);
            }

            // Auto-detect: Try to find a device with force feedback
            logger.LogInfo($"Found {count} device(s), searching for steering wheel...");

            for (int i = 0; i < count; i++)
            {
                IntPtr testJoy = SDL_OpenJoystick(joystickIds[i]);
                if (testJoy == IntPtr.Zero) continue;

                string name = Marshal.PtrToStringAnsi(SDL_GetJoystickName(testJoy)) ?? "Unknown";
                logger.LogInfo($"  [{i}] {name}");

                // Check if it has haptic support
                IntPtr testHaptic = SDL_OpenHapticFromJoystick(testJoy);
                if (testHaptic != IntPtr.Zero)
                {
                    uint features = SDL_GetHapticFeatures(testHaptic);

                    // Check if it supports force feedback effects (not just rumble)
                    bool hasForceFeeback = (features & (SDL_HAPTIC_CONSTANT | SDL_HAPTIC_SPRING | SDL_HAPTIC_DAMPER)) != 0;

                    if (hasForceFeeback)
                    {
                        // Found a device with force feedback - keep it
                        logger.LogInfo($"  ✓ Selected: {name} (has force feedback)");
                        joystick = testJoy;
                        haptic = testHaptic;
                        DeviceName = name;
                        return true;
                    }

                    SDL_CloseHaptic(testHaptic);
                }

                SDL_CloseJoystick(testJoy);
            }

            // No force feedback device found, fall back to first device
            logger.LogInfo("  No force feedback device found, using first available...");
            return InitializeDevice(joystickIds[0]);
        }

        private bool InitializeDevice(uint joystickId)
        {
            joystick = SDL_OpenJoystick(joystickId);
            if (joystick == IntPtr.Zero) return false;

            DeviceName = Marshal.PtrToStringAnsi(SDL_GetJoystickName(joystick)) ?? "Unknown";

            haptic = SDL_OpenHapticFromJoystick(joystick);
            if (haptic == IntPtr.Zero)
            {
                SDL_CloseJoystick(joystick);
                joystick = IntPtr.Zero;
                return false;
            }
            return true;
        }

        public uint GetSupportedFeatures()
        {
            return haptic == IntPtr.Zero ? 0 : SDL_GetHapticFeatures(haptic);
        }

        public string[] GetSupportedFeatureNames()
        {
            if (haptic == IntPtr.Zero) return new string[0];

            uint features = SDL_GetHapticFeatures(haptic);
            var featureList = new System.Collections.Generic.List<string>();

            if ((features & SDL_HAPTIC_CONSTANT) != 0) featureList.Add("Constant Force");
            if ((features & SDL_HAPTIC_SINE) != 0) featureList.Add("Sine Wave");
            if ((features & (1u << 2)) != 0) featureList.Add("Square Wave");
            if ((features & (1u << 3)) != 0) featureList.Add("Triangle Wave");
            if ((features & (1u << 4)) != 0) featureList.Add("Sawtooth Up");
            if ((features & (1u << 5)) != 0) featureList.Add("Sawtooth Down");
            if ((features & (1u << 6)) != 0) featureList.Add("Ramp");
            if ((features & SDL_HAPTIC_SPRING) != 0) featureList.Add("Spring");
            if ((features & SDL_HAPTIC_DAMPER) != 0) featureList.Add("Damper");
            if ((features & (1u << 9)) != 0) featureList.Add("Inertia");
            if ((features & SDL_HAPTIC_FRICTION) != 0) featureList.Add("Friction");
            if ((features & (1u << 11)) != 0) featureList.Add("Left/Right");
            if ((features & (1u << 15)) != 0) featureList.Add("Custom");
            if ((features & (1u << 16)) != 0) featureList.Add("Gain");
            if ((features & SDL_HAPTIC_AUTOCENTER) != 0) featureList.Add("Autocenter");
            if ((features & (1u << 18)) != 0) featureList.Add("Status");
            if ((features & (1u << 19)) != 0) featureList.Add("Pause");

            return featureList.ToArray();
        }

        public string GetFeaturesString()
        {
            var features = GetSupportedFeatureNames();
            return features.Length > 0 ? string.Join(", ", features) : "None";
        }

        public bool PlayConstantForce(short magnitude, uint duration = 1000)
        {
            if (haptic == IntPtr.Zero) return false;

            if (constantEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, constantEffectId);
                SDL_DestroyHapticEffect(haptic, constantEffectId);
                constantEffectId = -1;
            }

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_CONSTANT,
                constant = new SDL_HapticConstant
                {
                    type = (ushort)SDL_HAPTIC_CONSTANT,
                    length = duration,
                    level = magnitude,
                    attack_length = 100,
                    fade_length = 100
                }
            };

            constantEffectId = SDL_CreateHapticEffect(haptic, ref effect);
            return constantEffectId >= 0 && SDL_RunHapticEffect(haptic, constantEffectId, 1);
        }

        public bool UpdateSpringEffect(short coefficient, short center = 0)
        {
            if (haptic == IntPtr.Zero) return false;

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_SPRING,
                condition = new SDL_HapticCondition
                {
                    type = (ushort)SDL_HAPTIC_SPRING,
                    length = SDL_HAPTIC_INFINITY,
                    right_sat0 = 0xFFFF,
                    right_sat1 = 0xFFFF,
                    right_sat2 = 0xFFFF,
                    left_sat0 = 0xFFFF,
                    left_sat1 = 0xFFFF,
                    left_sat2 = 0xFFFF,
                    right_coeff0 = coefficient,
                    right_coeff1 = coefficient,
                    right_coeff2 = coefficient,
                    left_coeff0 = coefficient,
                    left_coeff1 = coefficient,
                    left_coeff2 = coefficient,
                    center0 = center,
                    center1 = center,
                    center2 = center
                }
            };

            if (springEffectId < 0)
            {
                // Create the effect if it doesn't exist
                springEffectId = SDL_CreateHapticEffect(haptic, ref effect);
                if (springEffectId < 0) return false;
                return SDL_RunHapticEffect(haptic, springEffectId, 1);
            }
            else
            {
                // Update the existing effect
                return SDL_UpdateHapticEffect(haptic, springEffectId, ref effect);
            }
        }

        public short CalculateSpringFromSpeed(float speedKmh, float minSpeed = 0f, float maxSpeed = 200f)
        {
            const short minCoeff = 3000, maxCoeff = 15000;
            speedKmh = Math.Max(minSpeed, Math.Min(maxSpeed, speedKmh));
            float normalized = speedKmh / maxSpeed;
            return (short)(maxCoeff - (normalized * (maxCoeff - minCoeff)));
        }

        public bool UpdateDampingEffect(short coefficient)
        {
            if (haptic == IntPtr.Zero) return false;

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_DAMPER,
                condition = new SDL_HapticCondition
                {
                    type = (ushort)SDL_HAPTIC_DAMPER,
                    length = SDL_HAPTIC_INFINITY,
                    right_sat0 = 0xFFFF,
                    right_sat1 = 0xFFFF,
                    right_sat2 = 0xFFFF,
                    left_sat0 = 0xFFFF,
                    left_sat1 = 0xFFFF,
                    left_sat2 = 0xFFFF,
                    right_coeff0 = coefficient,
                    right_coeff1 = coefficient,
                    right_coeff2 = coefficient,
                    left_coeff0 = coefficient,
                    left_coeff1 = coefficient,
                    left_coeff2 = coefficient
                }
            };

            if (dampingEffectId < 0)
            {
                // Create the effect if it doesn't exist
                dampingEffectId = SDL_CreateHapticEffect(haptic, ref effect);
                if (dampingEffectId < 0) return false;
                return SDL_RunHapticEffect(haptic, dampingEffectId, 1);
            }
            else
            {
                // Update the existing effect
                return SDL_UpdateHapticEffect(haptic, dampingEffectId, ref effect);
            }
        }

        public short CalculateDampingFromSpeed(float speedKmh, string surfaceType = "asphalt")
        {
            const short minDamping = 2000, maxDamping = 5000;
            speedKmh = Math.Max(0f, Math.Min(200f, speedKmh));
            float normalized = speedKmh / 200f;
            short base_damping = (short)(minDamping + (normalized * (maxDamping - minDamping)));

            float multiplier = surfaceType.ToLower() switch
            {
                "asphalt" => 1.0f,
                "concrete" => 1.1f,
                "gravel" => 1.5f,
                "dirt" => 1.8f,
                "mud" => 2.5f,
                "sand" => 2.0f,
                "grass" => 1.6f,
                "snow" => 1.3f,
                "ice" => 0.7f,
                _ => 1.0f
            };

            return (short)Math.Min(15000, (int)(base_damping * multiplier));
        }

        public bool PlayFrictionEffect(short coefficient = 8000)
        {
            if (haptic == IntPtr.Zero) return false;

            if (frictionEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, frictionEffectId);
                SDL_DestroyHapticEffect(haptic, frictionEffectId);
                frictionEffectId = -1;
            }

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_FRICTION,
                condition = new SDL_HapticCondition
                {
                    type = (ushort)SDL_HAPTIC_FRICTION,
                    length = SDL_HAPTIC_INFINITY,
                    right_sat0 = 0xFFFF,
                    right_sat1 = 0xFFFF,
                    right_sat2 = 0xFFFF,
                    left_sat0 = 0xFFFF,
                    left_sat1 = 0xFFFF,
                    left_sat2 = 0xFFFF,
                    right_coeff0 = coefficient,
                    right_coeff1 = coefficient,
                    right_coeff2 = coefficient,
                    left_coeff0 = coefficient,
                    left_coeff1 = coefficient,
                    left_coeff2 = coefficient
                }
            };

            frictionEffectId = SDL_CreateHapticEffect(haptic, ref effect);
            return frictionEffectId >= 0 && SDL_RunHapticEffect(haptic, frictionEffectId, 1);
        }

        public bool PlayRumble(uint durationMs, float intensity = 0.75f, ushort frequency = 100)
        {
            if (haptic == IntPtr.Zero) return false;

            if (rumbleCancellation != null)
            {
                rumbleCancellation.Cancel();
                rumbleCancellation.Dispose();
                rumbleCancellation = null;
            }

            intensity = Math.Max(0.0f, Math.Min(1.0f, intensity));
            short magnitude = (short)(intensity * 32767);

            if (sineEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, sineEffectId);
                SDL_DestroyHapticEffect(haptic, sineEffectId);
                sineEffectId = -1;
            }

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_SINE,
                periodic = new SDL_HapticPeriodic
                {
                    type = (ushort)SDL_HAPTIC_SINE,
                    length = SDL_HAPTIC_INFINITY,
                    period = frequency,
                    magnitude = magnitude,
                    attack_length = 50,
                    fade_length = 50
                }
            };

            sineEffectId = SDL_CreateHapticEffect(haptic, ref effect);
            if (sineEffectId >= 0 && SDL_RunHapticEffect(haptic, sineEffectId, 1))
            {
                rumbleCancellation = new CancellationTokenSource();
                Task.Delay((int)durationMs, rumbleCancellation.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled && sineEffectId >= 0)
                        SDL_StopHapticEffect(haptic, sineEffectId);
                }, TaskScheduler.Default);
                return true;
            }
            return false;
        }

        public bool PlayShake(uint durationMs, float intensity = 0.75f, ushort frequency = 100)
        {
            if (haptic == IntPtr.Zero) return false;

            if (shakeCancellation != null)
            {
                shakeCancellation.Cancel();
                shakeCancellation.Dispose();
                shakeCancellation = null;
            }

            intensity = Math.Max(0.0f, Math.Min(1.0f, intensity));
            short magnitude = (short)(intensity * 32767);

            if (shakeEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, shakeEffectId);
                SDL_DestroyHapticEffect(haptic, shakeEffectId);
                shakeEffectId = -1;
            }

            var effect = new SDL_HapticEffect
            {
                type = (ushort)SDL_HAPTIC_SINE,
                periodic = new SDL_HapticPeriodic
                {
                    type = (ushort)SDL_HAPTIC_SINE,
                    length = SDL_HAPTIC_INFINITY,
                    period = frequency,
                    magnitude = magnitude,
                    attack_length = 10,
                    fade_length = 10
                }
            };

            shakeEffectId = SDL_CreateHapticEffect(haptic, ref effect);
            if (shakeEffectId >= 0 && SDL_RunHapticEffect(haptic, shakeEffectId, 1))
            {
                shakeCancellation = new CancellationTokenSource();
                Task.Delay((int)durationMs, shakeCancellation.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled && shakeEffectId >= 0)
                        SDL_StopHapticEffect(haptic, shakeEffectId);
                }, TaskScheduler.Default);
                return true;
            }
            return false;
        }

        public void RumbleLight(uint durationMs = 200) => PlayRumble(durationMs, 0.3f, 150);
        public void RumbleMedium(uint durationMs = 300) => PlayRumble(durationMs, 0.6f, 100);
        public void RumbleHeavy(uint durationMs = 500) => PlayRumble(durationMs, 0.9f, 60);
        public void RumbleCollision(uint durationMs = 150) => PlayRumble(durationMs, 1.0f, 50);
        public void RumbleRoughRoad(uint durationMs = 1000) => PlayRumble(durationMs, 0.4f, 80);

        public void StopAllEffects()
        {
            if (constantEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, constantEffectId);
                SDL_DestroyHapticEffect(haptic, constantEffectId);
                constantEffectId = -1;
            }
            if (springEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, springEffectId);
                SDL_DestroyHapticEffect(haptic, springEffectId);
                springEffectId = -1;
            }
            if (dampingEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, dampingEffectId);
                SDL_DestroyHapticEffect(haptic, dampingEffectId);
                dampingEffectId = -1;
            }
            if (frictionEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, frictionEffectId);
                SDL_DestroyHapticEffect(haptic, frictionEffectId);
                frictionEffectId = -1;
            }
            if (sineEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, sineEffectId);
                SDL_DestroyHapticEffect(haptic, sineEffectId);
                sineEffectId = -1;
            }
            if (shakeEffectId >= 0)
            {
                SDL_StopHapticEffect(haptic, shakeEffectId);
                SDL_DestroyHapticEffect(haptic, shakeEffectId);
                shakeEffectId = -1;
            }
        }

        public bool SetAutocenter(int percentage) => haptic != IntPtr.Zero && SDL_SetHapticAutocenter(haptic, percentage);
        public bool SetGain(int percentage) => haptic != IntPtr.Zero && SDL_SetHapticGain(haptic, percentage);

        public void UpdateForRacingGame(float speedKmh, string surface = "asphalt", bool isDrifting = false)
        {
            short spring = CalculateSpringFromSpeed(speedKmh);
            short damping = CalculateDampingFromSpeed(speedKmh, surface);

            if (isDrifting)
            {
                spring = (short)(spring * 0.5f);
                damping = (short)(damping * 0.7f);
            }

            UpdateSpringEffect(spring);
            UpdateDampingEffect(damping);
        }

        public void Cleanup()
        {
            if (rumbleCancellation != null)
            {
                rumbleCancellation.Cancel();
                rumbleCancellation.Dispose();
            }
            if (shakeCancellation != null)
            {
                shakeCancellation.Cancel();
                shakeCancellation.Dispose();
            }

            if (constantEffectId >= 0) SDL_DestroyHapticEffect(haptic, constantEffectId);
            if (springEffectId >= 0) SDL_DestroyHapticEffect(haptic, springEffectId);
            if (dampingEffectId >= 0) SDL_DestroyHapticEffect(haptic, dampingEffectId);
            if (frictionEffectId >= 0) SDL_DestroyHapticEffect(haptic, frictionEffectId);
            if (sineEffectId >= 0) SDL_DestroyHapticEffect(haptic, sineEffectId);
            if (shakeEffectId >= 0) SDL_DestroyHapticEffect(haptic, shakeEffectId);

            if (haptic != IntPtr.Zero) SDL_CloseHaptic(haptic);
            if (joystick != IntPtr.Zero) SDL_CloseJoystick(joystick);

            SDL_Quit();
        }

        public string GetLastError() => Marshal.PtrToStringAnsi(SDL_GetError()) ?? "";
    }
}